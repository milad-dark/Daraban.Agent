namespace Daraban.Agent.Core.Models;

// ---------------------------------------------------------------------------
// ESX / vCenter remote inventory
// ---------------------------------------------------------------------------

public sealed record EsxHostInfo
{
    public string Name { get; set; } = "";
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? CpuModel { get; set; }
    public int CpuCores { get; set; }
    public long MemoryMb { get; set; }
    public string? BiosVersion { get; set; }
    public string? Version { get; set; }      // ESXi build/version
    public List<EsxVmInfo> VirtualMachines { get; set; } = [];
}

public sealed record EsxVmInfo
{
    public string Name { get; set; } = "";
    public string? Uuid { get; set; }
    public string? GuestOs { get; set; }
    public int CpuCount { get; set; }
    public long MemoryMb { get; set; }
    public string? PowerState { get; set; }
    public List<string> IpAddresses { get; set; } = [];
    public List<long> DiskSizesMb { get; set; } = [];
}
