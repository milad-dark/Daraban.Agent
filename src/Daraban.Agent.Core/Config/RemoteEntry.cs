namespace Daraban.Agent.Core.Config;

public sealed class RemoteEntry
{
    public string Url { get; set; } = "";            // e.g. "ssh://admin:****@192.168.1.10"
    public string? TargetAlias { get; set; }        // e.g. "server0" or "local0"
    public string? DeviceId { get; set; }
    public DateTime? NextRunUtc { get; set; }
}