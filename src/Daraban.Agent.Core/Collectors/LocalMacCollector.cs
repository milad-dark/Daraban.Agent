using Daraban.Agent.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Local inventory collector for macOS hosts.
/// Uses system_profiler (in -json mode, where available), sysctl, sw_vers and
/// standard BSD/macOS CLI tools, and maps everything onto the same
/// DeviceContent shape used by LocalWindowsCollector / LocalLinuxCollector.
/// </summary>
[SupportedOSPlatform("macos")]
public class LocalMacCollector
{
    public DeviceInventory CollectLocal()
    {
        var content = new DeviceContent
        {
            ComputerName = Environment.MachineName,
            OperatingSystem = ReadOsVersion(),
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString()
        };

        CollectHardwareOverview(content);   // system_profiler SPHardwareDataType
        CollectCpu(content);                 // sysctl machdep.cpu.*
        CollectMemory(content);              // system_profiler SPMemoryDataType
        CollectStorage(content);             // diskutil list + info
        CollectNetwork(content);             // NetworkInterface (cross-platform BCL API)
        CollectDisplays(content);            // system_profiler SPDisplaysDataType
        CollectUsers(content);               // dscl
        CollectServices(content);            // launchctl
        CollectProcesses(content);           // Process.GetProcesses (cross-platform)
        CollectInstalledSoftware(content);   // system_profiler SPApplicationsDataType
        CollectBattery(content);             // pmset -g batt / ioreg

        return new DeviceInventory
        {
            DeviceId = content.ComputerName,
            Content = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true })
        };
    }

    // ---------- helpers ---------------------------------------------------

    private static string RunCommand(string cmd, string args, int timeoutMs = 8000)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(timeoutMs))
            {
                p.Kill(true);
                return string.Empty;
            }
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static JsonElement? RunJson(string cmd, string args, string rootProperty)
    {
        var raw = RunCommand(cmd, args);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty(rootProperty, out var el))
                return el.Clone();
        }
        catch { /* system_profiler occasionally emits partial JSON if a subsystem hangs */ }
        return null;
    }

    private static string ReadOsVersion()
    {
        var product = RunCommand("sw_vers", "-productName").Trim();
        var version = RunCommand("sw_vers", "-productVersion").Trim();
        var build = RunCommand("sw_vers", "-buildVersion").Trim();
        return string.IsNullOrWhiteSpace(product) ? RuntimeInformation.OSDescription : $"{product} {version} ({build})";
    }

    // ---------- Hardware overview (Model/Manufacturer/BIOS-equivalent) ---------------

    private static void CollectHardwareOverview(DeviceContent content)
    {
        try
        {
            var hw = RunJson("system_profiler", "SPHardwareDataType -json", "SPHardwareDataType");
            if (hw is { } el && el.GetArrayLength() > 0)
            {
                var item = el[0];
                string? Get(string name) => item.TryGetProperty(name, out var v) ? v.GetString() : null;

                content.ComputerSystem = new ComputerSystemInfo
                {
                    Manufacturer = "Apple Inc.",
                    Model = Get("machine_model") ?? Get("machine_name"),
                    SystemType = RuntimeInformation.OSArchitecture.ToString()
                };

                // macOS has no traditional BIOS; boot ROM / SMC version is the closest analogue.
                content.Bios = new BiosInfo
                {
                    Manufacturer = "Apple Inc.",
                    Version = Get("boot_rom_version") ?? Get("SMC_version_system"),
                    SerialNumber = Get("serial_number")
                };

                content.MotherboardModel = Get("machine_model");
                content.MotherboardSerial = Get("serial_number");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting hardware overview: {ex.Message}");
        }
    }

    // ---------- CPU ---------------------------------------------------------------------

    private static void CollectCpu(DeviceContent content)
    {
        try
        {
            var brand = RunCommand("sysctl", "-n machdep.cpu.brand_string").Trim();
            var physicalCores = RunCommand("sysctl", "-n hw.physicalcpu").Trim();
            var logicalCores = RunCommand("sysctl", "-n hw.logicalcpu").Trim();
            var freqHz = RunCommand("sysctl", "-n hw.cpufrequency_max").Trim(); // absent on Apple Silicon

            content.Cpus.Add(new CpuInfo
            {
                Name = brand,
                Cores = physicalCores,
                LogicalProcessors = logicalCores,
                Speed = long.TryParse(freqHz, out var hz) && hz > 0 ? (hz / 1_000_000).ToString() : "N/A (Apple Silicon reports no fixed clock)",
                Architecture = RuntimeInformation.OSArchitecture.ToString()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting CPU info: {ex.Message}");
        }
    }

    // ---------- Memory -----------------------------------------------------------------

    private static void CollectMemory(DeviceContent content)
    {
        try
        {
            var mem = RunJson("system_profiler", "SPMemoryDataType -json", "SPMemoryDataType");
            bool addedDetail = false;

            if (mem is { } el)
            {
                foreach (var item in el.EnumerateArray())
                {
                    if (!item.TryGetProperty("_items", out var dimms))
                        continue;
                    foreach (var dimm in dimms.EnumerateArray())
                    {
                        string? Get(string name) => dimm.TryGetProperty(name, out var v) ? v.GetString() : null;
                        var sizeStr = Get("dimm_size");
                        if (string.IsNullOrWhiteSpace(sizeStr) || sizeStr.Contains("Empty", StringComparison.OrdinalIgnoreCase))
                            continue;

                        content.Memories.Add(new MemoryInfo
                        {
                            DeviceLocator = Get("_name"),
                            MemoryType = Get("dimm_type"),
                            Speed = ParseLongPrefix(Get("dimm_speed")),
                            Manufacturer = Get("dimm_manufacturer"),
                            SerialNumber = Get("dimm_serial_number"),
                            Caption = sizeStr,
                            Capacity = ParseMemorySizeToMb(sizeStr)
                        });
                        addedDetail = true;
                    }
                }
            }

            if (!addedDetail)
            {
                // Apple Silicon Macs report unified memory as one non-removable block.
                var totalBytes = RunCommand("sysctl", "-n hw.memsize").Trim();
                if (long.TryParse(totalBytes, out var bytes))
                {
                    var mb = bytes / 1024 / 1024;
                    content.Memories.Add(new MemoryInfo
                    {
                        Caption = $"{mb} MB",
                        Capacity = mb,
                        Description = "Unified memory (soldered, non-removable)"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting memory info: {ex.Message}");
        }
    }

    private static long ParseLongPrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        var m = Regex.Match(value, @"\d+");
        return m.Success ? long.Parse(m.Value) : 0;
    }

    private static long ParseMemorySizeToMb(string size)
    {
        var m = Regex.Match(size, @"(?<num>\d+)\s*(?<unit>MB|GB)");
        if (!m.Success)
            return 0;
        var num = long.Parse(m.Groups["num"].Value);
        return m.Groups["unit"].Value == "GB" ? num * 1024 : num;
    }

    // ---------- Storage ------------------------------------------------------------------

    private static void CollectStorage(DeviceContent content)
    {
        try
        {
            var store = RunJson("system_profiler", "SPStorageDataType -json", "SPStorageDataType");
            if (store is { } el)
            {
                foreach (var item in el.EnumerateArray())
                {
                    string? Get(string name) => item.TryGetProperty(name, out var v) ? v.GetString() : null;
                    long GetLong(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

                    var sizeBytes = GetLong("size_in_bytes");
                    content.Storages.Add(new StorageInfo
                    {
                        Model = Get("_name"),
                        Size = sizeBytes > 0 ? $"{sizeBytes / 1024 / 1024 / 1024} GB" : null,
                        InterfaceType = Get("physical_drive")?.Contains("SSD", StringComparison.OrdinalIgnoreCase) == true ? "SSD" : "HDD",
                        Serial = null // not exposed by SPStorageDataType; requires diskutil info per-disk with elevated entitlements
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting storage info: {ex.Message}");
        }
    }

    // ---------- Network -------------------------------------------------------------------

    private static void CollectNetwork(DeviceContent content)
    {
        try
        {
            foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = iface.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?.Address.ToString();

                content.Networks.Add(new NetworkInterfaceInfo
                {
                    Description = iface.Description is { Length: > 0 } d ? d : iface.Name,
                    MACAddress = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                    IPAddress = ipv4,
                    Status = iface.OperationalStatus.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting network info: {ex.Message}");
        }
    }

    // ---------- Displays (maps onto Monitors + VideoControllers) --------------------------

    private static void CollectDisplays(DeviceContent content)
    {
        try
        {
            var gpu = RunJson("system_profiler", "SPDisplaysDataType -json", "SPDisplaysDataType");
            if (gpu is not { } el)
                return;

            foreach (var card in el.EnumerateArray())
            {
                string? Get(string name) => card.TryGetProperty(name, out var v) ? v.GetString() : null;

                content.VideoControllers.Add(new VideoControllerInfo
                {
                    Name = Get("sppci_model") ?? Get("_name"),
                    VideoProcessor = Get("sppci_model")
                });

                if (card.TryGetProperty("spdisplays_ndrvs", out var monitors))
                {
                    foreach (var mon in monitors.EnumerateArray())
                    {
                        content.Monitors.Add(new MonitorInfo
                        {
                            Name = mon.TryGetProperty("_name", out var n) ? n.GetString() : null
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting display info: {ex.Message}");
        }
    }

    // ---------- Users --------------------------------------------------------------------

    private static void CollectUsers(DeviceContent content)
    {
        try
        {
            var users = RunCommand("dscl", ". -list /Users UniqueID");
            foreach (var line in users.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;
                if (!int.TryParse(parts[^1], out var uid) || uid < 500)
                    continue; // skip system accounts (_www, daemon, etc.)

                content.LocalUserAccounts.Add(new UserAccountInfo
                {
                    Name = parts[0],
                    SID = uid.ToString()
                });
            }

            var groups = RunCommand("dscl", ". -list /Groups PrimaryGroupID");
            foreach (var line in groups.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                content.LocalGroups.Add(new GroupInfo
                {
                    Name = parts[0],
                    SID = parts[^1]
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting users/groups: {ex.Message}");
        }
    }

    // ---------- Services (launchd) ---------------------------------------------------------

    private static void CollectServices(DeviceContent content)
    {
        try
        {
            var output = RunCommand("launchctl", "list");
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length < 3)
                    continue;

                content.Services.Add(new ServiceInfo
                {
                    Name = parts[2],
                    State = parts[0] == "-" ? "Stopped" : "Running",
                    PathName = parts[2]
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting services: {ex.Message}");
        }
    }

    // ---------- Processes --------------------------------------------------------------------

    private static void CollectProcesses(DeviceContent content)
    {
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    content.Processes.Add(new ProcessInfo
                    {
                        Name = proc.ProcessName,
                        ProcessId = proc.Id.ToString(),
                        WorkingSetSize = proc.WorkingSet64.ToString()
                    });
                }
                catch
                {
                    // Sandboxed / SIP-protected processes may refuse enumeration — skip.
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting processes: {ex.Message}");
        }
    }

    // ---------- Installed software (Applications folder via system_profiler) ------------------

    private static void CollectInstalledSoftware(DeviceContent content)
    {
        try
        {
            var apps = RunJson("system_profiler", "SPApplicationsDataType -json", "SPApplicationsDataType");
            if (apps is not { } el)
                return;

            foreach (var app in el.EnumerateArray())
            {
                string? Get(string name) => app.TryGetProperty(name, out var v) ? v.GetString() : null;
                var name = Get("_name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                content.Software.Add(new SoftwareInfo
                {
                    Name = name,
                    Version = Get("version"),
                    Vendor = Get("obtained_from") // "apple", "identified_developer", "unknown" — not a real vendor string but the closest field
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting installed software: {ex.Message}");
        }
    }

    // ---------- Battery ------------------------------------------------------------------------

    private static void CollectBattery(DeviceContent content)
    {
        try
        {
            var battery = RunJson("system_profiler", "SPPowerDataType -json", "SPPowerDataType");
            if (battery is not { } el)
                return;

            foreach (var item in el.EnumerateArray())
            {
                if (!item.TryGetProperty("sppower_battery_health_info", out _) &&
                    !item.TryGetProperty("sppower_battery_charge_info", out _))
                    continue;

                string? chargeInfoStr = null;
                if (item.TryGetProperty("sppower_battery_charge_info", out var charge) &&
                    charge.TryGetProperty("sppower_battery_state_of_charge", out var soc))
                {
                    chargeInfoStr = soc.GetRawText().Trim('"');
                }

                content.Batteries.Add(new BatteryInfo
                {
                    Name = "Internal Battery",
                    EstimatedChargeRemaining = chargeInfoStr,
                    BatteryStatus = item.TryGetProperty("sppower_battery_charge_info", out var c2) &&
                                     c2.TryGetProperty("sppower_battery_is_charging", out var charging)
                        ? charging.GetRawText().Trim('"')
                        : null
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting battery info: {ex.Message}");
        }
    }
}
