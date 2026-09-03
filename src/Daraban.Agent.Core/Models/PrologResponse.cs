namespace Daraban.Agent.Core.Models;

/// <summary>
/// Parsed prolog response from the control server. Mirrors the fields glpi-agent ships
/// back in its prolog: which tasks to run, parallel workers, remote inventory targets,
/// server config overrides, the server-provided schedule, and the detected GLPI version.
/// </summary>
public sealed class PrologResponse
{
    /// <summary>Tasks the server wants this agent to run, in order. Null = keep local schedule.</summary>
    public List<string>? Tasks { get; set; }

    /// <summary>Maximum parallel workers the server allows for concurrent jobs.</summary>
    public int Workers { get; set; }

    /// <summary>Server timestamp for when this agent was first known (ISO-8601).</summary>
    public string? ServedSince { get; set; }

    /// <summary>Remote inventory targets the server wants this agent to inventory.</summary>
    public List<RemoteTarget> Remote { get; set; } = new();

    /// <summary>Raw server-side config overrides (informational for now).</summary>
    public string? Config { get; set; }

    /// <summary>Server-provided scheduling interval, in seconds. 0 = use local delay.</summary>
    public int DelayTime { get; set; }

    /// <summary>Server asks the agent to force-run even if not required.</summary>
    public bool Force { get; set; }

    /// <summary>GLPI server version, used to gate version-dependent features.</summary>
    public string? GlpiVersion { get; set; }
}

/// <summary>
/// A remote inventory target as defined by the server. Server-side "remote" entries can
/// carry an optional mode (ssh/winrm); when absent the agent derives it from the URL scheme.
/// </summary>
public sealed class RemoteTarget
{
    public string Url { get; set; } = "";
    public string? Mode { get; set; }

    /// <summary>Renders the target back to the connection-string format RemoteHostSpec.Parse expects.</summary>
    public string ToConnectionString() => Url;
}