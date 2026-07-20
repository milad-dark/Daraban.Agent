using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
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
        var json = JsonSerializer.Serialize(inventory);

        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            // Offline mode: write to local directory (like GLPI --local)【turn0search1】
            var dir = options.Local;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"inventory-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, json, ct);
            Console.WriteLine($"[local] Inventory written to {file}");
        }
        else if (!string.IsNullOrWhiteSpace(options.Server))
        {
            var client = DarabanClientFactory.Create(options);
            await client.PostInventoryAsync(inventory.DeviceId, inventory.Content, ct: ct);
            await client.PostInventoryAsync(json, ct);
            Console.WriteLine("[local] Inventory sent to server.");
        }
        else
        {
            Console.WriteLine("[local] No server or local path configured; skipped.");
        }
    }
}