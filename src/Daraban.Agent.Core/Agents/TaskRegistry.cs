namespace Daraban.Agent.Core.Agents;

public static class TaskRegistry
{
    private static readonly Dictionary<string, IAgentTask> _tasks = new()
    {
        ["local"] = new LocalInventoryTask(),
        ["netdiscovery"] = new NetDiscoveryTask(),
        ["remote"] = new RemoteInventoryTask(),
        // ["netinventory"] = ...,
        // ["esx"] = ...,
        // ["wakeonlan"] = ...,
        // ["deploy"] = ...
    };

    public static IReadOnlyDictionary<string, IAgentTask> All => _tasks;

    public static IAgentTask? Find(string name)
        => _tasks.GetValueOrDefault(name);
}