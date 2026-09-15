using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Daraban.Agent.Core.Transport;

/// <summary>
/// Legacy FusionInventory protocol client (GLPI 9.5 + FusionInventory plugin).
///
/// The FusionInventory agent talks to `communication.php` with XML bodies:
///   prolog:    POST {server}/plugins/fusioninventory/communication.php?action=prolog
///              <glpi><deviceID>uuid</deviceID><hardware>...</hardware></glpi>
///   inventory: POST ...?action=inventory
///              <request><QUERY>INVENTORY</QUERY><DEVICEID>uuid</DEVICEID><CONTENT>...</CONTENT></request>
///
/// This implementation covers the prolog handshake and a best-effort XML inventory
/// serialization of DeviceContent. It implements IDarabanClient so it plugs into the
/// same multi-target delivery + service DI as the native JSON client.
/// </summary>
public sealed class FusionInventoryClient : IDarabanClient
{
    private const string CommunicationPath = "/plugins/fusioninventory/communication.php";
    private readonly HttpClient _http;
    private readonly AgentOptions _options;

    public string ServerUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    public FusionInventoryClient(HttpClient http, AgentOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<PrologResponse?> PrologAsync(string deviceId, CancellationToken ct = default)
    {
        try
        {
            var xml = new XElement("glpi",
                new XElement("deviceID", deviceId),
                new XElement("hardware"));

            using var content = BuildXmlContent(xml);
            using var request = CreateRequest(HttpMethod.Post, $"{CommunicationPath}?action=prolog", content);
            using var resp = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[server] FusionInventory prolog failed: HTTP {(int)resp.StatusCode}");
                return null;
            }

            var responseXml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParsePrologXml(responseXml);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[server] FusionInventory prolog error: {ex.Message}");
            return null;
        }
    }

    // ── inventory ──────────────────────────────────────────────────────────────

    public Task PostInventoryAsync(string deviceId, object contentObject, string itemtype = "Computer", CancellationToken ct = default)
    {
        // contentObject may be a DeviceContent (from LocalInventoryTask) or a JSON string (from
        // RemoteInventoryTask). Normalize: if it's a string, deserialize to DeviceContent first.
        DeviceContent content = contentObject switch
        {
            DeviceContent dc => dc,
            string json => TryDeserializeContent(json) ?? new DeviceContent(),
            _ => new DeviceContent()
        };
        return SendInventoryAsync(deviceId, content, ct);
    }

    public Task PostInventoryAsync(string jsonPayload, string itemtype = "Computer", CancellationToken ct = default)
    {
        DeviceContent content;
        string deviceId = "unknown";
        try
        {
            var doc = JsonDocument.Parse(jsonPayload);
            if (doc.RootElement.TryGetProperty("DeviceId", out var idEl))
                deviceId = idEl.GetString() ?? deviceId;
            content = JsonSerializer.Deserialize<DeviceContent>(
                doc.RootElement.TryGetProperty("Content", out var c) ? c.GetRawText() : "{}",
                JsonOpts) ?? new DeviceContent();
        }
        catch
        {
            content = new DeviceContent();
        }

        return SendInventoryAsync(deviceId, content, ct);
    }

    private async Task SendInventoryAsync(string deviceId, DeviceContent content, CancellationToken ct)
    {
        var contentXml = ToFusionInventoryXml(content);

        var request = new XElement("request",
            new XElement("QUERY", "INVENTORY"),
            new XElement("DEVICEID", deviceId),
            new XElement("CONTENT", contentXml));

        using var body = BuildXmlContent(request);
        using var httpReq = CreateRequest(HttpMethod.Post, $"{CommunicationPath}?action=inventory", body);
        using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"FusionInventory inventory POST failed: {(int)resp.StatusCode} — {text}");
        }
    }

    // ── task/result endpoints (FusionInventory supports netdiscovery etc. too) ──

    public Task PostDiscoveryAsync(string deviceId, IEnumerable<DiscoveredHost> hosts, CancellationToken ct = default)
        => PostGenericResultsAsync(deviceId, "DISCOVERY", hosts.Select(h => new
        {
            h.IpAddress, h.MacAddress, h.Hostname, h.DeviceType
        }), ct);

    public Task PostNetInventoryAsync(string deviceId, IEnumerable<NetworkDeviceInventory> devices, CancellationToken ct = default)
        => PostGenericResultsAsync(deviceId, "NETINVENTORY", devices, ct);

    public Task PostWakeOnLanResultAsync(string deviceId, IEnumerable<WakeOnLanResult> results, CancellationToken ct = default)
        => PostGenericResultsAsync(deviceId, "WAKEONLAN", results, ct);

    // itemtype is a GLPI 11+ concept — the legacy XML protocol has no equivalent, so the
    // parameter is accepted for interface parity and ignored.
    public Task PostEsxInventoryAsync(string deviceId, EsxHostInfo host, string itemtype = "EsxHost", CancellationToken ct = default)
        => PostGenericResultsAsync(deviceId, "ESX", new[] { host }, ct);

    public Task PostDeployResultAsync(string deviceId, DeployJobResult result, CancellationToken ct = default)
        => PostGenericResultsAsync(deviceId, "DEPLOY", new[] { result }, ct);

    // ── jobs (deploy + collect) ────────────────────────────────────────────────

    public async Task<List<DeployJob>> GetPendingDeployJobsAsync(string deviceId, CancellationToken ct = default)
    {
        // FusionInventory's deploy protocol is a separate XML exchange (plan/job list). For the
        // legacy client we return an empty list so the task degrades gracefully in compat mode.
        return [];
    }

    public async Task<List<CollectJob>> GetCollectJobsAsync(CancellationToken ct)
    {
        return [];
    }

    public Task PostCollectResultsAsync(IList<CollectResult> results, CancellationToken ct)
        => PostGenericResultsAsync(_options.AgentId ?? Environment.MachineName, "COLLECT", results, ct);

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task PostGenericResultsAsync(string deviceId, string query, object payload, CancellationToken ct)
    {
        var xml = new XElement("request",
            new XElement("QUERY", query),
            new XElement("DEVICEID", deviceId),
            new XElement("CONTENT", JsonToXmlElement(payload)));

        using var body = BuildXmlContent(xml);
        using var httpReq = CreateRequest(HttpMethod.Post, $"{CommunicationPath}?action={query.ToLowerInvariant()}", body);
        using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            // Results endpoints are fire-and-forget in FusionInventory compat mode;
            // log but don't throw so one failure doesn't kill delivery to other targets.
            Console.Error.WriteLine($"[server] FusionInventory {query} POST failed: HTTP {(int)resp.StatusCode}");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent content)
    {
        var request = new HttpRequestMessage(method,
            new Uri(_http.BaseAddress!.ToString().TrimEnd('/') + "/" + path.TrimStart('/')));

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.Add("X-Api-Key", _options.ApiKey);
        request.Content = content;
        return request;
    }

    private static HttpContent BuildXmlContent(XElement root)
    {
        var sb = new StringBuilder();
        using (var writer = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings { OmitXmlDeclaration = false, Indent = false }))
            root.Save(writer);

        var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        return content;
    }

    private static PrologResponse? ParsePrologXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null)
                return null;

            var response = new PrologResponse();
            var tasks = root.Element("TASKS");
            if (tasks is not null)
                response.Tasks = tasks.Elements("TASKS")? // actual FusionInventory nests per-task blocks
                    .Select(t => t.Name.LocalName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

            var workers = root.Element("WORKERS")?.Value;
            if (int.TryParse(workers, out var w))
                response.Workers = w;

            response.ServedSince = root.Element("SERVED_SINCE")?.Value;
            response.Config = root.Element("CONFIG")?.ToString();
            response.GlpiVersion = root.Element("VERSION")?.Value;

            return response;
        }
        catch (System.Xml.XmlException ex)
        {
            Console.Error.WriteLine($"[server] FusionInventory prolog response was not valid XML: {ex.Message}");
            return null;
        }
    }

    public static DeviceContent? TryDeserializeContent(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DeviceContent>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── DeviceContent → FusionInventory XML ────────────────────────────────────

    /// <summary>Maps a DeviceContent into the FusionInventory <c>HARDWARE</c> XML element.</summary>
    public static XElement ToFusionInventoryXml(DeviceContent content)
    {
        var root = new XElement("HARDWARE");

        AddText(root, "NAME", content.ComputerName);
        AddText(root, "OSNAME", content.OperatingSystem);
        AddText(root, "OSVERSION", null);
        AddText(root, "ARCH", content.OsArchitecture);
        AddText(root, "DOMAIN", content.Domain);
        AddText(root, "WORKGROUP", content.Workgroup);
        AddText(root, "USERID", content.LoggedOnUser);

        // BIOS
        if (content.Bios is not null)
        {
            AddText(root, "BIOS_MANUFACTURER", content.Bios.Manufacturer);
            AddText(root, "BIOS_VERSION", content.Bios.Version);
            AddText(root, "BIOS_DATE", content.Bios.ReleaseDate);
            AddText(root, "BIOS_SERIAL", content.Bios.SerialNumber);
        }

        // Computer system
        if (content.ComputerSystem is not null)
        {
            AddText(root, "MANUFACTURER", content.ComputerSystem.Manufacturer);
            AddText(root, "MODEL", content.ComputerSystem.Model);
            AddText(root, "MEMORY", content.ComputerSystem.TotalPhysicalMemory);
        }
        AddText(root, "MOTHERBOARD_SERIAL", content.MotherboardSerial);
        AddText(root, "MOTHERBOARD_MODEL", content.MotherboardModel);

        // Software
        var softwares = new XElement("SOFTWARES");
        foreach (var s in content.Software)
        {
            softwares.Add(new XElement("SOFTWARE",
                new XElement("NAME", s.Name ?? ""),
                new XElement("VERSION", s.Version ?? ""),
                new XElement("PUBLISHER", s.Vendor ?? ""),
                new XElement("COMMENTS", s.Caption ?? ""),
                new XElement("HELPLINK", null)));
        }
        root.Add(softwares);

        // CPUs
        var cpus = new XElement("CPUS");
        foreach (var c in content.Cpus)
        {
            cpus.Add(new XElement("CPU",
                new XElement("NAME", c.Name ?? ""),
                new XElement("SPEED", c.Speed ?? ""),
                new XElement("CORES", c.Cores ?? "")));
        }
        root.Add(cpus);

        // Storage
        var storages = new XElement("STORAGES");
        foreach (var s in content.Storages)
        {
            storages.Add(new XElement("STORAGE",
                new XElement("MODEL", s.Model ?? ""),
                new XElement("SIZE", s.Size ?? ""),
                new XElement("DISKSIZE", s.Size ?? ""),
                new XElement("TYPE", s.InterfaceType ?? "")));
        }
        root.Add(storages);

        // Network interfaces
        var networks = new XElement("NETWORKS");
        foreach (var n in content.Networks)
        {
            networks.Add(new XElement("NETWORK",
                new XElement("DESCRIPTION", n.Description ?? ""),
                new XElement("MACADDR", n.MACAddress ?? ""),
                new XElement("IPADDRESS", n.IPAddress ?? ""),
                new XElement("STATUS", n.Status ?? "")));
        }
        root.Add(networks);

        // Video controllers
        var videos = new XElement("VIDEOS");
        foreach (var v in content.VideoControllers)
        {
            videos.Add(new XElement("VIDEO",
                new XElement("NAME", v.Name ?? ""),
                new XElement("CHIPSET", v.VideoProcessor ?? ""),
                new XElement("MEMORY", v.AdapterRAM ?? ""),
                new XElement("DRIVER", v.DriverVersion ?? "")));
        }
        root.Add(videos);

        // Monitors
        var monitors = new XElement("MONITORS");
        foreach (var m in content.Monitors)
        {
            monitors.Add(new XElement("MONITOR",
                new XElement("SN", m.Serial ?? ""),
                new XElement("CAPTION", m.Name ?? ""),
                new XElement("MANUFACTURER", m.Manufacturer ?? "")));
        }
        root.Add(monitors);

        // Printers
        var printers = new XElement("PRINTERS");
        foreach (var p in content.Printers)
        {
            printers.Add(new XElement("PRINTER",
                new XElement("NAME", p.Name ?? ""),
                new XElement("DRIVER", p.DriverName ?? ""),
                new XElement("PORT", p.PortName ?? "")));
        }
        root.Add(printers);

        // Users
        var users = new XElement("USERS");
        foreach (var u in content.LocalUserAccounts)
        {
            users.Add(new XElement("USER",
                new XElement("LOGIN", u.Name ?? ""),
                new XElement("NAME", u.FullName ?? "")));
        }
        root.Add(users);

        return root;
    }

    private static void AddText(XElement parent, string name, string? value)
    {
        if (value is not null)
            parent.Add(new XElement(name, value));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Serializes a payload object as a compact JSON element for the CONTENT block.</summary>
    private static XElement JsonToXmlElement(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        return new XElement("JSON", json);
    }
}