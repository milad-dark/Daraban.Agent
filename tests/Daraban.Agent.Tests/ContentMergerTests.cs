using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Models;

namespace Daraban.Agent.Tests;

public class ContentMergerTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"contentmerge-{Guid.NewGuid():N}");

    public ContentMergerTests()
    {
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tmpDir, recursive: true);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_tmpDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Merge_NoFile_ReturnsUnchanged()
    {
        var content = new DeviceContent { ComputerName = "host01" };

        var result = ContentMerger.Merge(content, null);
        Assert.Same(content, result);

        var result2 = ContentMerger.Merge(content, Path.Combine(_tmpDir, "missing.json"));
        Assert.Same(content, result2);
    }

    [Fact]
    public void Merge_Json_ContentWrapper_OverridesMatchingProperties()
    {
        var path = Write("extra.json", """{ "content": { "computerName": "custom-name", "operatingSystem": "Custom OS" } }""");
        var content = new DeviceContent { ComputerName = "host01", OperatingSystem = "Windows", Domain = "corp" };

        var result = ContentMerger.Merge(content, path);

        Assert.Equal("custom-name", result.ComputerName);
        Assert.Equal("Custom OS", result.OperatingSystem);
        Assert.Equal("corp", result.Domain); // untouched
    }

    [Fact]
    public void Merge_Json_BareObject_OverridesMatches()
    {
        var path = Write("bare.json", """{ "domain": "merged.local" }""");
        var content = new DeviceContent { Domain = "old.local" };

        var result = ContentMerger.Merge(content, path);

        Assert.Equal("merged.local", result.Domain);
    }

    [Fact]
    public void Merge_Json_ListCategory_ReplacesList()
    {
        var path = Write("sw.json", """{ "content": { "software": [ { "name": "OnlyApp" } ] } }""");
        var content = new DeviceContent { Software = [new SoftwareInfo { Name = "A" }] };

        var result = ContentMerger.Merge(content, path);

        Assert.Single(result.Software);
        Assert.Equal("OnlyApp", result.Software[0].Name);
    }

    [Fact]
    public void Merge_Json_UnknownFields_Ignored()
    {
        var path = Write("unknown.json", """{ "content": { "nonexistentField": "x" } }""");
        var content = new DeviceContent { ComputerName = "host01" };

        var result = ContentMerger.Merge(content, path);

        Assert.Equal("host01", result.ComputerName);
    }

    [Fact]
    public void Merge_Xml_ContentNode_Overrides()
    {
        var path = Write("extra.xml", "<content><computerName>xml-name</computerName></content>");
        var content = new DeviceContent { ComputerName = "host01" };

        var result = ContentMerger.Merge(content, path);

        Assert.Equal("xml-name", result.ComputerName);
    }

    [Fact]
    public void Merge_InvalidJson_Tolerated()
    {
        var path = Write("bad.json", "{ not json ]");
        var content = new DeviceContent { ComputerName = "host01" };

        var result = ContentMerger.Merge(content, path);

        Assert.Equal("host01", result.ComputerName);
    }
}