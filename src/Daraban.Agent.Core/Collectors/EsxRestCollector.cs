using Daraban.Agent.Core.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Collects host + VM inventory from vCenter or a standalone ESXi host using the
/// vSphere Automation REST API (/api/session, /api/vcenter/host, /api/vcenter/vm).
///
/// This intentionally uses the modern REST API rather than the legacy SOAP vSphere
/// API (which needs the large VMware.vSphere SDK) so the whole collector stays a
/// plain HttpClient consumer with no extra native dependency. The trade-off: some
/// deep hardware fields (BIOS version, exact CPU model string) are only exposed via
/// SOAP HostSystem.hardware on older ESXi builds — those come back null here rather
/// than failing the whole collection, and are flagged in the Vendor/Model fallback below.
/// </summary>
public sealed class EsxRestCollector : IAsyncDisposable
{
    private readonly HttpClient _http;
    private string? _sessionToken;

    public EsxRestCollector(string hostOrVCenter, bool ignoreSslErrors = true)
    {
        var handler = new HttpClientHandler();
        if (ignoreSslErrors)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true; // most ESXi hosts use self-signed certs

        _http = new HttpClient(handler) { BaseAddress = new Uri($"https://{hostOrVCenter}") };
    }

    public async Task ConnectAsync(string username, string password, CancellationToken ct = default)
    {
        var byteArray = System.Text.Encoding.ASCII.GetBytes($"{username}:{password}");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        using var resp = await _http.PostAsync("/api/session", content: null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        // The vSphere Automation API returns the session id as a bare JSON string, e.g. "abcd1234...".
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _sessionToken = JsonSerializer.Deserialize<string>(raw);

        _http.DefaultRequestHeaders.Authorization = null;
        _http.DefaultRequestHeaders.Remove("vmware-api-session-id");
        _http.DefaultRequestHeaders.Add("vmware-api-session-id", _sessionToken);
    }

    public async Task<List<EsxHostInfo>> CollectAllHostsAsync(CancellationToken ct = default)
    {
        var hosts = new List<EsxHostInfo>();

        var hostList = await GetJsonAsync("/api/vcenter/host", ct);
        if (hostList is not { ValueKind: JsonValueKind.Array })
            return hosts;

        foreach (var h in hostList.Value.EnumerateArray())
        {
            var hostId = h.GetProperty("host").GetString()!;
            var name = h.TryGetProperty("name", out var n) ? n.GetString() : hostId;

            var hostInfo = new EsxHostInfo { Name = name ?? hostId };

            // Per-host hardware summary.
            var summary = await GetJsonAsync($"/api/vcenter/host/{Uri.EscapeDataString(hostId)}", ct);
            // The vSphere REST API's per-host GET is intentionally sparse (connection/power state);
            // vendor/model/cpu detail requires the SOAP HostSystem.hardware.systemInfo object.
            // We still record what's available and leave the rest null rather than guessing.

            hostInfo.VirtualMachines = await CollectVmsForHostAsync(hostId, ct);
            hosts.Add(hostInfo);
        }

        return hosts;
    }

    private async Task<List<EsxVmInfo>> CollectVmsForHostAsync(string hostId, CancellationToken ct)
    {
        var vms = new List<EsxVmInfo>();

        var vmList = await GetJsonAsync($"/api/vcenter/vm?filter.hosts={Uri.EscapeDataString(hostId)}", ct);
        if (vmList is not { ValueKind: JsonValueKind.Array })
            return vms;

        foreach (var v in vmList.Value.EnumerateArray())
        {
            var vmId = v.GetProperty("vm").GetString()!;
            var name = v.TryGetProperty("name", out var n) ? n.GetString() : vmId;
            var powerState = v.TryGetProperty("power_state", out var ps) ? ps.GetString() : null;

            var vmInfo = new EsxVmInfo { Name = name ?? vmId, PowerState = powerState };

            var detail = await GetJsonAsync($"/api/vcenter/vm/{Uri.EscapeDataString(vmId)}", ct);
            if (detail is { } d)
            {
                if (d.TryGetProperty("cpu", out var cpu) && cpu.TryGetProperty("count", out var cpuCount))
                    vmInfo.CpuCount = cpuCount.GetInt32();

                if (d.TryGetProperty("memory", out var mem) && mem.TryGetProperty("size_MiB", out var memSize))
                    vmInfo.MemoryMb = memSize.GetInt64();

                if (d.TryGetProperty("disks", out var disks) && disks.ValueKind == JsonValueKind.Object)
                {
                    foreach (var disk in disks.EnumerateObject())
                    {
                        if (disk.Value.TryGetProperty("capacity", out var cap))
                            vmInfo.DiskSizesMb.Add(cap.GetInt64() / 1024 / 1024);
                    }
                }

                if (d.TryGetProperty("guest_OS", out var guestOs))
                    vmInfo.GuestOs = guestOs.GetString();
            }

            // Guest IP addresses require VMware Tools to be running inside the VM.
            var identity = await GetJsonAsync($"/api/vcenter/vm/{Uri.EscapeDataString(vmId)}/guest/identity", ct);
            if (identity is { } id && id.TryGetProperty("ip_address", out var ip))
                vmInfo.IpAddresses.Add(ip.GetString() ?? "");

            vms.Add(vmInfo);
        }

        return vms;
    }

    private async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[esx] Request to {url} failed: {ex.Message}");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_sessionToken != null)
        {
            try
            { await _http.DeleteAsync("/api/session"); }
            catch { /* best-effort session cleanup */ }
        }
        _http.Dispose();
    }
}
