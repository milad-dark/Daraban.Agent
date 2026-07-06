using Daraban.Agent.Core.Models;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Daraban.Agent.Core.Transport;

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

    // ------------------------------------------------------------------
    // Prolog
    // ------------------------------------------------------------------

    public async Task<string?> PrologAsync(string deviceId, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/inventory?action=getConfig&machineid={Uri.EscapeDataString(deviceId)}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[glpi] Prolog failed: HTTP {(int)resp.StatusCode}");
                return null;
            }
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[glpi] Prolog error: {ex.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Inventory (native GLPI 10+ JSON format)
    // ------------------------------------------------------------------

    public Task PostInventoryAsync(string deviceId, object contentObject, string itemtype = "Computer", CancellationToken ct = default)
    {
        var payload = new
        {
            deviceid = deviceId,
            itemtype,
            action = "inventory",
            content = contentObject
        };
        return SendAsync("/api/inventory", payload, ct);
    }

    /// <summary>Kept for compatibility with existing tasks that already built their own JSON string.</summary>
    public Task PostInventoryAsync(string jsonPayload, CancellationToken ct = default)
        => SendRawAsync("/api/inventory", jsonPayload, ct);

    // ------------------------------------------------------------------
    // NetDiscovery
    // ------------------------------------------------------------------

    public Task PostDiscoveryAsync(string deviceId, IEnumerable<DiscoveredHost> hosts, CancellationToken ct = default)
    {
        var payload = new
        {
            deviceid = deviceId,
            action = "netdiscovery",
            timestampUtc = DateTime.UtcNow,
            content = hosts
        };
        return SendAsync("/api/networkdiscovery", payload, ct);
    }

    // ------------------------------------------------------------------
    // NetInventory (SNMP)
    // ------------------------------------------------------------------

    public Task PostNetInventoryAsync(string deviceId, IEnumerable<NetworkDeviceInventory> devices, CancellationToken ct = default)
    {
        var payload = new
        {
            deviceid = deviceId,
            action = "netinventory",
            timestampUtc = DateTime.UtcNow,
            content = devices
        };
        return SendAsync("/api/networkinventory", payload, ct);
    }

    // ------------------------------------------------------------------
    // WakeOnLan
    // ------------------------------------------------------------------

    public Task PostWakeOnLanResultAsync(string deviceId, IEnumerable<WakeOnLanResult> results, CancellationToken ct = default)
    {
        var payload = new
        {
            deviceid = deviceId,
            action = "wakeonlan",
            timestampUtc = DateTime.UtcNow,
            content = results
        };
        return SendAsync("/api/wakeonlan", payload, ct);
    }

    // ------------------------------------------------------------------
    // ESX / vCenter
    // ------------------------------------------------------------------

    public Task PostEsxInventoryAsync(string deviceId, EsxHostInfo host, CancellationToken ct = default)
    {
        // ESX hosts and their VMs are reported the same way a normal computer inventory is,
        // just with itemtype set appropriately for whatever custom asset type your GLPI expects.
        return PostInventoryAsync(deviceId, host, itemtype: "Daraban\\CustomAsset\\EsxAsset", ct: ct);
    }

    // ------------------------------------------------------------------
    // Deploy
    // ------------------------------------------------------------------

    public async Task<List<DeployJob>> GetPendingDeployJobsAsync(string deviceId, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/deploy?action=getJobs&machineid={Uri.EscapeDataString(deviceId)}";
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
            Console.WriteLine($"[glpi] GetPendingDeployJobs error: {ex.Message}");
            return [];
        }
    }

    public Task PostDeployResultAsync(string deviceId, DeployJobResult result, CancellationToken ct = default)
    {
        var payload = new
        {
            deviceid = deviceId,
            action = "deployResult",
            content = result
        };
        return SendAsync("/api/deploy", payload, ct);
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private Task SendAsync(string url, object payload, CancellationToken ct)
        => SendRawAsync(url, JsonSerializer.Serialize(payload), ct);

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

    private static async Task PostJsonAsync(HttpClient http, string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(url, content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    //public Task PostDiscoveryAsync(string jsonPayload, CancellationToken ct = default)
    //    => PostJsonAsync(_http, "/api/discovery", new { action = "netdiscovery", content = jsonPayload }, ct);

    //public Task PostNetInventoryAsync(string jsonPayload, CancellationToken ct = default)
    //    => PostJsonAsync(_http, "/api/netinventory", new { action = "netinventory", content = jsonPayload }, ct);
}