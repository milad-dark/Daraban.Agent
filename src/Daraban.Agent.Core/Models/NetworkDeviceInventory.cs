namespace Daraban.Agent.Core.Models;

/// <summary>Full SNMP-derived record for one network device, as produced by NetInventoryTask.</summary>
public sealed record NetworkDeviceInventory
{
    public string IpAddress { get; set; } = "";
    public string? Community { get; set; }
    public bool Reachable { get; set; }
    public DeviceInventory? Inventory { get; set; }
    public string? Error { get; set; }
}
