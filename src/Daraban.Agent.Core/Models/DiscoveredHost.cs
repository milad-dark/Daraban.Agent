namespace Daraban.Agent.Core.Models;

/// <summary>A single host found during the NetDiscovery sweep (ping/ARP), before SNMP detail is added.</summary>
public sealed record DiscoveredHost
{
    // ── existing fields — unchanged ───────────────────────────────────────────
    public string IpAddress { get; set; } = "";
    public string? MacAddress { get; set; }
    public string? Hostname { get; set; }
    public bool Responded { get; set; }
    public long RoundtripMs { get; set; }

    // ── NEW: SNMP fingerprint fields ──────────────────────────────────────────
    public bool SnmpReachable { get; set; }
    public string? SysDescr { get; set; }       // sysDescr.0  — e.g. "Cisco IOS Software..."
    public string? SysObjectId { get; set; }    // sysObjectID.0 — e.g. "1.3.6.1.4.1.9.1.1"
    public string? DeviceType { get; set; }     // fingerprinted: "NetworkDevice","Printer", etc.
}