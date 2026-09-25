using Daraban.Agent.Core.Tools;

namespace Daraban.Agent.Tests;

public class EdidParserTests
{
    /// <summary>Builds a minimal valid 128-byte EDID base block.</summary>
    private static byte[] MakeEdid(
        string manufacturer = "DEL",
        uint serial = 0x11223344,
        byte widthCm = 52,
        byte heightCm = 31)
    {
        var edid = new byte[128];

        // Fixed header 00 FF FF FF FF FF FF 00
        edid[0] = 0x00;
        for (var i = 1; i <= 6; i++) edid[i] = 0xFF;
        edid[7] = 0x00;

        // Manufacturer word: three 5-bit letters, big-endian across bytes 8-9.
        var word = 0;
        foreach (var c in manufacturer)
            word = (word << 5) | (c - 'A' + 1);
        edid[8] = (byte)(word >> 8);
        edid[9] = (byte)(word & 0xFF);

        // Product code (bytes 10-11), serial (bytes 12-15, little-endian).
        edid[10] = 0x34;
        edid[11] = 0x12;
        edid[12] = (byte)(serial & 0xFF);
        edid[13] = (byte)((serial >> 8) & 0xFF);
        edid[14] = (byte)((serial >> 16) & 0xFF);
        edid[15] = (byte)((serial >> 24) & 0xFF);

        // Week (16), year-1990 (17), size (21-22).
        edid[16] = 20;
        edid[17] = 35; // 2025
        edid[21] = widthCm;
        edid[22] = heightCm;

        // Checksum so the block sums to 0 mod 256 (parser doesn't verify, but keep it realistic).
        var sum = 0;
        for (var i = 0; i < 127; i++) sum += edid[i];
        edid[127] = (byte)((256 - sum) & 0xFF);

        return edid;
    }

    [Fact]
    public void Parse_NullOrShort_ReturnsNull()
    {
        Assert.Null(EdidParser.Parse(null));
        Assert.Null(EdidParser.Parse(Array.Empty<byte>()));
        Assert.Null(EdidParser.Parse(new byte[64]));
    }

    [Fact]
    public void Parse_BadHeader_ReturnsNull()
    {
        var edid = MakeEdid();
        edid[1] = 0x00; // break the FF FF FF signature
        Assert.Null(EdidParser.Parse(edid));
    }

    [Fact]
    public void Parse_ValidBlock_ExtractsManufacturer()
    {
        var info = EdidParser.Parse(MakeEdid(manufacturer: "DEL"));
        Assert.NotNull(info);
        Assert.Equal("DEL", info!.Manufacturer);
        Assert.Equal("DEL", info.PnpId);
    }

    [Theory]
    [InlineData("AOC")]
    [InlineData("SAM")]
    [InlineData("AAA")]
    [InlineData("ZZZ")]
    public void Parse_ValidBlock_ExtractsVariousManufacturers(string manufacturer)
    {
        var info = EdidParser.Parse(MakeEdid(manufacturer: manufacturer));
        Assert.NotNull(info);
        Assert.Equal(manufacturer, info!.Manufacturer);
    }

    [Fact]
    public void Parse_ValidBlock_ExtractsSerialNumber()
    {
        var info = EdidParser.Parse(MakeEdid(serial: 0x11223344));
        Assert.NotNull(info);
        // 32-bit serial is little-endian in the block.
        Assert.Equal("287454020", info!.Serial);
    }

    [Fact]
    public void Parse_ValidBlock_ExtractsPhysicalSize()
    {
        var info = EdidParser.Parse(MakeEdid(widthCm: 60, heightCm: 34));
        Assert.NotNull(info);
        Assert.Equal(60, info!.WidthCm);
        Assert.Equal(34, info.HeightCm);
    }

    [Fact]
    public void Parse_ZeroSize_LeavesNull()
    {
        var info = EdidParser.Parse(MakeEdid(widthCm: 0, heightCm: 0));
        Assert.NotNull(info);
        Assert.Null(info!.WidthCm);
        Assert.Null(info.HeightCm);
    }

    [Fact]
    public void Parse_LongerThan128_StillParsesBaseBlock()
    {
        // Extensions blocks may follow the 128-byte base block.
        var edid = new byte[256];
        MakeEdid().CopyTo(edid, 0);
        var info = EdidParser.Parse(edid);
        Assert.NotNull(info);
        Assert.Equal("DEL", info!.Manufacturer);
    }
}
