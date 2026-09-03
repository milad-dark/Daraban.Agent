using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Xml.Linq;

namespace Daraban.Agent.Tests;

public class FusionInventoryClientTests
{
    [Fact]
    public void ToFusionInventoryXml_SystemFields_Mapped()
    {
        var content = new DeviceContent
        {
            ComputerName = "host01",
            OperatingSystem = "Ubuntu 22.04",
            Domain = "corp.local",
            Workgroup = "WORKGROUP",
        };

        var root = FusionInventoryClient.ToFusionInventoryXml(content);

        Assert.Equal("host01", root.Element("NAME")?.Value);
        Assert.Equal("Ubuntu 22.04", root.Element("OSNAME")?.Value);
        Assert.Equal("corp.local", root.Element("DOMAIN")?.Value);
        Assert.Equal("WORKGROUP", root.Element("WORKGROUP")?.Value);
    }

    [Fact]
    public void ToFusionInventoryXml_Software_Populated()
    {
        var content = new DeviceContent
        {
            Software = [new SoftwareInfo { Name = "Chrome", Version = "120.1", Vendor = "Google" }]
        };

        var root = FusionInventoryClient.ToFusionInventoryXml(content);

        var sw = root.Element("SOFTWARES")?.Element("SOFTWARE");
        Assert.NotNull(sw);
        Assert.Equal("Chrome", sw!.Element("NAME")?.Value);
        Assert.Equal("120.1", sw.Element("VERSION")?.Value);
        Assert.Equal("Google", sw.Element("PUBLISHER")?.Value);
    }

    [Fact]
    public void ToFusionInventoryXml_CpuAndStorage_Populated()
    {
        var content = new DeviceContent
        {
            Cpus = [new CpuInfo { Name = "Intel i7", Cores = "8", Speed = "4000" }],
            Storages = [new StorageInfo { Model = "NVMe", Size = "512 MB", InterfaceType = "NVMe" }]
        };

        var root = FusionInventoryClient.ToFusionInventoryXml(content);

        var cpu = root.Element("CPUS")?.Element("CPU");
        Assert.NotNull(cpu);
        Assert.Equal("Intel i7", cpu!.Element("NAME")?.Value);

        var storage = root.Element("STORAGES")?.Element("STORAGE");
        Assert.NotNull(storage);
        Assert.Equal("NVMe", storage!.Element("MODEL")?.Value);
        Assert.Equal("512 MB", storage.Element("SIZE")?.Value);
    }

    [Fact]
    public void ToFusionInventoryXml_EmptyContent_RootStillValid()
    {
        var root = FusionInventoryClient.ToFusionInventoryXml(new DeviceContent());

        Assert.NotNull(root.Element("SOFTWARES"));
        Assert.NotNull(root.Element("CPUS"));
        Assert.NotNull(root.Element("STORAGES"));
        Assert.NotNull(root.Element("NETWORKS"));
    }

    [Fact]
    public void TryDeserializeContent_CamelCaseJson_RoundTrips()
    {
        var json = """{ "computerName": "host01", "operatingSystem": "Windows 11" }""";

        var content = FusionInventoryClient.TryDeserializeContent(json);

        Assert.NotNull(content);
        Assert.Equal("host01", content!.ComputerName);
        Assert.Equal("Windows 11", content.OperatingSystem);
    }

    [Fact]
    public void TryDeserializeContent_InvalidJson_ReturnsNull()
    {
        Assert.Null(FusionInventoryClient.TryDeserializeContent("{ not json"));
    }
}