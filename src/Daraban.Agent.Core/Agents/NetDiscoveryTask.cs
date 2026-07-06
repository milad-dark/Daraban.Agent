using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Sweeps an IP range (ICMP ping + best-effort ARP/reverse-DNS) to find live hosts,
/// the same first step the real glpi-agent NetDiscovery task performs before NetInventory
/// runs SNMP against whatever answered.
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

        var results = new List<DiscoveredHost>();
        using var throttle = new SemaphoreSlim(Math.Max(1, options.DiscoveryThreads));

        var tasks = addresses.Select(async ip =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                results.Add(await ProbeAsync(ip, ct));
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        var alive = results.Where(r => r.Responded).OrderBy(r => r.IpAddress, StringComparer.Ordinal).ToList();
        Console.WriteLine($"[netdiscovery] {alive.Count} host(s) responded.");

        await DeliverAsync(options, alive, ct);
    }

    private static async Task<DiscoveredHost> ProbeAsync(IPAddress ip, CancellationToken ct)
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
                host.MacAddress = LookupArp(ip);
                host.Hostname = await ReverseDnsAsync(ip, ct);
            }
        }
        catch
        {
            host.Responded = false;
        }
        return host;
    }

    /// <summary>
    /// Reads the OS ARP/neighbor table after a successful ping instead of crafting raw ARP
    /// frames (which need raw sockets/admin rights); this mirrors what "arp -a" already knows.
    /// </summary>
    private static string? LookupArp(IPAddress ip)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = RunCommand("arp", $"-a {ip}");
                var m = System.Text.RegularExpressions.Regex.Match(output, @"([0-9a-fA-F]{2}-){5}[0-9a-fA-F]{2}");
                return m.Success ? m.Value.Replace('-', ':').ToUpperInvariant() : null;
            }
            else
            {
                // Linux/macOS: "ip neigh show <ip>" or fallback to "arp -n <ip>"
                var output = RunCommand("ip", $"neigh show {ip}");
                var m = System.Text.RegularExpressions.Regex.Match(output, @"([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}");
                if (m.Success)
                    return m.Value.ToUpperInvariant();

                output = RunCommand("arp", $"-n {ip}");
                m = System.Text.RegularExpressions.Regex.Match(output, @"([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}");
                return m.Success ? m.Value.ToUpperInvariant() : null;
            }
        }
        catch
        {
            return null;
        }
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
            p.WaitForExit(2000);
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
            return entry.HostName;
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

        // Skip network/broadcast addresses for anything larger than a /31 or /32.
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

    private static async Task DeliverAsync(AgentOptions options, List<DiscoveredHost> alive, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            Directory.CreateDirectory(options.Local);
            var file = Path.Combine(options.Local, $"netdiscovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(alive, new JsonSerializerOptions { WriteIndented = true }), ct);
            Console.WriteLine($"[netdiscovery] Results written to {file}");
        }
        else if (!string.IsNullOrWhiteSpace(options.Server))
        {
            var client = DarabanClientFactory.Create(options);
            await client.PostDiscoveryAsync(options.Tag ?? Environment.MachineName, alive, ct);
            Console.WriteLine("[netdiscovery] Results sent to server.");
        }
        else
        {
            Console.WriteLine("[netdiscovery] No server or local path configured; results kept in memory only.");
        }
    }
}
