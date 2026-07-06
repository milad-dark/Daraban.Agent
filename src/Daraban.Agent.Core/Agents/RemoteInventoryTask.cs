using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Transport;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

public sealed class RemoteInventoryTask : IAgentTask
{
    public string Name => "remote";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        // TODO: load remotes from a local file/DB (like glpi-remote add/list).
        // For each remote:
        //   if url.StartsWith("ssh://")  -> SSH.NET collector
        //   if url.StartsWith("winrm://") -> WinRM collector
        // Build inventory JSON per host, then post.

        var stub = new
        {
            action = "remote",
            deviceid = options.Tag ?? Environment.MachineName,
            timestampUtc = DateTime.UtcNow,
            message = "Not implemented yet. See TODO in RemoteInventoryTask."
        };

        var json = JsonSerializer.Serialize(stub);

        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            var dir = options.Local;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"remote-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, json, ct);
            Console.WriteLine($"[remote] Remote inventory written to {file}");
        }
        else if (!string.IsNullOrWhiteSpace(options.Server))
        {
            var client = new DarabanClient(new HttpClient { BaseAddress = new Uri(options.Server) });
            await client.PostInventoryAsync(json, ct); // same endpoint, different action tag
            Console.WriteLine("[remote] Remote inventory sent to server.");
        }
        else
        {
            Console.WriteLine("[remote] No server or local path configured; skipped.");
        }
    }
}