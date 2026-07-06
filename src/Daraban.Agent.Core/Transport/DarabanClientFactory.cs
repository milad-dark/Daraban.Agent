using Daraban.Agent.Core.Config;

namespace Daraban.Agent.Core.Transport;

public static class DarabanClientFactory
{
    public static DarabanClient Create(AgentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Server))
            throw new InvalidOperationException("AgentOptions.Server must be set before creating a DarabanClient.");

        var http = new HttpClient { BaseAddress = new Uri(options.Server) };

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);

        return new DarabanClient(http);
    }
}
