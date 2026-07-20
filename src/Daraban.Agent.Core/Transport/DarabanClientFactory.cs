using Daraban.Agent.Core.Config;

namespace Daraban.Agent.Core.Transport;

public static class DarabanClientFactory
{
    public static DarabanClient Create(AgentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Server))
            throw new InvalidOperationException("AgentOptions.Server must be set before creating a DarabanClient.");

        var http = new HttpClient { BaseAddress = new Uri(options.Server.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        return new DarabanClient(http, options);
    }
}
