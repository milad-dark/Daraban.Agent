using Daraban.Agent.Core.Agents;
using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Http;
using Daraban.Agent.Core.Models;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using System.Text.Json;

namespace Daraban.Agent.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // ---- Target / transport ------------------------------------------------------
        var serverOpt = new Option<string?>("--server") { Description = "Server base URL, e.g. http://localhost:5000" };
        var localOpt = new Option<string?>("--local") { Description = "Write results to this local directory instead of sending to a server" };
        var tagOpt = new Option<string?>("--tag") { Description = "Device id reported to the server (defaults to the machine name)" };
        var apiKeyOpt = new Option<string?>("--api-key") { Description = "Sent as the X-Api-Key header on every request, once server-side auth is enabled" };
        var proxyOpt = new Option<string?>("--proxy") { Description = "HTTP proxy for server communication: URL (http://host:port), 'none' to disable env-var proxy, default inherits HTTP_PROXY" };
        var sslKeystoreOpt = new Option<string?>("--ssl-keystore") { Description = "SSL client certificate source (Windows stores e.g. 'My,CA', or macOS keychain), mirrors glpi-agent ssl-keystore" };
        var sslFingerprintOpt = new Option<string?>("--ssl-fingerprint") { Description = "SHA-256 fingerprint (hex, colon-separated) of the server TLS cert to trust, mirrors glpi-agent ssl-fingerprint" };
        var fusionCompatOpt = new Option<bool>("--fusioninventory-compat") { Description = "Talk the legacy FusionInventory XML protocol (GLPI 9.5) instead of the native JSON API", DefaultValueFactory = _ => false };

        // ---- Task selection -------------------------------------------------------------
        var tasksOpt = new Option<string?>("--tasks")
        { Description = "Comma-separated tasks to run: local,netdiscovery,netinventory,remote,wakeonlan,deploy,esx (default: local)" };
        var noTaskOpt = new Option<string?>("--no-task") { Description = "Comma-separated tasks to skip" };

        // ---- Scheduling (daemon / service mode) ------------------------------------------
        var delayOpt = new Option<int>("--delay") { Description = "Seconds between scheduled runs", DefaultValueFactory = _ => 3600 };
        var lazyOpt = new Option<bool>("--lazy") { Description = "Add random jitter before each run, like glpi-agent --lazy", DefaultValueFactory = _ => false };
        var onceOpt = new Option<bool>("--once") { Description = "Run the selected tasks a single time and exit (default: loop forever on --delay)", DefaultValueFactory = _ => false };
        var fullInventoryPostponeOpt = new Option<int>("--full-inventory-postpone") { Description = "Runs between full inventories (0 disables partial/differential inventory, default 14)", DefaultValueFactory = _ => 14 };
        var requiredCategoryOpt = new Option<string?>("--required-category") { Description = "Comma-separated categories always included in a partial inventory (e.g. network,software)" };
        var additionalContentOpt = new Option<string?>("--additional-content") { Description = "Path to an XML/JSON file merged into the inventory before sending (mirrors glpi-agent additional-content)" };
        var noCategoryOpt = new Option<string?>("--no-category") { Description = "Comma-separated categories to exclude from the inventory (e.g. process,software), mirrors glpi-agent no-category" };
        var itemtypeOpt = new Option<string?>("--itemtype") { Description = "Inventory itemtype for GLPI 11+ custom asset types (e.g. \\Glpi\\CustomAsset\\ServerAsset), mirrors glpi-agent itemtype" };
        var esxItemtypeOpt = new Option<string?>("--esx-itemtype") { Description = "Itemtype for ESX/vCenter inventories (GLPI 11+), mirrors glpi-agent esx-itemtype" };

        // ---- HTTP status interface --------------------------------------------------------
        var httpPortOpt = new Option<int>("--http-port") { Description = "Agent HTTP status interface port", DefaultValueFactory = _ => 62354 };
        var httpTrustOpt = new Option<string?>("--http-trust") { Description = "CIDR range allowed to query the status endpoint" };
        var noHttpdOpt = new Option<bool>("--no-httpd") { Description = "Disable the HTTP status interface", DefaultValueFactory = _ => false };

        // ---- NetDiscovery / NetInventory ------------------------------------------------------
        var ipRangeOpt = new Option<string?>("--ip-range") { Description = "CIDR range to sweep, e.g. 192.168.1.0/24 (netdiscovery/netinventory)" };
        var communityOpt = new Option<string>("--snmp-community") { Description = "SNMP community string", DefaultValueFactory = _ => "public" };
        var snmpTimeoutOpt = new Option<int>("--snmp-timeout") { Description = "SNMP timeout in ms", DefaultValueFactory = _ => 2000 };
        var snmpRetriesOpt = new Option<int>("--snmp-retries") { Description = "Max SNMP retries per request when a device does not respond", DefaultValueFactory = _ => 0 };
        var threadsOpt = new Option<int>("--discovery-threads") { Description = "Parallel probes for netdiscovery/netinventory", DefaultValueFactory = _ => 32 };
        var snmpVersionOpt = new Option<string>("--snmp-version") { Description = "SNMP version: v1, v2c (default) or v3", DefaultValueFactory = _ => "v2c" };
        var snmpV3UserOpt = new Option<string?>("--snmp-v3-user") { Description = "SNMPv3 USM username" };
        var snmpV3AuthPassOpt = new Option<string?>("--snmp-v3-auth-pass") { Description = "SNMPv3 authentication password" };
        var snmpV3AuthProtoOpt = new Option<string>("--snmp-v3-auth-protocol") { Description = "SNMPv3 auth protocol: MD5, SHA or SHA256", DefaultValueFactory = _ => "MD5" };
        var snmpV3PrivPassOpt = new Option<string?>("--snmp-v3-priv-pass") { Description = "SNMPv3 privacy (encryption) password" };
        var snmpV3PrivProtoOpt = new Option<string>("--snmp-v3-priv-protocol") { Description = "SNMPv3 privacy protocol: DES or AES", DefaultValueFactory = _ => "AES" };

        // ---- WakeOnLan --------------------------------------------------------------------------
        var wolMacOpt = new Option<string?>("--wol-mac") { Description = "Comma-separated MAC addresses to wake" };
        var wolBroadcastOpt = new Option<string?>("--wol-broadcast") { Description = "Broadcast address for WoL packets (default 255.255.255.255)" };

        // ---- Deploy --------------------------------------------------------------------------------
        var deployWorkDirOpt = new Option<string?>("--deploy-workdir") { Description = "Directory to stage downloaded deploy files (default: temp)" };

        // ---- ESX / vCenter --------------------------------------------------------------------------
        var esxHostOpt = new Option<string?>("--esx-host") { Description = "vCenter/ESXi hostname or IP" };
        var esxUserOpt = new Option<string?>("--esx-user") { Description = "vCenter/ESXi username" };
        var esxPasswordOpt = new Option<string?>("--esx-password") { Description = "vCenter/ESXi password" };

        // ---- Legacy one-off collector (kept for quick manual testing) ---------------------------------
        var methodOpt = new Option<string?>("--method") { Description = "One-off collector to run directly, bypassing the task pipeline: local, ssh, snmp, winrm" };
        var hostOpt = new Option<string>("--host") { Description = "Target host (ssh/snmp/winrm)", DefaultValueFactory = _ => "192.168.1.1" };
        var userOpt = new Option<string>("--user") { Description = "Username (ssh/winrm)", DefaultValueFactory = _ => "root" };
        var passOpt = new Option<string>("--password") { Description = "Password (ssh/winrm) or SNMP community", DefaultValueFactory = _ => "password" };
        var fileOpt = new Option<string>("--file") { Description = "Output JSON file path for --method mode", DefaultValueFactory = _ => "inventory.json" };
        var agentIdOption = new Option<string?>(name: "--agent-id") { Description = "Unique identifier for this agent (default: machine hostname)" };
        var gzipOption = new Option<bool>(name: "--gzip") { Description = "Compress POST payloads with gzip before sending to server" };

        var rootCommand = new RootCommand("Daraban Agent CLI")
        {
serverOpt, localOpt, tagOpt, apiKeyOpt, proxyOpt, sslKeystoreOpt, sslFingerprintOpt, fusionCompatOpt,
            tasksOpt, noTaskOpt,
            delayOpt, lazyOpt, onceOpt, fullInventoryPostponeOpt, requiredCategoryOpt, additionalContentOpt, noCategoryOpt, itemtypeOpt, esxItemtypeOpt,
            httpPortOpt, httpTrustOpt, noHttpdOpt,
            ipRangeOpt, communityOpt, snmpTimeoutOpt, threadsOpt, snmpRetriesOpt,
            snmpVersionOpt, snmpV3UserOpt, snmpV3AuthPassOpt, snmpV3AuthProtoOpt, snmpV3PrivPassOpt, snmpV3PrivProtoOpt,
            wolMacOpt, wolBroadcastOpt,
            deployWorkDirOpt,
            esxHostOpt, esxUserOpt, esxPasswordOpt,
            methodOpt, hostOpt, userOpt, passOpt, fileOpt,
            agentIdOption, gzipOption,
        };

        rootCommand.SetAction(async (pr, ct) =>
        {
            var method = pr.GetValue(methodOpt);
            if (!string.IsNullOrWhiteSpace(method))
                return await RunOneOffCollectorAsync(method, pr.GetValue(hostOpt)!, pr.GetValue(userOpt)!, pr.GetValue(passOpt)!, pr.GetValue(fileOpt)!, ct);

            var options = BuildOptions(pr, serverOpt, localOpt, tagOpt, apiKeyOpt, proxyOpt, sslKeystoreOpt, sslFingerprintOpt, fusionCompatOpt, tasksOpt, noTaskOpt,
                delayOpt, lazyOpt, onceOpt, fullInventoryPostponeOpt, requiredCategoryOpt, additionalContentOpt, noCategoryOpt, itemtypeOpt, esxItemtypeOpt, httpPortOpt, httpTrustOpt, noHttpdOpt,
                ipRangeOpt, communityOpt, snmpTimeoutOpt, threadsOpt, snmpRetriesOpt, snmpVersionOpt, snmpV3UserOpt, snmpV3AuthPassOpt, snmpV3AuthProtoOpt, snmpV3PrivPassOpt, snmpV3PrivProtoOpt,
                wolMacOpt, wolBroadcastOpt, deployWorkDirOpt, esxHostOpt, esxUserOpt, esxPasswordOpt, agentIdOption, gzipOption);

            return await RunAgentAsync(options, ct);
        });

        rootCommand.Subcommands.Add(CreateListTasksCommand());

        var result = await rootCommand.Parse(args).InvokeAsync();

#if DEBUG
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
#endif
        return result;
    }

    // ------------------------------------------------------------------
    // Full agent run: prolog + selected tasks, once or forever (--once).
    // Same code path a Windows/systemd service uses via AgentRunner.
    // ------------------------------------------------------------------
    static async Task<int> RunAgentAsync(AgentOptions options, CancellationToken ct)
    {
        var status = new AgentStatusTracker();
        var runner = new AgentRunner(TaskRegistry.All, status);

        if (!options.NoHttpd)
            _ = RunStatusServerAsync(options.HttpPort, options.HttpTrust, status, ct);

        // Ctrl+C should stop the loop cleanly instead of killing the process mid-task.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            if (options.RunOnce)
                await runner.RunOnceAsync(options, cts.Token);
            else
                await runner.RunForeverAsync(options, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[agent] Stopped.");
        }

        return 0;
    }

    static async Task RunStatusServerAsync(int port, string? httpTrust, AgentStatusTracker status, CancellationToken ct)
    {
        try
        {
            var app = StatusEndpoint.BuildMinimalWeb(port, status, httpTrust);
            Console.WriteLine($"[agent] HTTP status interface listening on port {port} (/status).");
            await app.RunAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[agent] Failed to start HTTP interface: {ex.Message}");
        }
    }

    static Command CreateListTasksCommand()
    {
        var cmd = new Command("list-tasks", "List every task this agent build knows about");
        cmd.SetAction(_ =>
        {
            foreach (var task in TaskRegistry.All)
                Console.WriteLine(task.Name);
            return Task.FromResult(0);
        });
        return cmd;
    }

    static async Task RunStatusServerAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            var app = StatusEndpoint.BuildMinimalWeb(port);
            Console.WriteLine($"[agent] HTTP status interface listening on port {port} (/status).");
            await app.RunAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[agent] Failed to start HTTP interface: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Legacy quick collector — runs a single collector directly and dumps JSON.
    // Handy for "does this actually see the hardware" checks without a server.
    // ------------------------------------------------------------------
    static async Task<int> RunOneOffCollectorAsync(string method, string host, string user, string password, string file, CancellationToken ct)
    {
        DeviceInventory? inventory;
        Console.WriteLine($"[agent] Running {method} inventory...");

        try
        {
            inventory = method.ToLowerInvariant() switch
            {
                "local" => LocalCollectorFactory.CollectLocal(),
                "ssh" => await new SshRemoteCollector().CollectAsync(host, user, password, ct),
                "winrm" => await new WinrmRemoteCollector(host, user, password).CollectAsync(ct),
                "snmp" => await new SnmpNetworkCollector().DiscoverAsync(host, password, 3000, ct),
                _ => throw new ArgumentException($"Unknown method: {method}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[agent] Error: {ex.Message}");
            return 1;
        }

        var json = JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("--- INVENTORY JSON START ---");
        Console.WriteLine(json);
        Console.WriteLine("--- INVENTORY JSON END ---");
        await File.WriteAllTextAsync(file, json, ct);
        Console.WriteLine($"\n[agent] Inventory saved to: {Path.GetFullPath(file)}");
        return 0;
    }

    static AgentOptions BuildOptions(ParseResult pr,
        Option<string?> serverOpt, Option<string?> localOpt, Option<string?> tagOpt, Option<string?> apiKeyOpt, Option<string?> proxyOpt, Option<string?> sslKeystoreOpt, Option<string?> sslFingerprintOpt, Option<bool> fusionCompatOpt,
        Option<string?> tasksOpt, Option<string?> noTaskOpt,
        Option<int> delayOpt, Option<bool> lazyOpt, Option<bool> onceOpt, Option<int> fullInventoryPostponeOpt, Option<string?> requiredCategoryOpt, Option<string?> additionalContentOpt, Option<string?> noCategoryOpt, Option<string?> itemtypeOpt, Option<string?> esxItemtypeOpt,
        Option<int> httpPortOpt, Option<string?> httpTrustOpt, Option<bool> noHttpdOpt,
        Option<string?> ipRangeOpt, Option<string> communityOpt, Option<int> snmpTimeoutOpt, Option<int> threadsOpt, Option<int> snmpRetriesOpt,
        Option<string> snmpVersionOpt, Option<string?> snmpV3UserOpt, Option<string?> snmpV3AuthPassOpt, Option<string> snmpV3AuthProtoOpt, Option<string?> snmpV3PrivPassOpt, Option<string> snmpV3PrivProtoOpt,
        Option<string?> wolMacOpt, Option<string?> wolBroadcastOpt, Option<string?> deployWorkDirOpt,
        Option<string?> esxHostOpt, Option<string?> esxUserOpt, Option<string?> esxPasswordOpt, Option<string> agentId, Option<bool> gzipOption)
    {
        var options = new AgentOptions
        {
            Local = pr.GetValue(localOpt),
            Tag = pr.GetValue(tagOpt),
            ApiKey = pr.GetValue(apiKeyOpt),
            Proxy = pr.GetValue(proxyOpt),
            SslKeystore = pr.GetValue(sslKeystoreOpt),
            SslFingerprint = pr.GetValue(sslFingerprintOpt),
            FusionInventoryCompat = pr.GetValue(fusionCompatOpt),
            DelayTimeSeconds = pr.GetValue(delayOpt),
            Lazy = pr.GetValue(lazyOpt),
            RunOnce = pr.GetValue(onceOpt),
            FullInventoryPostpone = pr.GetValue(fullInventoryPostponeOpt),
            AdditionalContent = pr.GetValue(additionalContentOpt),
            Itemtype = pr.GetValue(itemtypeOpt),
            EsxItemtype = pr.GetValue(esxItemtypeOpt),
            HttpPort = pr.GetValue(httpPortOpt),
            HttpTrust = pr.GetValue(httpTrustOpt),
            NoHttpd = pr.GetValue(noHttpdOpt),
            IpRange = pr.GetValue(ipRangeOpt),
            SnmpCommunity = pr.GetValue(communityOpt) ?? "public",
            SnmpTimeoutMs = pr.GetValue(snmpTimeoutOpt),
            DiscoveryThreads = pr.GetValue(threadsOpt),
            SnmpRetries = pr.GetValue(snmpRetriesOpt),
            SnmpVersion = pr.GetValue(snmpVersionOpt) ?? "v2c",
            SnmpV3User = pr.GetValue(snmpV3UserOpt),
            SnmpV3AuthPass = pr.GetValue(snmpV3AuthPassOpt),
            SnmpV3AuthProtocol = pr.GetValue(snmpV3AuthProtoOpt) ?? "MD5",
            SnmpV3PrivPass = pr.GetValue(snmpV3PrivPassOpt),
            SnmpV3PrivProtocol = pr.GetValue(snmpV3PrivProtoOpt) ?? "AES",
            WakeOnLanBroadcast = pr.GetValue(wolBroadcastOpt),
            DeployWorkDir = pr.GetValue(deployWorkDirOpt),
            EsxHost = pr.GetValue(esxHostOpt),
            EsxUser = pr.GetValue(esxUserOpt),
            EsxPassword = pr.GetValue(esxPasswordOpt),
            UseGzip = pr.GetValue(gzipOption),
        };

        var tasksRaw = pr.GetValue(tasksOpt);
        if (!string.IsNullOrWhiteSpace(tasksRaw))
            options.Tasks.AddRange(tasksRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var serverRaw = pr.GetValue(serverOpt);
        if (!string.IsNullOrWhiteSpace(serverRaw))
            options.Servers.AddRange(serverRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var noTaskRaw = pr.GetValue(noTaskOpt);
        if (!string.IsNullOrWhiteSpace(noTaskRaw))
            options.NoTasks.AddRange(noTaskRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var requiredCategoryRaw = pr.GetValue(requiredCategoryOpt);
        if (!string.IsNullOrWhiteSpace(requiredCategoryRaw))
            options.RequiredCategories.AddRange(requiredCategoryRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var noCategoryRaw = pr.GetValue(noCategoryOpt);
        if (!string.IsNullOrWhiteSpace(noCategoryRaw))
            options.NoCategories.AddRange(noCategoryRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var wolMacRaw = pr.GetValue(wolMacOpt);
        if (!string.IsNullOrWhiteSpace(wolMacRaw))
            options.WakeOnLanMacs.AddRange(wolMacRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (pr.GetValue(agentId) is { } id)
            options.AgentId = id;

        return options;
    }
}