using Daraban.Agent.Core.Config;
using System.Net;
using System.Security.Cryptography.X509Certificates;

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
        var handler = BuildHandler(options);
        var http = new HttpClient(handler) { BaseAddress = new Uri(server.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        return new DarabanClient(http, options);
    }

    /// <summary>
    /// Builds the HttpClientHandler honoring AgentOptions.Proxy (mirrors glpi-agent `proxy`):
    ///   null   → HttpClient default, which respects HTTP_PROXY/HTTPS_PROXY env vars
    ///   "none" → disable proxy entirely (does not inherit env vars)
    ///   URL    → use the given proxy for all requests
    ///
    /// Also wires ssl-keystore client certificates and ssl-fingerprint server pinning.
    /// </summary>
    public static HttpClientHandler BuildHandler(AgentOptions options)
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

        // Client certificate from ssl-keystore (Windows/macOS)
        if (!string.IsNullOrWhiteSpace(options.SslKeystore))
        {
            var clientCert = SslCertificateProvider.GetClientCertificate(options.SslKeystore);
            if (clientCert is not null)
            {
                handler.ClientCertificates.Add(clientCert);
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            }
            else
            {
                Console.Error.WriteLine($"[transport] ssl-keystore configured ('{options.SslKeystore}') but no client certificate found.");
            }
        }

        // Server certificate fingerprint pinning (ssl-fingerprint)
        if (!string.IsNullOrWhiteSpace(options.SslFingerprint))
        {
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                SslCertificateProvider.FingerprintMatches(options.SslFingerprint, new X509Certificate2(cert!));
        }

        return handler;
    }
}
