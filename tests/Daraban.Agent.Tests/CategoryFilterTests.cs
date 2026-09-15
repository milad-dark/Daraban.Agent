using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;

namespace Daraban.Agent.Tests;

public class CategoryFilterTests
{
    private static DeviceContent Sample()
        => new()
        {
            ComputerName = "host01",
            OperatingSystem = "Windows 11",
            Software = [new SoftwareInfo { Name = "Chrome" }],
            Processes = [new ProcessInfo { Name = "explorer" }],
            Printers = [new PrinterInfo { Name = "HP" }],
            Bios = new BiosInfo { Manufacturer = "AMI" },
        };

    [Fact]
    public void StripCategories_BlanksListCategories()
    {
        var content = Sample();

        InventoryDiffer.StripCategories(content, ["software", "process"]);

        Assert.Empty(content.Software);
        Assert.Empty(content.Processes);
        Assert.Single(content.Printers); // untouched category kept
        Assert.Equal("host01", content.ComputerName);
    }

    [Fact]
    public void StripCategories_BlanksStringCategory()
    {
        var content = Sample();

        InventoryDiffer.StripCategories(content, ["system"]);

        Assert.Equal("", content.ComputerName);
        Assert.Equal("Windows 11", content.OperatingSystem); // "operating system" alias kept separate
    }

    [Fact]
    public void StripCategories_ResetsStructCategory()
    {
        var content = Sample();

        InventoryDiffer.StripCategories(content, ["bios"]);

        Assert.Null(content.Bios.Manufacturer); // fresh default instance — nullable fields are null
    }

    [Fact]
    public void StripCategories_CaseInsensitive()
    {
        var content = Sample();

        InventoryDiffer.StripCategories(content, ["Software"]);

        Assert.Empty(content.Software);
    }

    [Fact]
    public void StripCategories_UnknownName_DoesNotThrowOrModify()
    {
        var content = Sample();

        InventoryDiffer.StripCategories(content, ["environment"]); // glpi has no direct mapping

        Assert.Single(content.Software);
        Assert.Equal("host01", content.ComputerName);
    }

    [Fact]
    public void StripCategories_EmptyOrNullList_NoOp()
    {
        var content = Sample();

        InventoryDiffer.StripCategories(content, []);

        Assert.Single(content.Software);
    }

    [Fact]
    public void Diff_AfterStrip_StrippedCategoriesStayEmpty()
    {
        // Full integration: strip first, then diff — stripped sections must not resurrect
        // on partial runs (hashes computed on the stripped content).
        var options = new AgentOptions { FullInventoryPostpone = 3 };
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"invstrip-{Guid.NewGuid():N}.json");

        try
        {
            var content = Sample();
            InventoryDiffer.StripCategories(content, ["software"]);
            InventoryDiffer.Diff(content, options, snapshotPath); // run 1: full

            var next = Sample();
            InventoryDiffer.StripCategories(next, ["software"]);
            var (result, action, _) = InventoryDiffer.Diff(next, options, snapshotPath); // run 2: partial

            Assert.Equal("partial", action);
            Assert.Empty(result.Software);
        }
        finally
        {
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }
    }
}