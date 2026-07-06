using System.Text;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Backs the /status HTTP endpoint with real state instead of the previous hardcoded
/// "waiting" string. One instance is shared between the scheduler and the status server
/// (singleton in the Service's DI container; a plain shared instance in the CLI).
/// </summary>
public sealed class AgentStatusTracker
{
    private readonly object _lock = new();
    private string _current = "waiting";
    private readonly Dictionary<string, (DateTime LastRunUtc, bool Success, string? Message)> _history = new();

    public void SetRunning(string taskName)
    {
        lock (_lock)
            _current = $"running task {taskName}";
    }

    public void SetIdle()
    {
        lock (_lock)
            _current = "waiting";
    }

    public void RecordResult(string taskName, bool success, string? message = null)
    {
        lock (_lock)
            _history[taskName] = (DateTime.UtcNow, success, message);
    }

    public string ToStatusText()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"status: {_current}");
            foreach (var (name, entry) in _history.OrderBy(kv => kv.Key))
            {
                var outcome = entry.Success ? "ok" : $"error: {entry.Message}";
                sb.AppendLine($"{name}: last run {entry.LastRunUtc:u} -> {outcome}");
            }
            return sb.ToString();
        }
    }
}
