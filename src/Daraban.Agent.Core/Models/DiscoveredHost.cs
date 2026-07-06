namespace Daraban.Agent.Core.Models;

/// <summary>A single host found during the NetDiscovery sweep (ping/ARP), before SNMP detail is added.</summary>
public sealed record DiscoveredHost
{
    public string IpAddress { get; set; } = "";
    public string? MacAddress { get; set; }
    public string? Hostname { get; set; }
    public bool Responded { get; set; }
    public long RoundtripMs { get; set; }
}
