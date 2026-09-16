using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Standard reverse DNS (System.Net.Dns) only resolves names that your router's DNS server
/// happens to publish — which in practice is almost always just Windows PCs (their hostname
/// gets registered via DHCP, and Windows' resolver falls back to NetBIOS on top of that).
/// Phones and most IoT devices advertise their name over mDNS/Bonjour instead (the ".local"
/// name you see in Finder/Bonjour Browser), which System.Net.Dns never queries.
///
/// This sends a one-shot unicast mDNS PTR query straight to the device's port 5353 asking
/// for the reverse ("in-addr.arpa") record. Apple devices and Avahi-enabled Linux/IoT boxes
/// generally answer this reliably. Android's mDNS support varies by OEM/vendor and is not
/// guaranteed to answer — treat a null result as "no name available", not as a bug.
/// </summary>
public static class MdnsResolver
{
    public static async Task<string?> TryResolveAsync(IPAddress ip, int timeoutMs = 300, CancellationToken ct = default)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return null; // IPv4 reverse (in-addr.arpa) only; IPv6 would need ip6.arpa

        try
        {
            var reverseName = string.Join(".", ip.GetAddressBytes().Reverse()) + ".in-addr.arpa";
            var query = BuildPtrQuery(reverseName);

            using var udp = new UdpClient(0) { Client = { ReceiveTimeout = timeoutMs } };
            await udp.SendAsync(query, query.Length, new IPEndPoint(ip, 5353));

            var receiveTask = udp.ReceiveAsync();
            var winner = await Task.WhenAny(receiveTask, Task.Delay(timeoutMs, ct));
            if (winner != receiveTask || !receiveTask.IsCompletedSuccessfully)
                return null;

            var name = ParsePtrAnswer(receiveTask.Result.Buffer);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null; // no responder on 5353, timed out, or malformed reply — all just "no name"
        }
    }

    // ------------------------------------------------------------------
    // Minimal hand-rolled DNS wire format — just enough to build one PTR
    // question and parse one PTR answer back out. Not a general DNS client.
    // ------------------------------------------------------------------

    private static byte[] BuildPtrQuery(string name)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0, 0,      // ID (mDNS ignores this)
                               0, 0,      // flags: standard query
                               0, 1,      // QDCOUNT = 1
                               0, 0, 0, 0, 0, 0 }); // AN/NS/AR COUNT = 0

        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0);              // root label
        ms.Write(new byte[] { 0, 12 }); // QTYPE = PTR
        ms.Write(new byte[] { 0, 1 });  // QCLASS = IN

        return ms.ToArray();
    }

    private static string? ParsePtrAnswer(byte[] data)
    {
        try
        {
            int answerCount = (data[6] << 8) | data[7];
            if (answerCount == 0) return null;

            int pos = 12;
            pos = SkipName(data, pos);
            pos += 4; // QTYPE + QCLASS of the question we asked

            for (int i = 0; i < answerCount; i++)
            {
                pos = SkipName(data, pos);
                int type = (data[pos] << 8) | data[pos + 1]; pos += 2;
                pos += 2; // class
                pos += 4; // ttl
                int rdLength = (data[pos] << 8) | data[pos + 1]; pos += 2;

                if (type == 12) // PTR
                    return ReadName(data, pos).TrimEnd('.');

                pos += rdLength;
            }
        }
        catch
        {
            // malformed/truncated packet — just means no name this time
        }
        return null;
    }

    private static int SkipName(byte[] data, int pos)
    {
        while (true)
        {
            int len = data[pos];
            if (len == 0) return pos + 1;
            if ((len & 0xC0) == 0xC0) return pos + 2; // compression pointer, 2 bytes total
            pos += len + 1;
        }
    }

    private static string ReadName(byte[] data, int pos)
    {
        var sb = new StringBuilder();
        ReadNameRec(data, pos, sb, depth: 0);
        return sb.ToString();
    }

    private static void ReadNameRec(byte[] data, int pos, StringBuilder sb, int depth)
    {
        if (depth > 10) return; // guards against a malicious/corrupt pointer loop
        while (true)
        {
            int len = data[pos];
            if (len == 0) return;

            if ((len & 0xC0) == 0xC0)
            {
                int pointer = ((len & 0x3F) << 8) | data[pos + 1];
                ReadNameRec(data, pointer, sb, depth + 1);
                return;
            }

            sb.Append(Encoding.ASCII.GetString(data, pos + 1, len));
            sb.Append('.');
            pos += len + 1;
        }
    }
}