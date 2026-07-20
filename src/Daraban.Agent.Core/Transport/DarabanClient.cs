using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Daraban.Agent.Core.Transport;
/// <summary>
/// HTTP transport to the Daraban.Agent.Server API (ASP.NET Core + Blazor). Routes match
/// Daraban.Agent.Server.Controllers.AgentController:
///   GET  /api/agent/prolog?deviceId=...
///   POST /api/agent/inventory | discovery | netinventory | wakeonlan | esx
///   GET  /api/agent/deploy/jobs?deviceId=...
///   POST /api/agent/deploy/result
///
/// This replaces the earlier version of this file, which targeted stock GLPI's
/// /front/inventory.php paths. If you ever point an agent at a real GLPI server instead,
/// swap this implementation out — the IGlpiClient interface stays the same either way.
/// </summary>
public sealed class DarabanClient : IDarabanClient
{
    private readonly HttpClient _http;
    //private readonly bool _useGzip;
    private readonly AgentOptions _options;
    //private readonly ILogger<DarabanClient> _logger;
    private readonly OAuthTokenProvider _tokenProvider;

    public DarabanClient(HttpClient http, AgentOptions options)
    {
        _http = http;
        _options = options;
        _tokenProvider = new OAuthTokenProvider(http, options);

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Daraban-Agent", "1.0"));
    }

    public async Task<string?> PrologAsync(string deviceId, CancellationToken ct = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, $"api/agent/prolog?deviceId={Uri.EscapeDataString(deviceId)}", ct);
            using var resp = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[server] Prolog failed: HTTP {(int)resp.StatusCode}");
                return null;
            }
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[server] Prolog error: {ex.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------
    public Task PostInventoryAsync(string deviceId, object contentObject, string itemtype = "Computer", CancellationToken ct = default)
        => SendEnvelopeAsync("/api/agent/inventory", deviceId, "inventory", itemtype, contentObject, ct);

    /// <summary>
    /// Compatibility overload for tasks that already built their own JSON string (e.g.
    /// LocalInventoryTask serializes DeviceInventory itself). The deviceId is pulled out
    /// of the JSON's "DeviceId" property so callers don't have to change their call sites.
    /// </summary>
    public Task PostInventoryAsync(string jsonPayload, CancellationToken ct = default)
    {
        string deviceId = "unknown";
        object content = jsonPayload;
        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            if (doc.RootElement.TryGetProperty("DeviceId", out var idEl) && idEl.GetString() is { } id)
                deviceId = id;
            if (doc.RootElement.TryGetProperty("Content", out var contentEl))
                content = JsonSerializer.Deserialize<object>(contentEl.GetRawText())!;
        }
        catch { /* not our envelope shape — send as-is under an "unknown" device id */ }

        return SendEnvelopeAsync("/api/agent/inventory", deviceId, "inventory", "Computer", content, ct);
    }

    // ------------------------------------------------------------------
    public Task PostDiscoveryAsync(string deviceId, IEnumerable<DiscoveredHost> hosts, CancellationToken ct = default)
        => SendEnvelopeAsync("/api/agent/discovery", deviceId, "discovery", null, hosts, ct);

    public Task PostNetInventoryAsync(string deviceId, IEnumerable<NetworkDeviceInventory> devices, CancellationToken ct = default)
        => SendEnvelopeAsync("/api/agent/netinventory", deviceId, "netinventory", null, devices, ct);

    public Task PostWakeOnLanResultAsync(string deviceId, IEnumerable<WakeOnLanResult> results, CancellationToken ct = default)
        => SendEnvelopeAsync("/api/agent/wakeonlan", deviceId, "wakeonlan", null, results, ct);

    public Task PostEsxInventoryAsync(string deviceId, EsxHostInfo host, CancellationToken ct = default)
        => SendEnvelopeAsync("/api/agent/esx", deviceId, "esx", "EsxHost", host, ct);

    // ------------------------------------------------------------------
    public async Task<List<DeployJob>> GetPendingDeployJobsAsync(string deviceId, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/agent/deploy/jobs?deviceId={Uri.EscapeDataString(deviceId)}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return [];

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            return JsonSerializer.Deserialize<List<DeployJob>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[server] GetPendingDeployJobs error: {ex.Message}");
            return [];
        }
    }

    public Task PostDeployResultAsync(string deviceId, DeployJobResult result, CancellationToken ct = default)
        => SendEnvelopeAsync("/api/agent/deploy/result", deviceId, "deployResult", null, result, ct);

    // ── GET /api/agent/collect/jobs ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<CollectJob>> GetCollectJobsAsync(CancellationToken ct)
    {
        using var request = await CreateRequestAsync(
             HttpMethod.Get,
             "api/agent/collect/jobs", ct);

        try
        {
            using var response = await _http.SendAsync(request, ct);

            // 204 No Content = server has no pending jobs for this agent
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return [];
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var jobs = JsonSerializer.Deserialize<List<CollectJob>>(json, JsonOpts);

            return jobs ?? [];
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[collect] Failed to fetch collect jobs from server: {ex.Message}");
            return [];     // graceful degradation — don't crash the task
        }
    }

    // ── POST /api/agent/collect/results ──────────────────────────────────────

    /// <inheritdoc/>
    public async Task PostCollectResultsAsync(IList<CollectResult> results, CancellationToken ct)
    {
        if (results.Count == 0)
            return;

        var payload = new CollectResultsPayload
        {
            AgentId = _options.AgentId,
            Timestamp = DateTime.UtcNow,
            Results = results
        };

        using var request = await CreateRequestAsync(HttpMethod.Post, "api/agent/collect/results", ct);

        request.Content = _options.UseGzip
            ? await BuildGzipContentAsync(payload, ct)
            : BuildJsonContent(payload);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[collect] Failed to post collect results: {ex.Message}");
            throw;   // let AgentRunner record failure in AgentStatusTracker
        }
    }


    // Internals
    private Task SendEnvelopeAsync(string url, string deviceId, string action, string? itemtype, object content, CancellationToken ct)
    {
        var envelope = new
        {
            deviceId,
            itemtype,
            action,
            timestampUtc = DateTime.UtcNow,
            content
        };
        return SendRawAsync(url, JsonSerializer.Serialize(envelope), ct);
    }

    private async Task SendRawAsync(string url, string json, CancellationToken ct)
    {
        HttpContent body;

        if (_options.UseGzip)
        {
            var buffer = new MemoryStream();
            await using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            await using (var writer = new StreamWriter(gzip, Encoding.UTF8))
            {
                await writer.WriteAsync(json).ConfigureAwait(false);
            }
            buffer.Position = 0;
            body = new StreamContent(buffer);
            body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            body.Headers.ContentEncoding.Add("gzip");
        }
        else
        {
            body = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var request = await CreateRequestAsync(HttpMethod.Post, url, ct);
        request.Content = body;
        using var resp = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"POST {url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase} — {text}");
        }
    }

    /// <summary>
    /// Builds an HttpRequestMessage with the base URL and optional API key header.
    /// </summary>
    private HttpRequestMessage BuildRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method,
            new Uri(_options.Server!.TrimEnd('/') + "/" + relativeUrl.TrimStart('/')));

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.Add("X-Api-Key", _options.ApiKey);

        return request;
    }

    /// <summary>
    /// Builds an HttpRequestMessage with the base URL and optional API key header.
    /// </summary>
    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string relativeUrl, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, relativeUrl.TrimStart('/'));
        var accessToken = await _tokenProvider.GetAccessTokenAsync(ct);
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Transitional only. The backend must reject this once OAuth enrollment is live.
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.Add("X-Api-Key", _options.ApiKey);

        return request;
    }

    /// <summary>Serializes <paramref name="payload"/> to a JSON <see cref="StringContent"/>.</summary>
    private static StringContent BuildJsonContent<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes <paramref name="payload"/> to gzip-compressed JSON.
    /// Mirrors the same compression used by PostLocalInventoryAsync.
    /// </summary>
    private static async Task<ByteArrayContent> BuildGzipContentAsync<T>(T payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        using var ms = new MemoryStream();
        await using var gz = new GZipStream(ms, CompressionLevel.Optimal);
        await gz.WriteAsync(jsonBytes, ct);
        await gz.FlushAsync(ct);

        var compressed = ms.ToArray();
        var content = new ByteArrayContent(compressed);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }
}