using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Collectors;

/// <summary>Builds and sends the standard 102-byte WoL "magic packet" over UDP broadcast.</summary>
public static class WakeOnLanSender
{
    public static void Send(string macAddress, string? broadcastAddress = null, int port = 9)
    {
        var mac = ParseMac(macAddress);
        var packet = BuildMagicPacket(mac);

        var broadcast = IPAddress.Parse(string.IsNullOrWhiteSpace(broadcastAddress) ? "255.255.255.255" : broadcastAddress);

        using var client = new UdpClient();
        client.EnableBroadcast = true;
        client.Send(packet, packet.Length, new IPEndPoint(broadcast, port));
    }

    private static byte[] BuildMagicPacket(byte[] mac)
    {
        // 6 bytes of 0xFF followed by the target MAC repeated 16 times.
        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++)
            packet[i] = 0xFF;
        for (int i = 0; i < 16; i++)
            Buffer.BlockCopy(mac, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    private static byte[] ParseMac(string macAddress)
    {
        var cleaned = Regex.Replace(macAddress, "[^0-9A-Fa-f]", "");
        if (cleaned.Length != 12)
            throw new ArgumentException($"'{macAddress}' is not a valid MAC address.", nameof(macAddress));

        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        return bytes;
    }
}
