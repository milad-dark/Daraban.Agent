using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

public sealed class RemoteInventoryTask : IAgentTask
{
    public string Name => "remote";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        // Was previously a pure stub ("Not implemented yet") even though
        // SshRemoteCollector and WinrmRemoteCollector are fully implemented and were
        // reachable only from the CLI's --method ssh / --method winrm test paths.
        // This now actually runs them against AgentOptions.RemoteHosts.
        if (options.RemoteHosts.Count == 0)
        {
            Console.WriteLine("[remote] No RemoteHosts configured; skipped. " +
                "Add entries like \"ssh://user:password@10.0.0.5\" to AgentOptions.RemoteHosts.");
            return;
        }

        var client = !string.IsNullOrWhiteSpace(options.Server) ? DarabanClientFactory.Create(options) : null;

        foreach (var entry in options.RemoteHosts)
        {
            RemoteHostSpec spec;
            try
            {
                spec = RemoteHostSpec.Parse(entry);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"[remote] Skipping invalid entry: {ex.Message}");
                continue;
            }

            DeviceInventory inventory;
            try
            {
                inventory = await CollectOneAsync(spec, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[remote] Failed to collect {spec.Scheme}://{spec.Host}: {ex.Message}");
                continue;
            }

            // Both collectors leave DeviceInventory.DeviceId at its default
            // (Environment.MachineName — the *local* agent's own hostname), which would
            // otherwise make the server attribute this remote host's data to the agent
            // machine itself.
            inventory.DeviceId = spec.Host;

            var json = JsonSerializer.Serialize(inventory);

            if (client is not null)
            {
                await client.PostInventoryAsync(json, ct);
                Console.WriteLine($"[remote] {spec.Host} inventory sent to server.");
            }
            else if (!string.IsNullOrWhiteSpace(options.Local))
            {
                Directory.CreateDirectory(options.Local);
                var file = Path.Combine(options.Local, $"remote-{spec.Host}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
                await File.WriteAllTextAsync(file, json, ct);
                Console.WriteLine($"[remote] {spec.Host} inventory written to {file}");
            }
            else
            {
                Console.WriteLine("[remote] No server or local path configured; skipped.");
            }
        }
    }

    private static Task<DeviceInventory> CollectOneAsync(RemoteHostSpec spec, CancellationToken ct)
        => spec.Scheme switch
        {
            "ssh" => new SshRemoteCollector().CollectAsync(spec.Host, spec.Username, spec.Password, ct),
            "winrm" => new WinrmRemoteCollector(spec.Host, spec.Username, spec.Password, https: spec.WinrmHttps).CollectAsync(ct),
            _ => throw new FormatException($"Unsupported remote scheme '{spec.Scheme}'.")
        };
}
