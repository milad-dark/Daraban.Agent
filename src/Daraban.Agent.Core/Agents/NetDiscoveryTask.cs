using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Transport;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

public sealed class NetDiscoveryTask : IAgentTask
{
    public string Name => "netdiscovery";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        // This is just a placeholder: ping first 10 IPs of a /24.
        // Replace with proper IP range parsing later.
        var baseIp = "192.168.1";
        var results = new List<object>();

        for (int i = 1; i <= 10; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ip = $"{baseIp}.{i}";
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(IPAddress.Parse(ip), timeout: TimeSpan.FromMilliseconds(2000), cancellationToken: ct);
            results.Add(new
            {
                ip,
                status = reply.Status.ToString(),
                roundtripMs = reply.RoundtripTime
            });
        }

        var payload = new
        {
            action = "netdiscovery",
            deviceid = options.Tag ?? Environment.MachineName,
            timestampUtc = DateTime.UtcNow,
            found = results
        };

        var json = JsonSerializer.Serialize(payload);

        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            var dir = options.Local;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"netdiscovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(file, json, ct);
            Console.WriteLine($"[netdiscovery] Discovery written to {file}");
        }
        else if (!string.IsNullOrWhiteSpace(options.Server))
        {
            var client = new GlpiClient(new HttpClient { BaseAddress = new Uri(options.Server) });
            await client.PostDiscoveryAsync(json, ct);
            Console.WriteLine("[netdiscovery] Discovery sent to server.");
        }
        else
        {
            Console.WriteLine("[netdiscovery] No server or local path configured; skipped.");
        }
    }
}