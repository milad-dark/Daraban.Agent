using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Transport;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

public sealed class LocalInventoryTask : IAgentTask
{
    public string Name => "local";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        // TODO: collect OS/hardware/software/network info here.
        // For now we emit a stub payload so the plumbing is testable.
        var payload = new
        {
            action = "inventory",
            content = new
            {
                deviceid = options.Tag ?? Environment.MachineName,
                os = Environment.OSVersion.VersionString,
                Cpu = "",
                CpuUsage = Environment.CpuUsage.TotalTime,
                Motherboard = "",
                Ram = "",
                Vga = "",
                Hdd = "",
                Network = "",
                timestampUtc = DateTime.UtcNow
            }
        };

        var json = JsonSerializer.Serialize(payload);

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
            var client = new GlpiClient(new HttpClient { BaseAddress = new Uri(options.Server) });
            await client.PostInventoryAsync(json, ct);
            Console.WriteLine("[local] Inventory sent to server.");
        }
        else
        {
            Console.WriteLine("[local] No server or local path configured; skipped.");
        }
    }
}