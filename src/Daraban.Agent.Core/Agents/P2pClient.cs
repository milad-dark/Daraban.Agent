using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Client side of the P2P deploy protocol: before pulling a deploy file from the central
/// server, ask peer agents on the same subnet whether they already have the file staged
/// (keyed by SHA-256) and, if so, stream it from them instead (mirrors glpi-agent's
/// p2p-ratio / peer preference).
///
/// The peer list is remembered between runs: <see cref="NetDiscoveryTask"/> publishes
/// every host it finds to <see cref="RememberPeers"/>, and <see cref="TryDownloadAsync"/>
/// additionally probes the local ARP table for same-subnet neighbors.
///
/// Integrity is unchanged: bytes received from a peer are verified against the manifest
/// SHA-256 before they are trusted; on mismatch the file is discarded and the server
/// download path runs as usual.
/// </summary>
public static class P2pClient
{
    private static readonly object PeerLock = new();
    private static readonly List<string> KnownPeers = new();

    /// <summary>
    /// Default per-peer request timeout in milliseconds.
    /// </summary>
    public const int DefaultTimeoutMs = 4000;

    /// <summary>
    /// Adds hosts discovered by netdiscovery (or any other source) to the peer cache.
    /// Duplicates and non-IPv4 entries are ignored; the list is capped so a huge
    /// discovery sweep cannot make deploy downloads try hundreds of peers.
    /// </summary>
    /// <param name="ips">Discovered peer IPv4 addresses.</param>
    /// <param name="maxPeers">Cache cap (oldest entries beyond the cap are dropped).</param>
    public static void RememberPeers(IEnumerable<string> ips, int maxPeers = 64)
    {
        lock (PeerLock)
        {
            foreach (var ip in ips)
            {
                if (string.IsNullOrWhiteSpace(ip) ||
                    !IPAddress.TryParse(ip, out var parsed) ||
                    parsed.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var normalized = parsed.ToString();
                KnownPeers.Remove(normalized);
                KnownPeers.Add(normalized);
            }

            if (KnownPeers.Count > maxPeers)
                KnownPeers.RemoveRange(0, KnownPeers.Count - maxPeers);
        }
    }

    /// <summary>
    /// Snapshot of the current peer cache (most recently seen peers last).
    /// </summary>
    public static IReadOnlyList<string> PeekPeers()
    {
        lock (PeerLock)
            return KnownPeers.ToArray();
    }

    /// <summary>
    /// Empties the peer cache (used by tests and when the subnet changes).
    /// </summary>
    public static void ForgetPeers()
    {
        lock (PeerLock)
            KnownPeers.Clear();
    }

    /// <summary>
    /// Tries to download a deploy file from same-subnet peers before the central server.
    /// </summary>
    /// <param name="sha256">Expected SHA-256 of the file (peer content is verified against it).</param>
    /// <param name="destPath">Destination path; a failed attempt leaves no partial file behind.</param>
    /// <param name="peerIps">Candidate peer IPs to try, in order.</param>
    /// <param name="timeoutMs">Per-peer HTTP timeout in milliseconds.</param>
    /// <param name="ct">Cancellation token for the overall operation.</param>
    /// <returns>"ip:port" of the peer that served verified content, or null when no peer
    /// could supply the file (caller falls back to the server download).</returns>
    public static async Task<string?> TryDownloadAsync(
        string sha256,
        string destPath,
        List<string>? peerIps,
        int timeoutMs,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sha256) ||
            peerIps is not { Count: > 0 })
            return null;

        var port = P2pClientPort;
        if (port <= 0)
            return null;

        foreach (var peer in peerIps.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            if (!IPAddress.TryParse(peer, out var peerIp) ||
                peerIp.AddressFamily != AddressFamily.InterNetwork ||
                !IsSameSubnet(peerIp))
                continue;

            var url = $"http://{peer}:{port}/{sha256.ToLowerInvariant()}";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(Math.Max(500, timeoutMs)) };
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Console.WriteLine($"[p2p-client] {peer} answered {(int)response.StatusCode} for {sha256[..Math.Min(12, sha256.Length)]}…; trying next peer.");
                    continue;
                }

                // Stream to a temp file first so a failed transfer never clobbers destPath
                // and never leaves a truncated file that a later install could pick up.
                var tempPath = destPath + ".p2p-part";
                await using (var stream = await response.Content.ReadAsStreamAsync(ct))
                await using (var fs = File.Create(tempPath))
                {
                    await stream.CopyToAsync(fs, ct);
                }

                var actual = await ComputeSha256Async(tempPath, ct);
                if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    Console.WriteLine($"[p2p-client] {peer} served corrupted content (hash mismatch); trying next peer.");
                    continue;
                }

                File.Move(tempPath, destPath, overwrite: true);
                Console.WriteLine($"[p2p-client] Got {sha256[..Math.Min(12, sha256.Length)]}… from peer {peer}:{port}.");
                return $"{peer}:{port}";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per-peer HttpClient timeout — move on to the next peer.
                Console.WriteLine($"[p2p-client] {peer} timed out; trying next peer.");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
            {
                Console.WriteLine($"[p2p-client] {peer} unreachable ({ex.GetType().Name}); trying next peer.");
            }
        }

        return null;
    }

    /// <summary>
    /// Port P2P peers listen on. Static (not from AgentOptions) because the client must
    /// use the same port for every peer; CLI --p2p-port sets it via <see cref="ConfigurePort"/>.
    /// </summary>
    public static int P2pClientPort { get; private set; } = 62355;

    /// <summary>
    /// Sets the port used both for outgoing peer requests and advertised to peers.
    /// </summary>
    public static void ConfigurePort(int port) => P2pClientPort = port;

    /// <summary>
    /// True when <paramref name="address"/> is in the same IPv4 /24 subnet as one of this
    /// machine's local unicast addresses. The P2P trust boundary — glpi-agent only shares
    /// between agents on the same network, and so do we.
    /// </summary>
    public static bool IsSameSubnet(IPAddress? address)
    {
        if (address is null)
            return false;

        var candidate = Normalize(address);
        if (candidate is null)
            return false;

        foreach (var iface in SafeLocalAddresses())
        {
            var local = Normalize(iface);
            if (local is null)
                continue;

            if (SameV4Subnet(local, candidate, prefixBits: 24))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gathers same-subnet candidates for a deploy download: the remembered peer cache
    /// (fed by netdiscovery) plus the current ARP table, deduplicated, subnet-filtered,
    /// excluding this machine's own addresses.
    /// </summary>
    public static List<string> GetCandidatePeers()
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in PeekPeers().Concat(ReadArpIps()))
        {
            if (!IPAddress.TryParse(source, out var ip) ||
                ip.AddressFamily != AddressFamily.InterNetwork ||
                seen.Contains(source) ||
                !IsSameSubnet(ip))
                continue;

            if (IsOwnAddress(ip))
                continue;

            seen.Add(source);
            candidates.Add(source);
        }
        return candidates;
    }

    private static bool IsOwnAddress(IPAddress candidate)
    {
        var c = Normalize(candidate);
        if (c is null)
            return false;

        return SafeLocalAddresses().Any(local =>
            Normalize(local) is { } l && l.GetAddressBytes().AsSpan().SequenceEqual(c.GetAddressBytes()));
    }

    private static IEnumerable<IPAddress> SafeLocalAddresses()
    {
        List<IPAddress> addresses;
        try
        {
            addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == OperationalStatus.Up)
                .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .ToList();
        }
        catch
        {
            addresses = [];
        }

        foreach (var a in addresses)
            yield return a;
    }

    private static IPAddress? Normalize(IPAddress address)
    {
        // Unmap BEFORE the family gate: Kestrel reports IPv4 peers on dual-mode sockets as
        // IPv4-mapped IPv6 (::ffff:a.b.c.d, AddressFamily InterNetworkV6) — exactly what
        // ctx.Connection.RemoteIpAddress gives the P2P server handler.
        var mapped = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return mapped.AddressFamily == AddressFamily.InterNetwork ? mapped : null;
    }

    private static bool SameV4Subnet(IPAddress a, IPAddress b, int prefixBits)
    {
        var bytesA = a.GetAddressBytes();
        var bytesB = b.GetAddressBytes();
        if (bytesA.Length != 4 || bytesB.Length != 4)
            return false;

        int fullBytes = prefixBits / 8;
        for (var i = 0; i < fullBytes && i < 4; i++)
            if (bytesA[i] != bytesB[i])
                return false;

        var rem = prefixBits % 8;
        if (rem > 0 && fullBytes < 4)
        {
            var mask = (byte)(0xFF << (8 - rem));
            if ((bytesA[fullBytes] & mask) != (bytesB[fullBytes] & mask))
                return false;
        }
        return true;
    }

    private static IEnumerable<string> ReadArpIps()
    {
        var output = OperatingSystem.IsWindows() ? RunCommand("arp", "-a") : RunCommand("ip", "neigh show");
        if (string.IsNullOrWhiteSpace(output))
            output = RunCommand("arp", "-an");

        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(
                     output,
                     @"(?<ip>\d+\.\d+\.\d+\.\d+)"))
        {
            yield return m.Groups["ip"].Value;
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
            p.WaitForExit(3000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
