// Models/CollectResultsPayload.cs
namespace Daraban.Agent.Core.Models;

/// <summary>
/// Wire format for POST /api/agent/collect/results.
/// Wraps the result list with agent identity and timestamp
/// so the server can correlate results without needing a session.
/// </summary>
internal sealed class CollectResultsPayload
{
    public string AgentId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public IList<CollectResult> Results { get; init; } = [];
}