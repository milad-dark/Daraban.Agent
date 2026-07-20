using Daraban.Agent.Core.Models;
using Microsoft.Win32;
using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Daraban.Agent.Core.Collectors;

[SupportedOSPlatform("windows")]
public class LocalWindowsCollector
{
    public DeviceInventory CollectLocal()
    {
        var content = new DeviceContent
        {
            ComputerName = Environment.MachineName,
            OperatingSystem = Environment.OSVersion.VersionString,
            OsArchitecture = Environment.Is64BitOperatingSystem ? "x86_64" : "x86"
        };

        // Collect all data
        CollectComputerSystem(content);
        CollectBios(content);
        CollectMotherboard(content);
        CollectCpus(content);
        CollectMemory(content);
        CollectStorage(content);
        CollectMonitors(content);
        CollectVideoControllers(content);
        CollectAudioDevices(content);
        CollectLocalUserAccounts(content);
        CollectLocalGroups(content);
        CollectServices(content);
        CollectDesktopSettings(content);
        CollectHotfixes(content);
        CollectProcesses(content);
        CollectPrinters(content);
        CollectInstalledSoftware(content);
        CollectBattery(content);

        return new DeviceInventory { Content = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }) };
    }

    private static void CollectComputerSystem(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, TotalPhysicalMemory, SystemType, PCSystemType, Domain, UserName, Workgroup FROM Win32_ComputerSystem");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.ComputerSystem = new ComputerSystemInfo
                {
                    Manufacturer = mo["Manufacturer"]?.ToString(),
                    Model = mo["Model"]?.ToString(),
                    TotalPhysicalMemory = mo["TotalPhysicalMemory"]?.ToString(),
                    SystemType = mo["SystemType"]?.ToString(),
                    PCSystemType = mo["PCSystemType"]?.ToString()
                };
                content.Domain = mo["Domain"]?.ToString() ?? "";
                content.Workgroup = mo["Workgroup"]?.ToString() ?? "";
                content.LoggedOnUser = mo["UserName"]?.ToString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting computer system info: {ex.Message}");
        }
    }

    private static void CollectBios(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.Bios = new BiosInfo
                {
                    Manufacturer = mo["Manufacturer"]?.ToString(),
                    Version = mo["SMBIOSBIOSVersion"]?.ToString(),
                    ReleaseDate = mo["ReleaseDate"]?.ToString(),
                    SerialNumber = mo["SerialNumber"]?.ToString()
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting BIOS info: {ex.Message}");
        }
    }

    private static void CollectMotherboard(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber, Version FROM Win32_BaseBoard");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.MotherboardModel = $"{mo["Manufacturer"]} {mo["Product"]}";
                content.MotherboardSerial = mo["SerialNumber"]?.ToString();
                content.MotherboardVersion = mo["Version"]?.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting motherboard info: {ex.Message}");
        }
    }

    private static void CollectCpus(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, ProcessorId, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors, Architecture FROM Win32_Processor");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.Cpus.Add(new CpuInfo
                {
                    Name = mo["Name"]?.ToString(),
                    ProcessorId = mo["ProcessorId"]?.ToString(),
                    Cores = mo["NumberOfCores"]?.ToString(),
                    LogicalProcessors = mo["NumberOfLogicalProcessors"]?.ToString(),
                    Speed = mo["MaxClockSpeed"]?.ToString(),
                    Architecture = mo["Architecture"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting CPU info: {ex.Message}");
        }
    }

    private static void CollectMemory(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            foreach (ManagementObject mo in searcher.Get())
            {
                if (long.TryParse(mo["Capacity"]?.ToString(), out long capacityBytes) && capacityBytes > 0)
                {
                    var memoryInfo = new MemoryInfo
                    {
                        BankLabel = mo["BankLabel"]?.ToString(),
                        Caption = mo["Caption"]?.ToString(),
                        CreationClassName = mo["CreationClassName"]?.ToString(),
                        Description = mo["Description"]?.ToString(),
                        DeviceLocator = mo["DeviceLocator"]?.ToString(),
                        FormFactor = mo["FormFactor"]?.ToString(),
                        Manufacturer = mo["Manufacturer"]?.ToString(),
                        MemoryType = mo["MemoryType"]?.ToString(),
                        Model = mo["Model"]?.ToString(),
                        Name = mo["Name"]?.ToString(),
                        PartNumber = mo["PartNumber"]?.ToString(),
                        PositionInRow = mo["PositionInRow"]?.ToString(),
                        SerialNumber = mo["SerialNumber"]?.ToString(),
                        Status = mo["Status"]?.ToString(),
                        Tag = mo["Tag"]?.ToString(),
                        Capacity = capacityBytes / 1024 / 1024, // Convert to MB
                        Speed = long.TryParse(mo["Speed"]?.ToString(), out var speed) ? speed : 0L,
                        Attributes = long.TryParse(mo["Attributes"]?.ToString(), out var attributes) ? attributes : 0L,
                        ConfiguredClockSpeed = long.TryParse(mo["ConfiguredClockSpeed"]?.ToString(), out var configSpeed) ? configSpeed : 0L,
                        DataWidth = long.TryParse(mo["DataWidth"]?.ToString(), out var dataWidth) ? dataWidth : 0L,
                        InterleavePosition = long.TryParse(mo["InterleavePosition"]?.ToString(), out var interleavePos) ? interleavePos : 0L,
                        InterleaveDataDepth = long.TryParse(mo["InterleaveDataDepth"]?.ToString(), out var interleaveDepth) ? interleaveDepth : 0L,
                        MaxVoltage = long.TryParse(mo["MaxVoltage"]?.ToString(), out var maxVolt) ? maxVolt : 0L,
                        MinVoltage = long.TryParse(mo["MinVoltage"]?.ToString(), out var minVolt) ? minVolt : 0L,
                        SmbiosMemoryType = long.TryParse(mo["SMBIOSMemoryType"]?.ToString(), out var smbiosType) ? smbiosType : 0L,
                        TypeDetail = long.TryParse(mo["TypeDetail"]?.ToString(), out var typeDetail) ? typeDetail : 0L
                    };

                    content.Memories.Add(memoryInfo);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting memory info: {ex.Message}");
        }
    }

    private static void CollectStorage(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, Size, InterfaceType, SerialNumber FROM Win32_DiskDrive");
            foreach (ManagementObject mo in searcher.Get())
            {
                long.TryParse(mo["Size"]?.ToString(), out long sizeBytes);
                content.Storages.Add(new StorageInfo
                {
                    Model = mo["Model"]?.ToString(),
                    Size = $"{sizeBytes / 1024 / 1024 / 1024} GB",
                    InterfaceType = mo["InterfaceType"]?.ToString(),
                    Serial = mo["SerialNumber"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting storage info: {ex.Message}");
        }
    }

    private static void CollectMonitors(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi",
             "SELECT InstanceName, Active, ManufacturerName, ProductCodeID, SerialNumberID, UserFriendlyName FROM WmiMonitorID");

            foreach (ManagementObject mo in searcher.Get())
            {
                if (mo["Active"] is bool active && !active)
                    continue;

                content.Monitors.Add(new MonitorInfo
                {
                    Name = GetMonitorString(mo["UserFriendlyName"]),
                    Manufacturer = GetMonitorString(mo["ManufacturerName"]),
                    Serial = GetMonitorString(mo["SerialNumberID"])
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting monitor info: {ex.Message}");
        }
    }
    // Helper method to convert WMI byte arrays to strings
    private static string? GetMonitorString(object value)
    {
        if (value is not ushort[] data)
            return null;

        return new string(Array.ConvertAll(data, item => (char)item))
            .TrimEnd('\0', ' ');
    }

    private static void CollectVideoControllers(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion, VideoProcessor FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.VideoControllers.Add(new VideoControllerInfo
                {
                    Name = mo["Name"]?.ToString(),
                    AdapterRAM = mo["AdapterRAM"]?.ToString(),
                    DriverVersion = mo["DriverVersion"]?.ToString(),
                    VideoProcessor = mo["VideoProcessor"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting video controller info: {ex.Message}");
        }
    }

    private static void CollectAudioDevices(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Status FROM Win32_SoundDevice");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.AudioDevices.Add(new AudioDevice
                {
                    Name = mo["Name"]?.ToString(),
                    Status = mo["Status"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting audio device info: {ex.Message}");
        }
    }

    private static void CollectLocalUserAccounts(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, FullName, Disabled, Lockout, SID FROM Win32_UserAccount WHERE LocalAccount = TRUE");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.LocalUserAccounts.Add(new UserAccountInfo
                {
                    Name = mo["Name"]?.ToString(),
                    FullName = mo["FullName"]?.ToString(),
                    Disabled = mo["Disabled"]?.ToString(),
                    Lockout = mo["Lockout"]?.ToString(),
                    SID = mo["SID"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting local user accounts: {ex.Message}");
        }
    }

    private static void CollectLocalGroups(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Description, SID FROM Win32_Group WHERE LocalAccount = TRUE");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.LocalGroups.Add(new GroupInfo
                {
                    Name = mo["Name"]?.ToString(),
                    Description = mo["Description"]?.ToString(),
                    SID = mo["SID"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting local groups: {ex.Message}");
        }
    }

    private static void CollectServices(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, State, StartMode, PathName FROM Win32_Service");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.Services.Add(new ServiceInfo
                {
                    Name = mo["Name"]?.ToString(),
                    DisplayName = mo["DisplayName"]?.ToString(),
                    State = mo["State"]?.ToString(),
                    StartMode = mo["StartMode"]?.ToString(),
                    PathName = mo["PathName"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting services: {ex.Message}");
        }
    }

    private static void CollectDesktopSettings(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, ScreenSaverActive, ScreenSaverSecure, ScreenSaverTimeout, Wallpaper FROM Win32_Desktop");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.Desktops.Add(new DesktopInfo
                {
                    Name = mo["Name"]?.ToString(),
                    ScreenSaverActive = mo["ScreenSaverActive"]?.ToString(),
                    ScreenSaverSecure = mo["ScreenSaverSecure"]?.ToString(),
                    ScreenSaverTimeout = mo["ScreenSaverTimeout"]?.ToString(),
                    Wallpaper = mo["Wallpaper"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting desktop settings: {ex.Message}");
        }
    }

    private static void CollectHotfixes(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT HotFixID, Description, InstalledBy, InstalledOn FROM Win32_QuickFixEngineering");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.Hotfixes.Add(new HotfixInfo
                {
                    HotFixID = mo["HotFixID"]?.ToString(),
                    Description = mo["Description"]?.ToString(),
                    InstalledBy = mo["InstalledBy"]?.ToString(),
                    InstalledOn = mo["InstalledOn"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting hotfixes: {ex.Message}");
        }
    }

    private static void CollectProcesses(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, ProcessId, ThreadCount, WorkingSetSize, CommandLine FROM Win32_Process");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.Processes.Add(new ProcessInfo
                {
                    Name = mo["Name"]?.ToString(),
                    ProcessId = mo["ProcessId"]?.ToString(),
                    ThreadCount = mo["ThreadCount"]?.ToString(),
                    WorkingSetSize = mo["WorkingSetSize"]?.ToString(),
                    CommandLine = mo["CommandLine"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting processes: {ex.Message}");
        }
    }

    private static void CollectPrinters(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Default, PortName, DriverName, PrinterStatus, Shared FROM Win32_Printer");
            foreach (ManagementObject mo in searcher.Get())
            {
                content.Printers.Add(new PrinterInfo
                {
                    Name = mo["Name"]?.ToString(),
                    Default = mo["Default"]?.ToString(),
                    PortName = mo["PortName"]?.ToString(),
                    DriverName = mo["DriverName"]?.ToString(),
                    PrinterStatus = mo["PrinterStatus"]?.ToString(),
                    Shared = mo["Shared"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting printers: {ex.Message}");
        }
    }

    private static void CollectInstalledSoftware(DeviceContent content)
    {
        try
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                    continue;
                foreach (var keyName in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(keyName);
                    var name = key?.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    content.Software.Add(new SoftwareInfo
                    {
                        Name = name,
                        Version = key?.GetValue("DisplayVersion")?.ToString(),
                        IdentifyingNumber = keyName,
                        Vendor = key?.GetValue("Publisher")?.ToString(),
                    });
                }
            }

            //using var searcher = new ManagementObjectSearcher("SELECT Name, Version, IdentifyingNumber, Caption, Vendor FROM Win32_Product");
            //foreach (ManagementObject mo in searcher.Get())
            //{
            //    var name = mo["Name"]?.ToString();
            //    if (string.IsNullOrWhiteSpace(name))
            //        continue;

            //    content.Software.Add(new SoftwareInfo
            //    {
            //        Name = name,
            //        Version = mo["Version"]?.ToString(),
            //        IdentifyingNumber = mo["IdentifyingNumber"]?.ToString(),
            //        Caption = mo["Caption"]?.ToString(),
            //        Vendor = mo["Vendor"]?.ToString()
            //    });
            //}
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting installed software: {ex.Message}");
        }
    }

    private static void CollectBattery(DeviceContent content)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                content.Batteries.Add(new BatteryInfo
                {
                    Name = name,
                    EstimatedChargeRemaining = mo["EstimatedChargeRemaining"]?.ToString(),
                    BatteryStatus = mo["BatteryStatus"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting installed software: {ex.Message}");
        }
    }



    // Keep the old static methods for reference/debugging
    static void WmiQuery(string query, string[] properties, string description)
    {
        Console.WriteLine($"\n===== {description} =====");
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject obj in searcher.Get())
            {
                foreach (var prop in properties)
                {
                    Console.WriteLine($"  {prop}: {obj[prop]}");
                }
                Console.WriteLine("  ---");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
        }
    }

    public static void GetAccountsAndDomains()
    {
        WmiQuery("SELECT Name, Domain, UserName, Workgroup FROM Win32_ComputerSystem",
            new[] { "Name", "Domain", "UserName", "Workgroup" },
            "Computer Domain Info");

        WmiQuery("SELECT Name, FullName, Disabled, Lockout, SID FROM Win32_UserAccount WHERE LocalAccount = TRUE",
            new[] { "Name", "FullName", "Disabled", "Lockout", "SID" },
            "Local User Accounts");

        WmiQuery("SELECT Name, Description, SID FROM Win32_Group WHERE LocalAccount = TRUE",
            new[] { "Name", "Description", "SID" },
            "Local Groups");

        WmiQuery("SELECT * FROM Win32_LogonSession WHERE LogonType = 2",
            new[] { "LogonId", "LogonType", "StartTime", "AuthenticationPackage" },
            "Interactive Logon Sessions");
    }

    public static void GetComputerHardware()
    {
        WmiQuery("SELECT Manufacturer, Model, TotalPhysicalMemory, SystemType, PCSystemType FROM Win32_ComputerSystem",
            new[] { "Manufacturer", "Model", "TotalPhysicalMemory", "SystemType", "PCSystemType" },
            "Computer System (PCSystemType: 1=Desktop, 2=Laptop)");

        WmiQuery("SELECT Name, ProcessorId, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors, Architecture FROM Win32_Processor",
            new[] { "Name", "ProcessorId", "MaxClockSpeed", "NumberOfCores", "NumberOfLogicalProcessors" },
            "Processor");

        WmiQuery("SELECT BankLabel, Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory",
            new[] { "BankLabel", "Capacity", "Speed", "Manufacturer", "PartNumber" },
            "Physical Memory");

        WmiQuery("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS",
            new[] { "Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate", "SerialNumber" },
            "BIOS");

        WmiQuery("SELECT Manufacturer, Product, SerialNumber, Version FROM Win32_BaseBoard",
            new[] { "Manufacturer", "Product", "SerialNumber", "Version" },
            "Baseboard (Motherboard)");

        WmiQuery("SELECT Name, AdapterRAM, DriverVersion, VideoProcessor FROM Win32_VideoController",
            new[] { "Name", "AdapterRAM", "DriverVersion", "VideoProcessor" },
            "Video Controller");

        WmiQuery("SELECT Name, Manufacturer, Status FROM Win32_SoundDevice",
            new[] { "Name", "Manufacturer", "Status" },
            "Sound Devices");

        WmiQuery("SELECT Name, EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery",
            new[] { "Name", "EstimatedChargeRemaining", "BatteryStatus" },
            "Battery");
    }


}