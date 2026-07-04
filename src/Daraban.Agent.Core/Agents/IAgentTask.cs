using Daraban.Agent.Core.Config;

namespace Daraban.Agent.Core.Agents;

public interface IAgentTask
{
    string Name { get; }          // e.g. "local", "netdiscovery", "netinventory", "remote"
    Task RunAsync(AgentOptions options, CancellationToken ct);
}
