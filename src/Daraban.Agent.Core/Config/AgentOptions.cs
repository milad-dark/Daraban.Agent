namespace Daraban.Agent.Core.Config;

public sealed class AgentOptions
{
    // Target definition (like GLPI --server/--local)【turn0search1】
    public string? Server { get; set; }   // send results to GLPI server (HTTP/HTTPS)
    public string? Local { get; set; }    // write results locally to this directory

    // Scheduling (mirrors --delaytime and --lazy)【turn0search1】
    public int DelayTimeSeconds { get; set; } = 3600;
    public bool Lazy { get; set; } = false;

    // Task selection (mirrors --tasks/--no-task/--list-tasks)【turn0search1】
    public List<string> Tasks { get; set; } = new();   // e.g. ["local", "netdiscovery", "netinventory", "remote"]
    public List<string> NoTasks { get; set; } = new();

    // HTTP interface (mirrors httpd-port, httpd-trust)【turn0search11】【turn0search10】
    public int HttpPort { get; set; } = 62354;
    public string? HttpTrust { get; set; }  // e.g. "192.168.1.0/24"
    public bool NoHttpd { get; set; } = false;

    // Optional: tag / machine id (helps the server identify the agent)
    public string? Tag { get; set; }
}