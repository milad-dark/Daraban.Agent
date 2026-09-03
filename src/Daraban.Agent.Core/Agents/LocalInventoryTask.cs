using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

public sealed class LocalInventoryTask : IAgentTask
{
    public string Name => "local";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        var inventory = LocalCollectorFactory.CollectLocal();
        inventory.DeviceId = options.AgentId ?? options.Tag ?? Environment.MachineName;

        // The collector stores DeviceContent serialized as JSON; deserialize for the diff.
        var deviceContent = JsonSerializer.Deserialize<DeviceContent>(inventory.Content) ?? new DeviceContent();

        // Differential inventory (full-inventory-postpone): produce partial content when
        // only a few categories changed. Falls back to a full inventory when disabled.
        var snapshotPath = InventoryDiffer.DefaultSnapshotPath(inventory.DeviceId);
        var (content, action, _) = InventoryDiffer.Diff(deviceContent, options, snapshotPath);
        inventory.Action = action;
        inventory.Content = JsonSerializer.Serialize(content);
        var json = JsonSerializer.Serialize(inventory);

        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            // Offline mode: write to local directory (like GLPI --local)
            var dir = options.Local;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"inventory-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, json, ct);
            Console.WriteLine($"[local] ({action}) Inventory written to {file}");
        }
        else if (options.Servers.Count > 0)
        {
            await MultiTargetDelivery.ForEachServerAsync(options, async client =>
            {
                await client.PostInventoryAsync(inventory.DeviceId, content, ct: ct);
            }, ct);
            Console.WriteLine($"[local] ({action}) Inventory sent to server(s).");
        }
        else
        {
            Console.WriteLine("[local] No server or local path configured; skipped.");
        }
    }
}