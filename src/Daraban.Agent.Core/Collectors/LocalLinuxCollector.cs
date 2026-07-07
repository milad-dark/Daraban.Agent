using Daraban.Agent.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Local inventory collector for Linux hosts.
/// Mirrors LocalWindowsCollector's output shape (DeviceContent) but sources
/// data from /proc, /sys, and standard Linux CLI tools instead of WMI, so the
/// JSON produced is structurally compatible with what already ships to GLPI.
/// </summary>
[SupportedOSPlatform("linux")]
public class LocalLinuxCollector
{
    public DeviceInventory CollectLocal()
    {
        var content = new DeviceContent
        {
            ComputerName = Environment.MachineName,
            OperatingSystem = ReadOsPrettyName(),
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString()
        };

        CollectSystemAndBios(content);      // dmidecode (falls back to /sys/class/dmi)
        CollectCpu(content);                 // /proc/cpuinfo + lscpu
        CollectMemory(content);              // /proc/meminfo + dmidecode -t memory
        CollectStorage(content);             // lsblk
        CollectNetwork(content);             // /sys/class/net + ip addr
        CollectVideoControllers(content);    // lspci
        CollectUsersAndGroups(content);      // /etc/passwd, /etc/group
        CollectServices(content);            // systemctl
        CollectProcesses(content);           // /proc/<pid>
        CollectInstalledSoftware(content);   // dpkg or rpm
        CollectBattery(content);             // /sys/class/power_supply or upower

        return new DeviceInventory
        {
            DeviceId = content.ComputerName,
            Content = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true })
        };
    }

    // ---------- helpers ---------------------------------------------------

    private static string RunCommand(string cmd, string args, int timeoutMs = 5000)
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
            // Binary not present or not permitted (e.g. dmidecode needs root) — degrade gracefully.
            return string.Empty;
        }
    }

    private static string ReadFileSafe(string path)
    {
        try
        { return File.Exists(path) ? File.ReadAllText(path) : string.Empty; }
        catch { return string.Empty; }
    }

    private static string ReadOsPrettyName()
    {
        var text = ReadFileSafe("/etc/os-release");
        var match = Regex.Match(text, "^PRETTY_NAME=\"?(?<v>[^\"\n]+)\"?", RegexOptions.Multiline);
        return match.Success ? match.Groups["v"].Value : RuntimeInformation.OSDescription;
    }

    // ---------- BIOS / system chassis --------------------------------------

    private static void CollectSystemAndBios(DeviceContent content)
    {
        try
        {
            // dmidecode needs root; fall back to /sys/class/dmi/id (readable by all users on most distros)
            content.ComputerSystem = new ComputerSystemInfo
            {
                Manufacturer = ReadFileSafe("/sys/class/dmi/id/sys_vendor").Trim(),
                Model = ReadFileSafe("/sys/class/dmi/id/product_name").Trim(),
                SystemType = RuntimeInformation.OSArchitecture.ToString()
            };

            content.Bios = new BiosInfo
            {
                Manufacturer = ReadFileSafe("/sys/class/dmi/id/bios_vendor").Trim(),
                Version = ReadFileSafe("/sys/class/dmi/id/bios_version").Trim(),
                ReleaseDate = ReadFileSafe("/sys/class/dmi/id/bios_date").Trim(),
                SerialNumber = ReadFileSafe("/sys/class/dmi/id/product_serial").Trim() // usually empty unless root
            };

            content.MotherboardModel = ReadFileSafe("/sys/class/dmi/id/board_name").Trim();
            content.MotherboardVersion = ReadFileSafe("/sys/class/dmi/id/board_version").Trim();
            content.MotherboardSerial = ReadFileSafe("/sys/class/dmi/id/board_serial").Trim();

            // If running as root and dmidecode is installed, it gives richer/more reliable data.
            var dmi = RunCommand("dmidecode", "-t system");
            if (!string.IsNullOrWhiteSpace(dmi))
            {
                content.ComputerSystem.Manufacturer ??= ExtractDmi(dmi, "Manufacturer");
                content.ComputerSystem.Model ??= ExtractDmi(dmi, "Product Name");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting system/BIOS info: {ex.Message}");
        }
    }

    private static string? ExtractDmi(string block, string key)
    {
        var m = Regex.Match(block, $@"{Regex.Escape(key)}:\s*(?<v>.+)");
        return m.Success ? m.Groups["v"].Value.Trim() : null;
    }

    // ---------- CPU ---------------------------------------------------------

    private static void CollectCpu(DeviceContent content)
    {
        try
        {
            var cpuinfo = ReadFileSafe("/proc/cpuinfo");
            var blocks = cpuinfo.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            string? modelName = null;
            int physicalCores = 0;
            int logicalProcessors = 0;

            foreach (var block in blocks)
            {
                if (Regex.IsMatch(block, "^processor", RegexOptions.Multiline))
                    logicalProcessors++;

                var nameMatch = Regex.Match(block, @"model name\s*:\s*(?<v>.+)");
                if (nameMatch.Success)
                    modelName ??= nameMatch.Groups["v"].Value.Trim();
            }

            var lscpu = RunCommand("lscpu", "");
            var coresMatch = Regex.Match(lscpu, @"Core\(s\) per socket:\s*(?<v>\d+)");
            var socketsMatch = Regex.Match(lscpu, @"Socket\(s\):\s*(?<v>\d+)");
            var maxMhzMatch = Regex.Match(lscpu, @"CPU max MHz:\s*(?<v>[\d.]+)");
            var archMatch = Regex.Match(lscpu, @"Architecture:\s*(?<v>\S+)");

            if (coresMatch.Success && socketsMatch.Success)
                physicalCores = int.Parse(coresMatch.Groups["v"].Value) * int.Parse(socketsMatch.Groups["v"].Value);

            content.Cpus.Add(new CpuInfo
            {
                Name = modelName,
                Cores = physicalCores > 0 ? physicalCores.ToString() : null,
                LogicalProcessors = logicalProcessors > 0 ? logicalProcessors.ToString() : null,
                Speed = maxMhzMatch.Success ? maxMhzMatch.Groups["v"].Value : null,
                Architecture = archMatch.Success ? archMatch.Groups["v"].Value : RuntimeInformation.OSArchitecture.ToString()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting CPU info: {ex.Message}");
        }
    }

    // ---------- Memory --------------------------------------------------------

    private static void CollectMemory(DeviceContent content)
    {
        try
        {
            // dmidecode gives per-DIMM detail (needs root); fall back to a single aggregate entry from /proc/meminfo.
            var dmi = RunCommand("dmidecode", "-t memory");
            var handles = Regex.Matches(dmi, @"Memory Device\r?\n(?<body>(?:.+\r?\n)+?)(?=\r?\n)");

            if (handles.Count > 0)
            {
                foreach (Match h in handles)
                {
                    var body = h.Groups["body"].Value;
                    var size = ExtractDmi(body, "Size");
                    if (string.IsNullOrWhiteSpace(size) || size.Contains("No Module", StringComparison.OrdinalIgnoreCase))
                        continue;

                    content.Memories.Add(new MemoryInfo
                    {
                        Manufacturer = ExtractDmi(body, "Manufacturer"),
                        PartNumber = ExtractDmi(body, "Part Number"),
                        SerialNumber = ExtractDmi(body, "Serial Number"),
                        Caption = size,
                        DeviceLocator = ExtractDmi(body, "Locator"),
                        MemoryType = ExtractDmi(body, "Type"),
                        Speed = ParseLongPrefix(ExtractDmi(body, "Speed")),
                        Capacity = ParseMemorySizeToMb(size)
                    });
                }
            }
            else
            {
                var meminfo = ReadFileSafe("/proc/meminfo");
                var totalKbMatch = Regex.Match(meminfo, @"MemTotal:\s*(?<v>\d+)\s*kB");
                if (totalKbMatch.Success)
                {
                    var totalMb = long.Parse(totalKbMatch.Groups["v"].Value) / 1024;
                    content.Memories.Add(new MemoryInfo
                    {
                        Caption = $"{totalMb} MB",
                        Capacity = totalMb,
                        Description = "Aggregate total (per-DIMM detail requires root + dmidecode)"
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

    // ---------- Storage ---------------------------------------------------------

    private static void CollectStorage(DeviceContent content)
    {
        try
        {
            // lsblk -b -J gives machine-readable JSON with sizes in bytes.
            var json = RunCommand("lsblk", "-b -d -J -o NAME,MODEL,SIZE,TRAN,SERIAL,TYPE");
            if (string.IsNullOrWhiteSpace(json))
                return;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("blockdevices", out var devices))
                return;

            foreach (var dev in devices.EnumerateArray())
            {
                var type = dev.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type != "disk")
                    continue;

                var sizeBytes = dev.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                    ? s.GetInt64()
                    : 0;

                content.Storages.Add(new StorageInfo
                {
                    Model = dev.TryGetProperty("model", out var m) ? m.GetString()?.Trim() : null,
                    Size = $"{sizeBytes / 1024 / 1024 / 1024} GB",
                    InterfaceType = dev.TryGetProperty("tran", out var tr) ? tr.GetString() : null,
                    Serial = dev.TryGetProperty("serial", out var sn) ? sn.GetString() : null
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting storage info: {ex.Message}");
        }
    }

    // ---------- Network -----------------------------------------------------------

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

    // ---------- GPU -----------------------------------------------------------------

    private static void CollectVideoControllers(DeviceContent content)
    {
        try
        {
            var lspci = RunCommand("lspci", "-mm");
            foreach (var line in lspci.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains("VGA", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("3D controller", StringComparison.OrdinalIgnoreCase))
                    continue;

                content.VideoControllers.Add(new VideoControllerInfo
                {
                    Name = line
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting video controller info: {ex.Message}");
        }
    }

    // ---------- Users / groups ------------------------------------------------------

    private static void CollectUsersAndGroups(DeviceContent content)
    {
        try
        {
            foreach (var line in ReadFileSafe("/etc/passwd").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(':');
                if (parts.Length < 7)
                    continue;
                // Only list human accounts (UID >= 1000), skip system/service accounts.
                if (!int.TryParse(parts[2], out var uid) || uid < 1000)
                    continue;

                content.LocalUserAccounts.Add(new UserAccountInfo
                {
                    Name = parts[0],
                    FullName = parts[4],
                    SID = parts[2] // UID stands in for Windows SID as the stable local identifier
                });
            }

            foreach (var line in ReadFileSafe("/etc/group").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(':');
                if (parts.Length < 4)
                    continue;

                content.LocalGroups.Add(new GroupInfo
                {
                    Name = parts[0],
                    SID = parts[2]
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting users/groups: {ex.Message}");
        }
    }

    // ---------- Services --------------------------------------------------------------

    private static void CollectServices(DeviceContent content)
    {
        try
        {
            // systemctl is the modern standard; degrades to empty list on non-systemd distros.
            var output = RunCommand("systemctl", "list-units --type=service --all --no-legend --no-pager --plain");
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(line, @"^(?<unit>\S+)\s+(?<load>\S+)\s+(?<active>\S+)\s+(?<sub>\S+)\s*(?<desc>.*)$");
                if (!m.Success)
                    continue;

                content.Services.Add(new ServiceInfo
                {
                    Name = m.Groups["unit"].Value.Replace(".service", ""),
                    DisplayName = m.Groups["desc"].Value.Trim(),
                    State = m.Groups["active"].Value,
                    StartMode = m.Groups["load"].Value
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting services: {ex.Message}");
        }
    }

    // ---------- Processes -------------------------------------------------------------

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
                        ThreadCount = proc.Threads.Count.ToString(),
                        WorkingSetSize = proc.WorkingSet64.ToString()
                    });
                }
                catch
                {
                    // Some /proc entries vanish mid-enumeration or need elevated rights — skip silently.
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

    // ---------- Installed software -----------------------------------------------------

    private static void CollectInstalledSoftware(DeviceContent content)
    {
        try
        {
            // Debian/Ubuntu family
            var dpkg = RunCommand("dpkg-query", "-W -f=\"${Package}\\t${Version}\\t${Maintainer}\\n\"");
            if (!string.IsNullOrWhiteSpace(dpkg))
            {
                foreach (var line in dpkg.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Trim('"').Split('\t');
                    if (parts.Length < 2)
                        continue;
                    content.Software.Add(new SoftwareInfo
                    {
                        Name = parts[0],
                        Version = parts[1],
                        Vendor = parts.Length > 2 ? parts[2] : null
                    });
                }
                return;
            }

            // RHEL/Fedora/openSUSE family
            var rpm = RunCommand("rpm", "-qa --qf \"%{NAME}\\t%{VERSION}-%{RELEASE}\\t%{VENDOR}\\n\"");
            foreach (var line in rpm.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim('"').Split('\t');
                if (parts.Length < 2)
                    continue;
                content.Software.Add(new SoftwareInfo
                {
                    Name = parts[0],
                    Version = parts[1],
                    Vendor = parts.Length > 2 ? parts[2] : null
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting installed software: {ex.Message}");
        }
    }

    // ---------- Battery ------------------------------------------------------------------

    private static void CollectBattery(DeviceContent content)
    {
        try
        {
            const string basePath = "/sys/class/power_supply";
            if (!Directory.Exists(basePath))
                return;

            foreach (var dir in Directory.GetDirectories(basePath))
            {
                var type = ReadFileSafe(Path.Combine(dir, "type")).Trim();
                if (!string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase))
                    continue;

                var capacity = ReadFileSafe(Path.Combine(dir, "capacity")).Trim();
                var status = ReadFileSafe(Path.Combine(dir, "status")).Trim();

                content.Batteries.Add(new BatteryInfo
                {
                    Name = Path.GetFileName(dir),
                    EstimatedChargeRemaining = capacity,
                    BatteryStatus = status
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting battery info: {ex.Message}");
        }
    }
}
