using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Reverse DNS and mDNS both fail on Windows/Android/iOS's randomized "private" MACs because
/// there's no stable identity to resolve against. But MAC randomization only scrambles the
/// Layer-2 address — the device's real name still goes out in plain text every time it asks
/// for a DHCP lease, in the "Host Name" option (option code 12). This listens passively for
/// that broadcast traffic and builds a MAC → hostname map from it, which is the actual
/// technique GlassWire/Fing/similar tools use to show real names despite MAC randomization.
///
/// Requirements / limitations:
///  - Needs admin/root to bind UDP port 67 (a privileged port).
///  - Only sees a device's name at the moment it (re)negotiates its DHCP lease — most devices
///    renew every few hours, not every few seconds, so a short one-shot scan will often catch
///    nothing. Run <see cref="StartBackgroundListener"/> once for the agent's whole lifetime
///    (not per-scan) so it accumulates hits over hours/days, the same way GlassWire — which
///    runs continuously in the background — builds its device list over time.
///  - Only sees devices on the same L2 broadcast domain as the machine running the agent
///    (same WiFi/switch segment) — won't see anything across routed subnets/VLANs.
/// </summary>
public static class DhcpHostnameSniffer
{
    /// <summary>Accumulates MAC (upper-case, colon-separated) → last-seen hostname. Thread-safe, shared for the process lifetime.</summary>
    public static ConcurrentDictionary<string, string> HostnameByMac { get; } = new();

    private static int _started;

    /// <summary>Starts listening in the background for the remaining lifetime of the process. Safe to call more than once — only starts once.</summary>
    public static void StartBackgroundListener(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ListenOnceAsync(ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[dhcp-sniff] Listener stopped unexpectedly, restarting in 30s: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
            }
        }, ct);
    }

    /// <summary>One-shot listen for a fixed window — useful for quick manual testing, but see the class doc for why this rarely catches much.</summary>
    public static async Task<int> ListenForAsync(TimeSpan duration, CancellationToken ct)
    {
        var before = HostnameByMac.Count;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(duration);
        try
        {
            await ListenOnceAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) { /* expected at the end of the window */ }
        return HostnameByMac.Count - before;
    }

    private static async Task ListenOnceAsync(CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 67)); // DHCP server port — broadcast requests land here too
        Console.WriteLine("[dhcp-sniff] Listening for DHCP Host Name (option 12) broadcasts on UDP/67 ...");

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var parsed = TryParseDhcpHostname(result.Buffer);
            if (parsed is { Hostname.Length: > 0 } p)
            {
                HostnameByMac[p.Mac] = p.Hostname;
                Console.WriteLine($"[dhcp-sniff] {p.Mac} -> \"{p.Hostname}\"");
            }
        }
    }

    /// <summary>Parses a raw BOOTP/DHCP packet, returning (client MAC, Option-12 hostname) if present.</summary>
    private static (string Mac, string? Hostname)? TryParseDhcpHostname(byte[] data)
    {
        // BOOTP header: op(1) htype(1) hlen(1) hops(1) xid(4) secs(2) flags(2)
        //               ciaddr(4) yiaddr(4) siaddr(4) giaddr(4) chaddr(16) sname(64) file(128)
        //               magic cookie(4) then DHCP options.
        if (data.Length < 240) return null;

        byte hlen = data[2];
        int macLen = Math.Min(hlen, (byte)16);
        var chaddr = data.Skip(28).Take(macLen).ToArray();
        var mac = string.Join(":", chaddr.Select(b => b.ToString("X2")));

        // Magic cookie 99.130.83.99 marks the start of DHCP (vs. plain BOOTP) options.
        if (data[236] != 99 || data[237] != 130 || data[238] != 83 || data[239] != 99)
            return null;

        int pos = 240;
        string? hostname = null;

        while (pos < data.Length)
        {
            byte option = data[pos];
            if (option == 255) break;       // End option
            if (option == 0) { pos++; continue; } // Pad option
            if (pos + 1 >= data.Length) break;

            byte len = data[pos + 1];
            if (pos + 2 + len > data.Length) break;

            if (option == 12) // Host Name
                hostname = Encoding.ASCII.GetString(data, pos + 2, len).Trim('\0', ' ');

            pos += 2 + len;
        }

        return (mac, hostname);
    }
}