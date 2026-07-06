using Daraban.Agent.Core.Models;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Daraban.Agent.Core.Collectors;

public class WinrmRemoteCollector
{
    private readonly HttpClient _http;
    private readonly string _hostname;
    private readonly string _username;
    private readonly string _password;

    public WinrmRemoteCollector(string hostname, string username, string password, bool https = false)
    {
        _hostname = hostname;
        _username = username;
        _password = password;

        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (m, c, ch, e) => true }; // Accept self-signed
        _http = new HttpClient(handler);
        _http.BaseAddress = new Uri($"{(https ? "https" : "http")}://{hostname}:5985/wsman");
        _http.DefaultRequestHeaders.Add("Content-Type", "application/soap+xml; charset=utf-8");
    }

    public async Task<DeviceInventory> CollectAsync(CancellationToken ct = default)
    {
        var content = new DeviceContent { ComputerName = _hostname };

        try
        {
            // 1. Create Shell (WinRM WS-Man protocol requirement)
            string shellId = await CreateShellIdAsync(ct);

            // 2. Get OS and Hardware via WMI queries
            var wmiOsCmd = "wmic os get Name, Version, OSLanguage, OSArchitecture /format:list";
            var osResult = await ExecuteCommandAsync(shellId, wmiOsCmd, ct);
            if (osResult != null)
            {
                var parts = osResult.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                if (parts.Count >= 1)
                    content.OperatingSystem = parts[0]; // Name
                if (parts.Count >= 2)
                    content.OperatingSystem += " " + parts[1]; // Version
                if (parts.Count >= 4)
                    content.OsArchitecture = parts[3];
            }

            var wmiCpuCmd = "wmic cpu get Name, NumberOfCores, MaxClockSpeed /format:list";
            var cpuResult = await ExecuteCommandAsync(shellId, wmiCpuCmd, ct);
            if (cpuResult != null)
            {
                var cpuLines = cpuResult.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1); // Skip header
                foreach (var line in cpuLines)
                {
                    var p = line.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                    if (p.Count >= 3)
                        content.Cpus.Add(new CpuInfo { Name = p[0], Cores = p[1], Speed = p[2] });
                }
            }

            var wmiMemCmd = "wmic memorychip get Capacity, Speed, MemoryType /format:list";
            var memResult = await ExecuteCommandAsync(shellId, wmiMemCmd, ct);
            if (memResult != null)
            {
                var memLines = memResult.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1);
                foreach (var line in memLines)
                {
                    var p = line.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                    if (p.Count >= 3)
                        content.Memories.Add(new MemoryInfo { Capacity = int.Parse(p[0]) / 1024, Speed = long.Parse(p[1]), MemoryType = p[2] });
                }
            }

            var wmiDiskCmd = "wmic diskdrive get Model, Size, InterfaceType, SerialNumber /format:list";
            var diskResult = await ExecuteCommandAsync(shellId, wmiDiskCmd, ct);
            if (diskResult != null)
            {
                var diskLines = diskResult.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1);
                foreach (var line in diskLines)
                {
                    var p = line.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                    if (p.Count >= 2)
                        content.Storages.Add(new StorageInfo { Model = p[0], Size = $"{long.Parse(p[1]) / 1024 / 1024 / 1024} GB", InterfaceType = p[2], Serial = p.Count >= 4 ? p[3] : "" });
                }
            }

            // 3. Delete Shell
            await DeleteShellAsync(shellId, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WinRM] Failed to query {_hostname}: {ex.Message}");
        }

        return new DeviceInventory { Content = JsonSerializer.Serialize(content) };
    }

    private async Task<string> ExecuteCommandAsync(string shellId, string command, CancellationToken ct)
    {
        var soap = $"""
                <s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"" xmlns:a=""http://www.w3.org/2005/08/addressing"" xmlns:w=""http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd" xmlns:
            p = ""http://schemas.microsoft.com/wbem/wsman/1/wsman.xsd"">
                  < s:Header >< a:To > wsman:{_hostname}</ a:To >< a:ReplyTo ></ a:ReplyTo >< a:Action > http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Command</a:Action></s:Header>
                  < s:Body >
                    < w:w = ""http://schemas.microsoft.com/wbem/wsman/1/windows/shell" s:encoding=""utf-8" xml:lang=""en-US""><w:ShellId>{shellId}</w:Command><w:Command>{System.Security.SecurityElement.Escape(command)}</w:Command></w:CommandLine>
                  </ s:Body >
                </ s:Envelope >
            """;

        var content = new StringContent(soap, Encoding.UTF8, "application/soap+xml");
        var response = await _http.PostAsync("", content, ct);
        response.EnsureSuccessStatusCode();

        var doc = XDocument.Load(await response.Content.ReadAsStreamAsync(ct));
        XNamespace ns = "http://schemas.microsoft.com/wbem/wsman/1/windows/shell";
        return doc.Descendants(ns + "StdOut").FirstOrDefault()?.Value ?? "";
    }

    private async Task<string> CreateShellIdAsync(CancellationToken ct)
    {
        var soap = $"""
            <s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"" xmlns:a=""http://www.w3.org/2005/08/addressing"" xmlns:w=""http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd" xmlns:p=""http://schemas.microsoft.com/wbem/wsman/1/wsman.xsd" xmlns:rsp=""http://schemas.microsoft.com/wbem/wsman/1/windows/shell"">
              <s:Header><a:To>wsman:{_hostname}</a:To><a:ReplyTo></a:ReplyTo><a:Action>http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Create</a:Action></s:Header>
              <s:Body><w:CommandLine xmlns:w=""http://schemas.microsoft.com/wbem/wsman/1/windows/shell"" s:encoding=""utf-8" xml:lang=""en-US""><w:InputStreams/><w:OutputStreams w:stdout=""Buffer=""true"" w:stderr=""Buffer=""true""/></w:CommandLine></s:Body>
            </s:Envelope>
            """;

        var content = new StringContent(soap, Encoding.UTF8, "application/soap+xml");

        // Add Basic Auth
        var byteArray = Encoding.ASCII.GetBytes($"{_username}:{_password}");
        var authHeader = Convert.ToBase64String(byteArray);
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

        var response = await _http.PostAsync("", content, ct);
        response.EnsureSuccessStatusCode();

        var doc = XDocument.Load(await response.Content.ReadAsStreamAsync(ct));
        XNamespace ns = "http://schemas.microsoft.com/wbem/wsman/1/windows/shell";
        return doc.Descendants(ns + "ShellId").FirstOrDefault()?.Value ?? throw new Exception("Failed to create WinRM shell.");
    }

    private async Task DeleteShellAsync(string shellId, CancellationToken ct)
    {
        var soap = $"""
                <s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"" xmlns:a=""http://www.w3.org/2005/08/addressing"" xmlns:w=""http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd"" xmlns:p=""http://schemas.microsoft.com/wbem/wsman/1/wsman.xsd" xmlns:
            rsp = ""http://schemas.microsoft.com/wbem/wsman/1/windows/shell"">
                  < s:Header >< a:To > wsman:{_hostname}</ a:To >< a:ReplyTo ></ a:ReplyTo >< a:Action > http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Signal</a:Action></s:Header>
                  < s:Body >< rsp:rsp = ""http://schemas.microsoft.com/wbem/wbem/wscim/v1" xml:lang=""en-US" w:ResourceURI=""http://schemas.microsoft.com/wbem/wsman/1/windows/shell/{shellId}"/></s:Body>
                </ s:Envelope >
            """;

        var content = new StringContent(soap, Encoding.UTF8, "application/soap+xml");
        await _http.PostAsync("", content, ct);
    }
}