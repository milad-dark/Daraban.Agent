using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// The task that was previously missing entirely: SnmpNetworkCollector existed but nothing
/// called it in bulk over a range and shipped the results anywhere. This task expands
/// --ip-range the same way NetDiscovery does, queries each address over SNMP, and posts
/// (or writes) the aggregated per-device inventories.
/// </summary>
public sealed class NetInventoryTask : IAgentTask
{
    public string Name => "netinventory";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.IpRange))
        {
            Console.WriteLine("[netinventory] No --ip-range configured; skipped.");
            return;
        }

        var addresses = NetDiscoveryTask.ExpandCidr(options.IpRange).Select(a => a.ToString()).ToList();
        Console.WriteLine($"[netinventory] Querying {addresses.Count} address(es) over SNMP (community='{options.SnmpCommunity}') ...");

        var collector = new SnmpNetworkCollector();
        var results = new List<NetworkDeviceInventory>();
        using var throttle = new SemaphoreSlim(Math.Max(1, options.DiscoveryThreads));

        var tasks = addresses.Select(async ip =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var record = new NetworkDeviceInventory { IpAddress = ip, Community = options.SnmpCommunity };
                try
                {
                    var inventory = await collector.DiscoverAsync(ip, options.SnmpCommunity, options.SnmpTimeoutMs, ct);

                    // A device that never replied still comes back as an (almost) empty DeviceContent;
                    // treat "no OperatingSystem/hostname learned" as unreachable rather than reporting noise.
                    var hasData = !string.IsNullOrWhiteSpace(inventory.Content) &&
                                  inventory.Content.Contains("\"OperatingSystem\":\"") &&
                                  !inventory.Content.Contains("\"OperatingSystem\":null");

                    record.Reachable = hasData;
                    record.Inventory = hasData ? inventory : null;
                }
                catch (Exception ex)
                {
                    record.Reachable = false;
                    record.Error = ex.Message;
                }

                if (record.Reachable)
                    lock (results)
                        results.Add(record);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine($"[netinventory] {results.Count} SNMP-reachable device(s) inventoried.");

        await DeliverAsync(options, results, ct);
    }

    private static async Task DeliverAsync(AgentOptions options, List<NetworkDeviceInventory> results, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            Directory.CreateDirectory(options.Local);
            var file = Path.Combine(options.Local, $"netinventory-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }), ct);
            Console.WriteLine($"[netinventory] Results written to {file}");
        }
        else if (!string.IsNullOrWhiteSpace(options.Server))
        {
            var client = DarabanClientFactory.Create(options);
            await client.PostNetInventoryAsync(options.Tag ?? Environment.MachineName, results, ct);
            Console.WriteLine("[netinventory] Results sent to server.");
        }
        else
        {
            Console.WriteLine("[netinventory] No server or local path configured; results kept in memory only.");
        }
    }
}
