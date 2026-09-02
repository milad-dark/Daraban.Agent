using Daraban.Agent.Core.Config;

namespace Daraban.Agent.Core.Transport;

public static class DarabanClientFactory
{
    public static DarabanClient Create(AgentOptions options)
    {
        if (options.Servers.Count == 0 || string.IsNullOrWhiteSpace(options.Servers[0]))
            throw new InvalidOperationException("AgentOptions.Servers must contain at least one server before creating a DarabanClient.");

        return CreateFor(options, options.Servers[0]);
    }

    /// <summary>
    /// Creates one DarabanClient per configured server target. Used for multi-target
    /// delivery (mirrors glpi-agent's comma-separated `server`: results are sent to
    /// every target, and one failing target never blocks the others).
    /// </summary>
    public static List<DarabanClient> CreateAll(AgentOptions options)
    {
        var clients = new List<DarabanClient>();
        foreach (var server in options.Servers)
        {
            if (string.IsNullOrWhiteSpace(server)) continue;
            try
            {
                clients.Add(CreateFor(options, server));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[transport] Failed to create client for '{server}': {ex.Message}");
            }
        }
        return clients;
    }

    private static DarabanClient CreateFor(AgentOptions options, string server)
    {
        var http = new HttpClient { BaseAddress = new Uri(server.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        return new DarabanClient(http, options);
    }
}
