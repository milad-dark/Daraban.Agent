using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Remote inventory of a vCenter/ESXi host and its VMs — the "ESX/vCenter remote inventory"
/// task that was entirely absent before. Runs from the agent host against
/// --esx-host/--esx-user/--esx-password; it does not need anything installed on the
/// hypervisor itself (same agentless model as glpi-agent's ESX task).
/// </summary>
public sealed class EsxInventoryTask : IAgentTask
{
    public string Name => "esx";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.EsxHost) ||
            string.IsNullOrWhiteSpace(options.EsxUser) ||
            string.IsNullOrWhiteSpace(options.EsxPassword))
        {
            Console.WriteLine("[esx] --esx-host/--esx-user/--esx-password are required; skipped.");
            return;
        }

        await using var collector = new EsxRestCollector(options.EsxHost, options.EsxIgnoreSslErrors);

        List<EsxHostInfo> hosts;
        try
        {
            await collector.ConnectAsync(options.EsxUser, options.EsxPassword, ct);
            hosts = await collector.CollectAllHostsAsync(ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[esx] Failed to collect from {options.EsxHost}: {ex.Message}");
            return;
        }

        Console.WriteLine($"[esx] Collected {hosts.Count} host(s), {hosts.Sum(h => h.VirtualMachines.Count)} VM(s) total.");
        await DeliverAsync(options, hosts, ct);
    }

    private static async Task DeliverAsync(AgentOptions options, List<EsxHostInfo> hosts, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            Directory.CreateDirectory(options.Local);
            var file = Path.Combine(options.Local, $"esx-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(hosts, new JsonSerializerOptions { WriteIndented = true }), ct);
            Console.WriteLine($"[esx] Results written to {file}");
        }
        else if (!string.IsNullOrWhiteSpace(options.Server))
        {
            var client = DarabanClientFactory.Create(options);
            var deviceId = options.Tag ?? options.EsxHost ?? Environment.MachineName;
            foreach (var host in hosts)
                await client.PostEsxInventoryAsync(deviceId, host, ct);
            Console.WriteLine("[esx] Results sent to server.");
        }
        else
        {
            Console.WriteLine("[esx] No server or local path configured; results kept in memory only.");
        }
    }
}
