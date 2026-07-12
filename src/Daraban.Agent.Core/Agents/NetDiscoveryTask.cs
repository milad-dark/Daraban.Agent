using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Agents;

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

        // ── PASS 1: ICMP + ARP — exactly your original logic ─────────────────
        // Read ARP table once up front, same as before
        var arpTable = ReadArpTable();
        var localMacs = BuildLocalMacTable();

        var results = new List<DiscoveredHost>();
        using var throttle = new SemaphoreSlim(Math.Max(1, options.DiscoveryThreads));

        var probeTasks = addresses.Select(async ip =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                // ProbeAsync is unchanged from your original — ICMP only
                var host = await ProbeAsync(ip, arpTable, localMacs, ct);
                lock (results)
                    results.Add(host);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(probeTasks);

        // Merge ARP-only entries — unchanged from your original
        var seenIps = new HashSet<string>(results.Select(r => r.IpAddress));
        foreach (var (ip, mac) in arpTable)
        {
            if (seenIps.Contains(ip)) continue;
            if (!addresses.Any(a => a.ToString() == ip)) continue;

            results.Add(new DiscoveredHost
            {
                IpAddress = ip,
                MacAddress = mac,
                Responded = false,
                Hostname = await ReverseDnsAsync(IPAddress.Parse(ip), ct)
            });
        }

        // Candidates = hosts that actually exist on the network
        // Same filter as your original
        var candidates = results
            .Where(r => r.Responded || r.MacAddress is not null)
            .ToList();

        Console.WriteLine(
            $"[netdiscovery] {results.Count(r => r.Responded)} host(s) answered ICMP; " +
            $"{candidates.Count} total known host(s) including ARP-only entries.");

        // ── PASS 2: SNMP fingerprint — only on candidates, not every IP ───────
        // This is why the original broke: SNMP was called on ALL 254 addresses.
        // Now it only runs on hosts we already know exist.
        if (candidates.Count > 0)
        {
            Console.WriteLine($"[netdiscovery] Running SNMP fingerprint on {candidates.Count} known host(s) ...");

            using var snmpThrottle = new SemaphoreSlim(Math.Max(1, options.DiscoveryThreads));

            var snmpTasks = candidates.Select(async host =>
            {
                await snmpThrottle.WaitAsync(ct);
                try
                {
                    var fingerprint = await SnmpNetworkCollector.ProbeForDiscoveryAsync(
                        host.IpAddress, options.SnmpCommunity, options.SnmpTimeoutMs);

                    if (fingerprint is null) return;

                    // Enrich the host in place — lock because candidates is shared
                    lock (host)
                    {
                        host.SnmpReachable = true;
                        host.SysDescr = fingerprint.SysDescr;
                        host.SysObjectId = fingerprint.SysObjectId;
                        host.DeviceType = FingerprintDevice(
                            fingerprint.SysDescr, fingerprint.SysObjectId);

                        // Prefer SNMP sysName over reverse DNS — it is set on the device itself
                        if (!string.IsNullOrWhiteSpace(fingerprint.SysName))
                            host.Hostname = fingerprint.SysName;
                    }
                }
                finally
                {
                    snmpThrottle.Release();
                }
            });

            await Task.WhenAll(snmpTasks);

            var snmpCount = candidates.Count(h => h.SnmpReachable);
            Console.WriteLine($"[netdiscovery] {snmpCount} host(s) answered SNMP.");
        }

        var known = candidates
            .OrderBy(r => ParseForSort(r.IpAddress))
            .ToList();

        await DeliverAsync(options, known, ct);
    }

    // ── ProbeAsync — IDENTICAL to your original ───────────────────────────────
    // No SNMP here — SNMP is Pass 2 in RunAsync above
    private static async Task<DiscoveredHost> ProbeAsync(
        IPAddress ip,
        Dictionary<string, string> arpTable,
        Dictionary<string, string> localMacs,
        CancellationToken ct)
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

    // ── FingerprintDevice ─────────────────────────────────────────────────────
    private static string? FingerprintDevice(string? sysDescr, string? sysObjectId)
    {
        if (string.IsNullOrWhiteSpace(sysDescr) && string.IsNullOrWhiteSpace(sysObjectId))
            return null;

        var desc = (sysDescr ?? string.Empty).ToLowerInvariant();

        if (desc.Contains("printer") || desc.Contains("jetdirect") ||
            desc.Contains("laserjet") || desc.Contains("officejet"))
            return "Printer";

        if (desc.Contains("cisco") || desc.Contains("catalyst") ||
            desc.Contains("juniper") || desc.Contains("extreme") ||
            desc.Contains("procurve") || desc.Contains("ios"))
            return "NetworkDevice";

        if (desc.Contains("esxi") || desc.Contains("vmware"))
            return "VirtualMachineHost";

        if (desc.Contains("windows") || desc.Contains("microsoft"))
            return "Computer";

        if (desc.Contains("linux") || desc.Contains("ubuntu") ||
            desc.Contains("debian") || desc.Contains("centos") ||
            desc.Contains("red hat") || desc.Contains("freebsd"))
            return "Computer";

        if (desc.Contains("ups") || desc.Contains("powerware") ||
            desc.Contains("apc ") || desc.Contains("eaton"))
            return "PowerDevice";

        if (desc.Contains("storage") || desc.Contains("nas") ||
            desc.Contains("qnap") || desc.Contains("synology"))
            return "Storage";

        if (desc.Contains("camera") || desc.Contains("axis"))
            return "Camera";

        if (desc.Contains("voip") || desc.Contains("phone") ||
            desc.Contains("asterisk"))
            return "Phone";

        if (!string.IsNullOrWhiteSpace(sysObjectId))
        {
            if (sysObjectId.StartsWith("1.3.6.1.4.1.9.")) return "NetworkDevice"; // Cisco
            if (sysObjectId.StartsWith("1.3.6.1.4.1.11.")) return "Printer";        // HP
            if (sysObjectId.StartsWith("1.3.6.1.4.1.2636.")) return "NetworkDevice"; // Juniper
            if (sysObjectId.StartsWith("1.3.6.1.4.1.318.")) return "PowerDevice";   // APC
            if (sysObjectId.StartsWith("1.3.6.1.4.1.232.")) return "Computer";      // HPE
            if (sysObjectId.StartsWith("1.3.6.1.4.1.674.")) return "Computer";      // Dell
            if (sysObjectId.StartsWith("1.3.6.1.4.1.6027.")) return "NetworkDevice"; // Force10
            if (sysObjectId.StartsWith("1.3.6.1.4.1.1916.")) return "NetworkDevice"; // Extreme
        }

        return "Unknown";
    }

    // ── Everything below is IDENTICAL to your original ────────────────────────

    private static Dictionary<string, string> BuildLocalMacTable()
    {
        var table = new Dictionary<string, string>();
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                var mac = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes()
                    .Select(b => b.ToString("X2")));
                if (string.IsNullOrEmpty(mac) || mac.Replace(":", "") == "000000000000") continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        table[addr.Address.ToString()] = mac;
                }
            }
        }
        catch { }
        return table;
    }

    private static Dictionary<string, string> ReadArpTable()
    {
        var table = new Dictionary<string, string>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = RunCommand("arp", "-a");
                foreach (Match m in Regex.Matches(output,
                    @"(?<ip>\d+\.\d+\.\d+\.\d+)\s+(?<mac>([0-9a-fA-F]{2}-){5}[0-9a-fA-F]{2})"))
                    table[m.Groups["ip"].Value] =
                        m.Groups["mac"].Value.Replace('-', ':').ToUpperInvariant();
            }
            else
            {
                var output = RunCommand("ip", "neigh show");
                if (string.IsNullOrWhiteSpace(output))
                    output = RunCommand("arp", "-an");

                foreach (Match m in Regex.Matches(output,
                    @"(?<ip>\d+\.\d+\.\d+\.\d+)\D+(?<mac>([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2})"))
                    table[m.Groups["ip"].Value] = m.Groups["mac"].Value.ToUpperInvariant();
            }
        }
        catch { }
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
        catch { return string.Empty; }
    }

    private static async Task<string?> ReverseDnsAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            var name = entry.HostName;
            if (string.IsNullOrWhiteSpace(name) ||
                name.EndsWith(".docker.internal", StringComparison.OrdinalIgnoreCase))
                return null;
            return name;
        }
        catch { return null; }
    }

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
            yield return new IPAddress(new byte[]
                { (byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i });
    }

    private static uint ParseForSort(string ip)
    {
        var b = IPAddress.Parse(ip).GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static async Task DeliverAsync(
        AgentOptions options, List<DiscoveredHost> hosts, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            Directory.CreateDirectory(options.Local);
            var file = Path.Combine(options.Local,
                $"netdiscovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file,
                JsonSerializer.Serialize(hosts,
                    new JsonSerializerOptions { WriteIndented = true }), ct);
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