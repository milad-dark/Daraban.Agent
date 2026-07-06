namespace Daraban.Agent.Core.Models;

// ---------------------------------------------------------------------------
// WakeOnLan
// ---------------------------------------------------------------------------

public sealed record WakeOnLanTarget
{
    public string MacAddress { get; set; } = "";   // "AA:BB:CC:DD:EE:FF" or "AA-BB-CC-DD-EE-FF"
    public string? BroadcastAddress { get; set; }   // defaults to 255.255.255.255 if not set
    public int Port { get; set; } = 9;
}

public sealed record WakeOnLanResult
{
    public string MacAddress { get; set; } = "";
    public bool Sent { get; set; }
    public string? Error { get; set; }
}

