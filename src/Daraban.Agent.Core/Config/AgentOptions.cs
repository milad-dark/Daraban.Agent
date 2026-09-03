namespace Daraban.Agent.Core.Config;

public sealed class AgentOptions
{
    // Target definition (like GLPI --server/--local)
    // Multiple execution targets are supported (mirrors glpi-agent's comma-separated `server`).
    public List<string> Servers { get; set; } = new();

    /// <summary>
    /// Convenience accessor for the primary (first) server. Backwards-compatible with
    /// code that reads `options.Server`. Setting it replaces the server list.
    /// </summary>
    public string? Server
    {
        get => Servers.Count > 0 ? Servers[0] : null;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Servers.Clear();
            }
            else
            {
                Servers.Clear();
                Servers.Add(value);
            }
        }
    }

    public string? Local { get; set; }    // write results locally to this directory

    // Scheduling (mirrors --delaytime and --lazy)
    public int DelayTimeSeconds { get; set; } = 3600;
    public bool Lazy { get; set; } = false;

    // Task selection (mirrors --tasks/--no-task/--list-tasks)
    public List<string> Tasks { get; set; } = new();   // e.g. ["local", "netdiscovery", "netinventory", "remote", "wakeonlan", "deploy", "esx"]
    public List<string> NoTasks { get; set; } = new();

    // HTTP interface (mirrors httpd-port, httpd-trust)
    public int HttpPort { get; set; } = 62354;
    public string? HttpTrust { get; set; }  // e.g. "192.168.1.0/24"
    public bool NoHttpd { get; set; } = false;

    // Optional: tag / machine id (helps the server identify the agent)
    public string? Tag { get; set; }

    // Sent as X-Api-Key header on every request once the server has auth enabled.
    public string? ApiKey { get; set; }

    /// <summary>
    /// SSL client certificate source, mirroring glpi-agent's `ssl-keystore`. On Windows,
    /// comma-separated store names to search for a client certificate (e.g. "My", "CA",
    /// "Root", "User-My"). On macOS the user keychain is used. Null disables the feature.
    /// </summary>
    public string? SslKeystore { get; set; }

    /// <summary>
    /// SHA-256 fingerprint of the server TLS certificate to trust (hex, colon-separated,
    /// e.g. "AA:BB:..."). When set, only a server presenting this certificate is accepted.
    /// Mirrors glpi-agent's `ssl-fingerprint`.
    /// </summary>
    public string? SslFingerprint { get; set; }

    /// <summary>
    /// When true, talks the legacy FusionInventory XML protocol (GLPI 9.5 + FusionInventory
    /// plugin) at /plugins/fusioninventory/communication.php instead of the native JSON API.
    /// Mirrors glpi-agent's backward-compatibility with FusionInventory for GLPI.
    /// </summary>
    public bool FusionInventoryCompat { get; set; } = false;

    // OAuth2 client-credentials configuration. ClientSecret must be supplied by a secret
    // provider/environment variable in production, never committed to appsettings.json.
    public string? OAuthTokenEndpoint { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthClientSecret { get; set; }
    public string? OAuthScope { get; set; } = "daraban.agent.inventory";

    // "Run once and exit" vs. the default "loop forever on DelayTimeSeconds" scheduler.
    public bool RunOnce { get; set; } = false;

    /// <summary>
    /// HTTP proxy URL used for server communication. Mirrors glpi-agent's `proxy` option:
    ///   null  → inherit from HTTP_PROXY/HTTPS_PROXY environment variables (HttpClient default)
    ///   "none" → disable proxy entirely, ignoring environment variables
    ///   "http://host:port" → use the given proxy explicitly
    /// </summary>
    public string? Proxy { get; set; }

    /// <summary>
    /// Unique identifier for this agent instance.
    /// Sent with every POST so the server can correlate data to a machine
    /// without relying on IP address (which can change).
    /// Defaults to the machine's hostname if not explicitly set.
    /// </summary>
    public string? AgentId { get; set; } = Environment.GetEnvironmentVariable("DARABAN_AGENT_ID") ?? Environment.MachineName;

    /// <summary>
    /// When true, POST bodies are gzip-compressed before sending.
    /// Mirrors glpi-agent's compression option — reduces bandwidth for
    /// large inventory payloads on slow links.
    /// </summary>
    public bool UseGzip { get; set; } = false;
    public int Threads { get; set; } = 4;

    // ---- Partial inventory (mirrors glpi-agent full-inventory-postpone) ----------
    /// <summary>
    /// Number of runs before the agent is allowed to report a partial inventory
    /// (only changed categories) instead of a full one. Mirrors glpi-agent's
    /// `full-inventory-postpone`. 0 disables the feature (always full inventory).
    /// Default matches glpi-agent (14).
    /// </summary>
    public int FullInventoryPostpone { get; set; } = 14;

    /// <summary>
    /// Categories that are always included in a partial inventory, even when unchanged
    /// (mirrors glpi-agent `required-category`). Useful for GLPI business rules that rely
    /// on category content, e.g. "network" for IP-range analysis.
    /// </summary>
    public List<string> RequiredCategories { get; set; } = new();


    // ---- NetDiscovery / NetInventory ----------------------------------------
    public string? IpRange { get; set; }          // e.g. "192.168.1.0/24"
    public string SnmpCommunity { get; set; } = "public";
    public int SnmpTimeoutMs { get; set; } = 2000;
    public int DiscoveryThreads { get; set; } = 32;

    // ---- SNMP version / v3 USM (netdiscovery / netinventory) ----------------
    /// <summary>SNMP version: "v1", "v2c" (default) or "v3".</summary>
    public string SnmpVersion { get; set; } = "v2c";
    public string? SnmpV3User { get; set; }
    public string? SnmpV3AuthPass { get; set; }
    /// <summary>Authentication protocol for SNMPv3: "MD5", "SHA" or "SHA256".</summary>
    public string SnmpV3AuthProtocol { get; set; } = "MD5";
    public string? SnmpV3PrivPass { get; set; }
    /// <summary>Privacy (encryption) protocol for SNMPv3: "DES" or "AES".</summary>
    public string SnmpV3PrivProtocol { get; set; } = "AES";

    /// <summary>
    /// Maximum number of times a SNMP request is retried after a device fails to
    /// respond. Mirrors glpi-agent's `snmp-retries` (default 0, no retry).
    /// </summary>
    public int SnmpRetries { get; set; } = 0;

    // ---- WakeOnLan -----------------------------------------------------------
    public List<string> WakeOnLanMacs { get; set; } = new();   // "AA:BB:CC:DD:EE:FF"
    public string? WakeOnLanBroadcast { get; set; }             // defaults to 255.255.255.255

    // ---- Deploy ----------------------------------------------------------------
    public string? DeployWorkDir { get; set; }     // where downloaded packages are staged (defaults to temp)

    // ---- ESX / vCenter -----------------------------------------------------------
    public string? EsxHost { get; set; }           // vCenter/ESXi hostname or IP
    public string? EsxUser { get; set; }
    public string? EsxPassword { get; set; }
    public bool EsxIgnoreSslErrors { get; set; } = false;

    // ---- Remote (RemoteInventoryTask) -----------------------------------------
    // Connection strings for machines this agent should inventory remotely, e.g.
    //   ssh://root:mypassword@10.0.0.5
    //   winrm://Administrator:mypassword@10.0.0.6:5985
    // Mirrors glpi-agent's "remote" concept (`glpi-remote add/list`), simplified to a
    // flat config list for now. TODO: move to a local file/DB once the fleet grows past
    // what's comfortable in appsettings.json.
    public List<string> RemoteHosts { get; set; } = new();
}