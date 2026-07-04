using System.Text;
using System.Text.Json;

namespace Daraban.Agent.Core.Transport;

public sealed class GlpiClient : IGlpiClient
{
    private readonly HttpClient _http;

    public GlpiClient(HttpClient http) => _http = http;

    private static async Task PostJsonAsync(HttpClient http, string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(url, content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public Task PostInventoryAsync(string jsonPayload, CancellationToken ct = default)
        => PostJsonAsync(_http, "/api/inventory", new { action = "inventory", content = jsonPayload }, ct);

    public Task PostDiscoveryAsync(string jsonPayload, CancellationToken ct = default)
        => PostJsonAsync(_http, "/api/discovery", new { action = "netdiscovery", content = jsonPayload }, ct);

    public Task PostNetInventoryAsync(string jsonPayload, CancellationToken ct = default)
        => PostJsonAsync(_http, "/api/netinventory", new { action = "netinventory", content = jsonPayload }, ct);
}