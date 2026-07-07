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
    private readonly bool _useGzip;

    public DarabanClient(HttpClient http, bool useGzip = true)
    {
        _http = http;
        _useGzip = useGzip;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Daraban-Agent", "1.0"));
    }

    public async Task<string?> PrologAsync(string deviceId, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/agent/prolog?deviceId={Uri.EscapeDataString(deviceId)}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
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

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

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

        if (_useGzip)
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

        using var resp = await _http.PostAsync(url, body, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"POST {url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase} — {text}");
        }
    }
}