namespace Daraban.Agent.Core.Models;

public sealed class CollectResult
{
    public string JobId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Value { get; init; }
    public string? Error { get; init; }
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;
}