namespace Daraban.Agent.Core.Config;

public sealed class AgentOptions
{
    // Target definition (like GLPI --server/--local)
    public string? Server { get; set; }   // send results to GLPI server (HTTP/HTTPS)
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

    // OAuth2 client-credentials configuration. ClientSecret must be supplied by a secret
    // provider/environment variable in production, never committed to appsettings.json.
    public string? OAuthTokenEndpoint { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthClientSecret { get; set; }
    public string? OAuthScope { get; set; } = "daraban.agent.inventory";

    // "Run once and exit" vs. the default "loop forever on DelayTimeSeconds" scheduler.
    public bool RunOnce { get; set; } = false;

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


    // ---- NetDiscovery / NetInventory ----------------------------------------
    public string? IpRange { get; set; }          // e.g. "192.168.1.0/24"
    public string SnmpCommunity { get; set; } = "public";
    public int SnmpTimeoutMs { get; set; } = 2000;
    public int DiscoveryThreads { get; set; } = 32;

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
}