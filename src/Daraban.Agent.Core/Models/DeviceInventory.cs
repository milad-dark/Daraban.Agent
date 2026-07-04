namespace Daraban.Agent.Core.Models;

public class DeviceInventory
{
    public string DeviceId { get; set; } = Environment.MachineName;
    public string Action { get; set; } = "inventory";
    public string Content { get; set; } = ""; // Serialized JSON of DeviceContent
}

public class DeviceContent
{
    // System Information
    public string ComputerName { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string OsArchitecture { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Workgroup { get; set; } = "";
    public string LoggedOnUser { get; set; } = "";

    // Hardware
    public ComputerSystemInfo ComputerSystem { get; set; } = new();
    public BiosInfo Bios { get; set; } = new();
    public string MotherboardSerial { get; set; } = "";
    public string MotherboardModel { get; set; } = "";
    public string MotherboardVersion { get; set; } = "";

    public List<CpuInfo> Cpus { get; set; } = [];
    public List<MemoryInfo> Memories { get; set; } = [];
    public List<StorageInfo> Storages { get; set; } = [];
    public List<NetworkInterfaceInfo> Networks { get; set; } = [];
    public List<MonitorInfo> Monitors { get; set; } = [];
    public List<AudioDevice> AudioDevices { get; set; } = [];
    public List<VideoControllerInfo> VideoControllers { get; set; } = [];

    // User & Security
    public List<UserAccountInfo> LocalUserAccounts { get; set; } = [];
    public List<GroupInfo> LocalGroups { get; set; } = [];
    public List<ServiceInfo> Services { get; set; } = [];

    // System Info
    public List<DesktopInfo> Desktops { get; set; } = [];
    public List<HotfixInfo> Hotfixes { get; set; } = [];
    public List<ProcessInfo> Processes { get; set; } = [];
    public List<PrinterInfo> Printers { get; set; } = [];

    // Software
    public List<SoftwareInfo> Software { get; set; } = [];

    public List<BatteryInfo> Batteries { get; set; } = [];

}

public class ComputerSystemInfo
{
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? TotalPhysicalMemory { get; set; }
    public string? SystemType { get; set; }
    public string? PCSystemType { get; set; } // 1=Desktop, 2=Laptop
}

public class BiosInfo
{
    public string? Manufacturer { get; set; }
    public string? Version { get; set; }
    public string? ReleaseDate { get; set; }
    public string? SerialNumber { get; set; }
}

public class CpuInfo
{
    public string? Name { get; set; }
    public string? ProcessorId { get; set; }
    public string? Cores { get; set; }
    public string? LogicalProcessors { get; set; }
    public string? Speed { get; set; }
    public string? Architecture { get; set; }
}

public partial class MemoryInfo
{
    public long Genus { get; set; }
    public string? Class { get; set; }
    public string? Superclass { get; set; }
    public string? Dynasty { get; set; }
    public string? Relpath { get; set; }
    public long PropertyCount { get; set; }
    public Derivation Derivation { get; set; }
    public string? Server { get; set; }
    public string? Namespace { get; set; }
    public string? Path { get; set; }
    public long Attributes { get; set; }
    public string? BankLabel { get; set; }
    public long Capacity { get; set; }
    public string? Caption { get; set; }
    public long ConfiguredClockSpeed { get; set; }
    public long ConfiguredVoltage { get; set; }
    public string? CreationClassName { get; set; }
    public long DataWidth { get; set; }
    public string? Description { get; set; }
    public string? DeviceLocator { get; set; }
    public string? FormFactor { get; set; }
    public object? HotSwappable { get; set; }
    public object? InstallDate { get; set; }
    public long InterleaveDataDepth { get; set; }
    public long InterleavePosition { get; set; }
    public string? Manufacturer { get; set; }
    public long MaxVoltage { get; set; } = 0;
    public string? MemoryType { get; set; }
    public long MinVoltage { get; set; } = 0;
    public object? Model { get; set; }
    public string? Name { get; set; }
    public object? OtherIdentifyingInfo { get; set; }
    public string? PartNumber { get; set; }
    public object? PositionInRow { get; set; }
    public object? PoweredOn { get; set; }
    public object? Removable { get; set; }
    public object? Replaceable { get; set; }
    public string? SerialNumber { get; set; }
    public object? Sku { get; set; }
    public long SmbiosMemoryType { get; set; } = 0;
    public long Speed { get; set; } = 0;
    public object? Status { get; set; }
    public string? Tag { get; set; }
    public long TotalWidth { get; set; } = 0;
    public long TypeDetail { get; set; } = 0;
    public object? Version { get; set; }
    public string? PsComputerName { get; set; }
}

public partial class Derivation
{
    public object CimPhysicalMemory { get; set; }
    public object CimChip { get; set; }
    public object CimPhysicalComponent { get; set; }
    public object CimPhysicalElement { get; set; }
}

public class StorageInfo
{
    public string? Model { get; set; }
    public string? Size { get; set; }
    public string? InterfaceType { get; set; }
    public string? Serial { get; set; }
}

public class NetworkInterfaceInfo
{
    public string? Description { get; set; }
    public string? MACAddress { get; set; }
    public string? IPAddress { get; set; }
    public string? Status { get; set; }
}

public class MonitorInfo
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? Serial { get; set; }
}

public class AudioDevice
{
    public string? Name { get; set; }
    public string? Status { get; set; }
}

public class VideoControllerInfo
{
    public string? Name { get; set; }
    public string? AdapterRAM { get; set; }
    public string? DriverVersion { get; set; }
    public string? VideoProcessor { get; set; }
}

public class UserAccountInfo
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public string? Disabled { get; set; }
    public string? Lockout { get; set; }
    public string? SID { get; set; }
}

public class GroupInfo
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? SID { get; set; }
}

public class ServiceInfo
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? State { get; set; }
    public string? StartMode { get; set; }
    public string? PathName { get; set; }
}

public class HotfixInfo
{
    public string? HotFixID { get; set; }
    public string? Description { get; set; }
    public string? InstalledOn { get; set; }
    public string? InstalledBy { get; set; }
}

public class ProcessInfo
{
    public string? Name { get; set; }
    public string? ProcessId { get; set; }
    public string? ThreadCount { get; set; }
    public string? WorkingSetSize { get; set; }
    public string? CommandLine { get; set; }
}

public class PrinterInfo
{
    public string? Name { get; set; }
    public string? Default { get; set; }
    public string? PortName { get; set; }
    public string? DriverName { get; set; }
    public string? PrinterStatus { get; set; }
    public string? Shared { get; set; }
}

public class DesktopInfo
{
    public string? Name { get; set; }
    public string? ScreenSaverActive { get; set; }
    public string? ScreenSaverSecure { get; set; }
    public string? ScreenSaverTimeout { get; set; }
    public string? Wallpaper { get; set; }
}

public class SoftwareInfo
{
    public string? IdentifyingNumber { get; set; }
    public string? Name { get; set; }
    public string? Vendor { get; set; }
    public string? Version { get; set; }
    public string? Caption { get; set; }
}

public class BatteryInfo
{
    public string? Name { get; set; }
    public string? EstimatedChargeRemaining { get; set; }
    public string? BatteryStatus { get; set; }
}