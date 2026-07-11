using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Sweeps an IP range (ICMP ping) to find live hosts, then enriches with MAC/hostname,
/// the same first step the real glpi-agent NetDiscovery task performs before NetInventory
/// runs SNMP against whatever answered.
///
/// Two things this version fixes vs. the first pass:
///  1. The agent's own IP never has a MAC in the OS's ARP/neighbor table (you don't ARP
///     yourself) — that MAC now comes from the local NetworkInterface list instead.
///  2. The ARP/neighbor table is read ONCE in bulk instead of shelling out to arp/ip once
///     per address — faster, and avoids per-process race conditions that occasionally
///     dropped entries.
/// It also merges in devices that are present in the ARP cache but didn't answer ICMP
/// (some phones/IoT devices block ping but still show up from recent traffic) — closer
/// to what passive tools like GlassWire show, though still not identical: GlassWire keeps
/// a *historical* device list; this is still a point-in-time sweep.
/// </summary>
public sealed class NetDiscoveryTask : IAgentTask
{
    public string Name => "netdiscovery";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        var range = options.IpRange;
        if (string.IsNullOrWhiteSpace(range))
        {
            Console.WriteLine("[netdiscovery] No --ip-range configured (e.g. 192.168.1.0/24); skipped.");
            return;
        }

        var addresses = ExpandCidr(range).ToList();
        Console.WriteLine($"[netdiscovery] Scanning {addresses.Count} addresses in {range} ...");

        // Read once, up front — cheaper than one process spawn per address, and this is
        // also what makes the own-IP-MAC fix possible (see BuildLocalMacTable below).
        var arpTable = ReadArpTable();
        var localMacs = BuildLocalMacTable();

        var results = new List<DiscoveredHost>();
        using var throttle = new SemaphoreSlim(Math.Max(1, options.DiscoveryThreads));

        var tasks = addresses.Select(async ip =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var host = await ProbeAsync(ip, arpTable, localMacs, ct);
                lock (results)
                    results.Add(host);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Merge in ARP-only entries: devices the OS already knows about (recent traffic)
        // that didn't answer ICMP in this sweep — e.g. phones/IoT devices that block ping.
        var seenIps = new HashSet<string>(results.Select(r => r.IpAddress));
        foreach (var (ip, mac) in arpTable)
        {
            if (seenIps.Contains(ip))
                continue;
            if (!addresses.Any(a => a.ToString() == ip))
                continue; // stay within the requested range

            results.Add(new DiscoveredHost
            {
                IpAddress = ip,
                MacAddress = mac,
                Responded = false, // didn't answer ICMP, but is a known device on the LAN
                Hostname = await ReverseDnsAsync(IPAddress.Parse(ip), ct)
            });
        }

        var known = results.Where(r => r.Responded || r.MacAddress is not null)
                            .OrderBy(r => ParseForSort(r.IpAddress))
                            .ToList();

        Console.WriteLine($"[netdiscovery] {results.Count(r => r.Responded)} host(s) answered ICMP; " +
                           $"{known.Count} total known host(s) including ARP-only entries.");

        await DeliverAsync(options, known, ct);
    }

    private static async Task<DiscoveredHost> ProbeAsync(
        IPAddress ip, Dictionary<string, string> arpTable, Dictionary<string, string> localMacs, CancellationToken ct)
    {
        var host = new DiscoveredHost { IpAddress = ip.ToString() };
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, timeout: 1500);
            host.Responded = reply.Status == IPStatus.Success;
            host.RoundtripMs = reply.RoundtripTime;

            if (host.Responded)
            {
                // Own IP: not in the ARP table (you don't ARP yourself) — use the local
                // interface's real MAC instead. Everyone else: look up in the ARP table
                // we already read in bulk.
                host.MacAddress = localMacs.GetValueOrDefault(host.IpAddress)
                                   ?? arpTable.GetValueOrDefault(host.IpAddress);

                host.Hostname = await ReverseDnsAsync(ip, ct);
            }
        }
        catch
        {
            host.Responded = false;
        }
        return host;
    }

    /// <summary>Own machine's IP → MAC, straight from the network interfaces — never comes from ARP.</summary>
    private static Dictionary<string, string> BuildLocalMacTable()
    {
        var table = new Dictionary<string, string>();
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up)
                    continue;
                var mac = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                if (string.IsNullOrEmpty(mac) || mac.Replace(":", "") == "000000000000")
                    continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        table[addr.Address.ToString()] = mac;
                }
            }
        }
        catch
        {
            // best-effort — an empty table just means we fall back to ARP for everything
        }
        return table;
    }

    /// <summary>Reads the whole ARP/neighbor table once, instead of shelling out per address.</summary>
    private static Dictionary<string, string> ReadArpTable()
    {
        var table = new Dictionary<string, string>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = RunCommand("arp", "-a");
                foreach (Match m in Regex.Matches(output, @"(?<ip>\d+\.\d+\.\d+\.\d+)\s+(?<mac>([0-9a-fA-F]{2}-){5}[0-9a-fA-F]{2})"))
                    table[m.Groups["ip"].Value] = m.Groups["mac"].Value.Replace('-', ':').ToUpperInvariant();
            }
            else
            {
                // Linux: "ip neigh show" — macOS/BSD: "arp -an"
                var output = RunCommand("ip", "neigh show");
                if (string.IsNullOrWhiteSpace(output))
                    output = RunCommand("arp", "-an");

                foreach (Match m in Regex.Matches(output,
                    @"(?<ip>\d+\.\d+\.\d+\.\d+)\D+(?<mac>([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2})"))
                    table[m.Groups["ip"].Value] = m.Groups["mac"].Value.ToUpperInvariant();
            }
        }
        catch
        {
            // best-effort — an empty table just means MAC lookups fall back to null for everyone
        }
        return table;
    }

    private static string RunCommand(string cmd, string args)
    {
        try
        {
            using var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<string?> ReverseDnsAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            var name = entry.HostName;

            // Docker's embedded DNS resolver can leak this name through to reverse lookups
            // when the agent (or its host) has Docker networking involved — it is never a
            // real LAN device name, so treat it the same as "no hostname found".
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith(".docker.internal", StringComparison.OrdinalIgnoreCase))
                return null;

            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Expands a CIDR block ("192.168.1.0/24") into its usable host addresses.</summary>
    internal static IEnumerable<IPAddress> ExpandCidr(string cidr)
    {
        var parts = cidr.Split('/');
        var baseIp = IPAddress.Parse(parts[0]);
        var prefixLen = parts.Length > 1 ? int.Parse(parts[1]) : 32;

        var baseBytes = baseIp.GetAddressBytes();
        if (baseBytes.Length != 4)
            throw new NotSupportedException("Only IPv4 ranges are supported.");

        uint baseInt = (uint)(baseBytes[0] << 24 | baseBytes[1] << 16 | baseBytes[2] << 8 | baseBytes[3]);
        uint mask = prefixLen == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLen);
        uint network = baseInt & mask;
        uint broadcast = network | ~mask;

        uint first = prefixLen >= 31 ? network : network + 1;
        uint last = prefixLen >= 31 ? broadcast : broadcast - 1;

        for (uint i = first; i <= last; i++)
        {
            yield return new IPAddress(new byte[]
            {
                (byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i
            });
        }
    }

    private static uint ParseForSort(string ip)
    {
        var b = IPAddress.Parse(ip).GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static async Task DeliverAsync(AgentOptions options, List<DiscoveredHost> hosts, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            Directory.CreateDirectory(options.Local);
            var file = Path.Combine(options.Local, $"netdiscovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(hosts, new JsonSerializerOptions { WriteIndented = true }), ct);
            Console.WriteLine($"[netdiscovery] Results written to {file}");
        }
        else if (!string.IsNullOrWhiteSpace(options.Server))
        {
            var client = DarabanClientFactory.Create(options);
            await client.PostDiscoveryAsync(options.Tag ?? Environment.MachineName, hosts, ct);
            Console.WriteLine("[netdiscovery] Results sent to server.");
        }
        else
        {
            Console.WriteLine("[netdiscovery] No server or local path configured; results kept in memory only.");
        }
    }
}