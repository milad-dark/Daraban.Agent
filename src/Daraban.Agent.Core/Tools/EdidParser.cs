namespace Daraban.Agent.Core.Tools;

/// <summary>
/// Parses the 128-byte base block of a VESA EDID structure, mirroring what
/// glpi-agent extracts for monitor inventories. Shared by the Windows (registry),
/// Linux (/sys/class/drm), and macOS (system_profiler) collectors.
/// </summary>
public static class EdidParser
{
    /// <summary>Minimum plausible EDID base-block size.</summary>
    public const int MinBlockSize = 128;

    /// <summary>Parsed fields from an EDID base block.</summary>
    public sealed record EdidInfo(
        string? Manufacturer,   // 3-letter PNP manufacturer code, e.g. "DEL", "AOC"
        string? PnpId,          // same 3-letter code (EDID bytes 8-10)
        string? Serial,         // 32-bit serial number as decimal string (bytes 11-12 little-endian... see Parse)
        int? WidthCm,           // max horizontal image size in cm (byte 21)
        int? HeightCm);         // max vertical image size in cm (byte 22)

    /// <summary>
    /// Parses an EDID base block. Returns null when the data is absent, too short,
    /// or lacks the 0x00 0xFF 0xFF 0xFF 0xFF 0xFF 0xFF 0xFF header signature.
    /// </summary>
    public static EdidInfo? Parse(byte[]? edid)
    {
        if (edid is null || edid.Length < MinBlockSize)
            return null;

        // Fixed 8-byte header: 00 FF FF FF FF FF FF 00
        if (edid[0] != 0x00 || edid[1] != 0xFF || edid[2] != 0xFF || edid[3] != 0xFF ||
            edid[4] != 0xFF || edid[5] != 0xFF || edid[6] != 0xFF || edid[7] != 0x00)
            return null;

        // Bytes 8-10: manufacturer ID big-endian — 15 bits, five 5-bit letters (A=1..Z=26).
        var mfgWord = (edid[8] << 8) | edid[9];
        var manufacturer = new string(
        [
            (char)('A' + ((mfgWord >> 10) & 0x1F) - 1),
            (char)('A' + ((mfgWord >> 5) & 0x1F) - 1),
            (char)('A' + (mfgWord & 0x1F) - 1),
        ]);

        // Bytes 10-11: product code (little-endian, not reported by glpi-agent).
        // Bytes 12-15: 32-bit serial number, little-endian.
        uint serialNumber = edid[12] | ((uint)edid[13] << 8) | ((uint)edid[14] << 16) | ((uint)edid[15] << 24);

        // Bytes 21-22: max horizontal / vertical image size in centimetres.
        int? width = edid[21] > 0 ? edid[21] : null;
        int? height = edid[22] > 0 ? edid[22] : null;

        return new EdidInfo(manufacturer, manufacturer, serialNumber.ToString(), width, height);
    }
}
