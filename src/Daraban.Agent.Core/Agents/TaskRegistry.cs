namespace Daraban.Agent.Core.Agents;

public static class TaskRegistry
{
    public static IReadOnlyList<IAgentTask> All { get; } = new List<IAgentTask>
    {
        new LocalInventoryTask(),     // local (Windows/Linux/macOS) computer inventory
        new NetDiscoveryTask(),       // ICMP/ARP sweep over --ip-range
        new NetInventoryTask(),       // SNMP inventory over the same range
        new RemoteInventoryTask(),    // SSH/WinRM agentless inventory of a remote host
        new WakeOnLanTask(),          // magic-packet wake-up for configured MACs
        new DeployTask(),             // pulls jobs from the server, installs, reports status
        new EsxInventoryTask(),       // vCenter/ESXi host + VM inventory
        new CollectTask(),
    };

    public static IAgentTask? Find(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}