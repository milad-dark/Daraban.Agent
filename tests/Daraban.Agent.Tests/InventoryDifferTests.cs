using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;

namespace Daraban.Agent.Tests;

public class InventoryDifferTests : IDisposable
{
    private readonly string _snapshotPath = Path.Combine(Path.GetTempPath(), $"invdiff-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_snapshotPath)) File.Delete(_snapshotPath);
    }

    private static DeviceContent Sample()
        => new()
        {
            ComputerName = "host01",
            OperatingSystem = "Windows 11",
            Software =
            [
                new SoftwareInfo { Name = "Chrome", Version = "120" },
                new SoftwareInfo { Name = "VS Code", Version = "1.85" }
            ],
            Cpus = [new CpuInfo { Name = "i7", Cores = "8" }],
        };

    [Fact]
    public void Diff_DisabledPostpone_AlwaysFull()
    {
        var options = new AgentOptions { FullInventoryPostpone = 0 };

        var (content, action, isFull) = InventoryDiffer.Diff(Sample(), options, _snapshotPath);

        Assert.Equal("inventory", action);
        Assert.True(isFull);
        Assert.Equal(2, content.Software.Count); // nothing blanked
    }

    [Fact]
    public void Diff_FirstRun_IsFull()
    {
        var options = new AgentOptions { FullInventoryPostpone = 14 };

        var (_, action, isFull) = InventoryDiffer.Diff(Sample(), options, _snapshotPath);

        Assert.Equal("inventory", action);
        Assert.True(isFull);
    }

    [Fact]
    public void Diff_UnchangedSecondRun_IsPartial_UnchangedCategoriesBlanked()
    {
        var options = new AgentOptions { FullInventoryPostpone = 3 };

        InventoryDiffer.Diff(Sample(), options, _snapshotPath);           // run 1: full
        var (content, action, isFull) = InventoryDiffer.Diff(Sample(), options, _snapshotPath); // run 2: partial

        Assert.Equal("partial", action);
        Assert.False(isFull);
        // Unchanged list categories blanked; scalar system identifiers preserved.
        Assert.Empty(content.Software);
        Assert.Equal("host01", content.ComputerName);
    }

    [Fact]
    public void Diff_ChangedCategory_IncludedInPartial()
    {
        var options = new AgentOptions { FullInventoryPostpone = 3 };

        InventoryDiffer.Diff(Sample(), options, _snapshotPath);

        var changed = Sample();
        changed.Software.Add(new SoftwareInfo { Name = "Firefox" });

        var (content, action, _) = InventoryDiffer.Diff(changed, options, _snapshotPath);

        Assert.Equal("partial", action);
        Assert.Equal(3, content.Software.Count);
    }

    [Fact]
    public void Diff_RequiredCategory_AlwaysIncluded()
    {
        var options = new AgentOptions
        {
            FullInventoryPostpone = 3,
            RequiredCategories = ["software"]
        };

        InventoryDiffer.Diff(Sample(), options, _snapshotPath);           // run 1: full snapshot

        var (content, action, _) = InventoryDiffer.Diff(Sample(), options, _snapshotPath); // run 2

        Assert.Equal("partial", action);
        // Even though software didn't change, required-category forces it in.
        Assert.Equal(2, content.Software.Count);
    }

    [Fact]
    public void Diff_CounterForcesFullInventory_AfterPostponePartials()
    {
        var options = new AgentOptions { FullInventoryPostpone = 2 };

        InventoryDiffer.Diff(Sample(), options, _snapshotPath);                    // run 1: full, 2 partials allowed
        var (c2, a2, isFull2) = InventoryDiffer.Diff(Sample(), options, _snapshotPath);   // run 2: partial (1 left)
        var (c3, a3, isFull3) = InventoryDiffer.Diff(Sample(), options, _snapshotPath);   // run 3: partial (0 left)
        var (c4, a4, isFull4) = InventoryDiffer.Diff(Sample(), options, _snapshotPath);   // run 4: full again

        Assert.Equal("partial", a2);
        Assert.False(isFull2);
        Assert.Empty(c2.Software);

        Assert.Equal("partial", a3);
        Assert.False(isFull3);

        Assert.Equal("inventory", a4);
        Assert.True(isFull4);
        Assert.Equal(2, c4.Software.Count);
    }
}