namespace Daraban.Agent.Core.Config;

public sealed class AgentOptions
{
    // Target definition (like GLPI --server/--local)
    public string? Server { get; set; }   // send results to GLPI server (HTTP/HTTPS)
    public string? Local { get; set; } = "./out";    // write results locally to this directory

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

    // "Run once and exit" vs. the default "loop forever on DelayTimeSeconds" scheduler.
    public bool RunOnce { get; set; } = false;

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
    public bool EsxIgnoreSslErrors { get; set; } = true; // most ESXi hosts use self-signed certs
}