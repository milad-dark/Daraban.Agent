using Daraban.Agent.Core.Config;
using System.Net;

namespace Daraban.Agent.Core.Transport;

public static class DarabanClientFactory
{
    public static DarabanClient Create(AgentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Server))
            throw new InvalidOperationException("AgentOptions.Server must be set before creating a DarabanClient.");

        var handler = BuildHandler(options);
        var http = new HttpClient(handler) { BaseAddress = new Uri(options.Server.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        return new DarabanClient(http, options);
    }

    /// <summary>
    /// Builds the HttpClientHandler honoring AgentOptions.Proxy (mirrors glpi-agent `proxy`):
    ///   null   → HttpClient default, which respects HTTP_PROXY/HTTPS_PROXY env vars
    ///   "none" → disable proxy entirely (does not inherit env vars)
    ///   URL    → use the given proxy for all requests
    /// </summary>
    public static HttpMessageHandler BuildHandler(AgentOptions options)
    {
        var handler = new HttpClientHandler();
        var proxy = options.Proxy;

        if (proxy is { } && proxy.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            handler.UseProxy = false;
        }
        else if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxy);
        }

        return handler;
    }
}
