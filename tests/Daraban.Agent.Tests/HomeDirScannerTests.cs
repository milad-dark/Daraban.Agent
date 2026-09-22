using Daraban.Agent.Core.Collectors;

namespace Daraban.Agent.Tests;

public class HomeDirScannerTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"homedirscan-{Guid.NewGuid():N}");

    public HomeDirScannerTests()
    {
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tmpDir, recursive: true);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_tmpDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void FindFiles_PatternsFilter()
    {
        Write("a.vmx", "x");
        Write("b.txt", "x");
        Write(Path.Combine("sub", "c.vbox"), "x");

        var found = HomeDirScanner.FindFiles([_tmpDir], ["*.vmx", "*.vbox"]).ToList();

        Assert.Equal(2, found.Count);
        Assert.DoesNotContain(found, f => f.EndsWith(".txt"));
    }

    [Fact]
    public void FindFiles_MaxDepthRespected()
    {
        Write("root.vmx", "x");
        Write(Path.Combine("L1", "l1.vmx"), "x");
        Write(Path.Combine("L1", "L2", "l2.vmx"), "x");
        Write(Path.Combine("L1", "L2", "L3", "l3.vmx"), "x");

        var found = HomeDirScanner.FindFiles([_tmpDir], ["*.vmx"], maxDepth: 1).ToList();

        Assert.Equal(2, found.Count); // root + L1 only
    }

    [Fact]
    public void FindFiles_MissingRoot_NoThrow()
    {
        var found = HomeDirScanner.FindFiles([Path.Combine(_tmpDir, "nope")], ["*.vmx"]).ToList();

        Assert.Empty(found);
    }

    [Fact]
    public void FromVmFile_ParsesDisplayName()
    {
        var path = Write("srv.vmx", ".encoding = \"UTF-8\"\ndisplayName = \"My Server\"\nguestOS = \"ubuntu-64\"\n");

        var info = HomeDirScanner.FromVmFile(path);

        Assert.Equal("My Server", info.Name);
        Assert.Equal("VMware", info.Vendor);
        Assert.Equal("detected", info.Version);
    }

    [Fact]
    public void FromVmFile_FallsBackToFileName()
    {
        var path = Write("disk.vbox", "<VirtualBox></VirtualBox>");

        var info = HomeDirScanner.FromVmFile(path);

        Assert.Equal("disk", info.Name);
        Assert.Equal("VirtualBox", info.Vendor);
    }

    [Fact]
    public void TryParseLicensePlist_DetectsLicense()
    {
        var path = Write("com.example.app.plist",
            "<?xml version=\"1.0\"?><plist><dict><key>LicenseKey</key><string>ABC-123-XYZ</string></dict></plist>");

        var info = HomeDirScanner.TryParseLicensePlist(path);

        Assert.NotNull(info);
        Assert.Equal("com.example.app", info!.Name);
        Assert.Contains("ABC-123-XYZ", info.Caption);
    }

    [Fact]
    public void TryParseLicensePlist_IgnoresNonLicense()
    {
        var path = Write("com.example.other.plist",
            "<?xml version=\"1.0\"?><plist><dict><key>Theme</key><string>dark</string></dict></plist>");

        Assert.Null(HomeDirScanner.TryParseLicensePlist(path));
    }
}
