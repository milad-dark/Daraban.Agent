using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Sends WoL magic packets to a configured list of MAC addresses.
/// In the real daraban-agent this list normally comes from the server (assets flagged
/// for wake-up); here it's read from AgentOptions.WakeOnLanMacs (CLI: --wol-mac, repeatable),
/// which keeps this task usable both interactively and server-driven.
/// </summary>
public sealed class WakeOnLanTask : IAgentTask
{
    public string Name => "wakeonlan";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        if (options.WakeOnLanMacs.Count == 0)
        {
            Console.WriteLine("[wakeonlan] No target MAC addresses configured (--wol-mac); skipped.");
            return;
        }

        var results = new List<WakeOnLanResult>();

        foreach (var mac in options.WakeOnLanMacs)
        {
            ct.ThrowIfCancellationRequested();
            var result = new WakeOnLanResult { MacAddress = mac };
            try
            {
                WakeOnLanSender.Send(mac, options.WakeOnLanBroadcast);
                result.Sent = true;
                Console.WriteLine($"[wakeonlan] Magic packet sent to {mac}");
            }
            catch (Exception ex)
            {
                result.Sent = false;
                result.Error = ex.Message;
                Console.WriteLine($"[wakeonlan] Failed to send to {mac}: {ex.Message}");
            }
            results.Add(result);
        }

        await DeliverAsync(options, results, ct);
    }

    private static async Task DeliverAsync(AgentOptions options, List<WakeOnLanResult> results, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            Directory.CreateDirectory(options.Local);
            var file = Path.Combine(options.Local, $"wakeonlan-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }), ct);
            Console.WriteLine($"[wakeonlan] Results written to {file}");
        }
        else if (!string.IsNullOrWhiteSpace(options.Server))
        {
            var client = DarabanClientFactory.Create(options);
            await client.PostWakeOnLanResultAsync(options.Tag ?? Environment.MachineName, results, ct);
            Console.WriteLine("[wakeonlan] Results sent to server.");
        }
    }
}
