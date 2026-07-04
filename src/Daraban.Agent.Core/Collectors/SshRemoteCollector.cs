using Daraban.Agent.Core.Models;
using Renci.SshNet;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Collectors;

public class SshRemoteCollector
{
    public async Task<DeviceInventory> CollectAsync(string host, string username, string passwordOrKey, CancellationToken ct = default)
    {
        using var client = new SshClient(host, username, passwordOrKey);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
        await client.ConnectAsync(ct);

        var content = new DeviceContent { ComputerName = host };

        try
        {
            // Get OS Info
            var osResult = await RunCommandAsync(client, "uname -a", ct);
            content.OperatingSystem = osResult.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];

            // Get Motherboard, CPU, RAM via dmidecode (requires root)
            var hwResult = await RunCommandAsync(client, "sudo dmidecode -t baseboard -t processor -t memory", ct);
            content.MotherboardModel = ExtractDmidecodeValue(hwResult, "Product Name");
            content.MotherboardSerial = ExtractDmideCount(hwResult, "Serial Number");
            content.Cpus.Add(new CpuInfo { Name = ExtractDmidecodeValue(hwResult, "Version") });

            // Parse RAM blocks
            var ramMatches = Regex.Matches(hwResult, @"Handle (\d+) DMID type (\d+), size (\d+) MB");
            foreach (Match m in ramMatches)
            {
                content.Memories.Add(new MemoryInfo { Capacity = $"{int.Parse(m.Groups[3].Value)} MB", Type = m.Groups[2].Value });
            }

            // Get Disks (HDD/SSD)
            var diskResult = await RunCommandAsync(client, "lsblk -d -o NAME,SIZE,TYPE,MODEL -b -n", ct);
            foreach (var line in diskResult.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4)
                {
                    content.Storages.Add(new StorageInfo
                    {
                        Model = parts[3],
                        Size = $"{long.Parse(parts[1]) / 1024 / 1024} MB",
                        InterfaceType = parts[2]
                    });
                }
            }

            // Get Installed Apps (Debian/Ubuntu or RHEL/CentOS)
            string pkgCmd = "dpkg -l 2>/dev/null || rpm -qa 2>/dev/null";
            var appResult = await RunCommandAsync(client, pkgCmd, ct);
            foreach (var line in appResult.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var appParts = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (appParts.Length >= 2)
                {
                    content.Software.Add(new SoftwareInfo { Name = appParts[1], Version = appParts[2] });
                }
            }
        }
        finally
        {
            client.Disconnect();
        }

        return new DeviceInventory { Content = JsonSerializer.Serialize(content) };
    }

    private static async Task<string> RunCommandAsync(SshClient client, string command, CancellationToken ct)
    {
        using var cmd = client.CreateCommand(command);
        await cmd.ExecuteAsync(ct);
        return cmd.Result;
    }

    private static string ExtractDmidecodeValue(string text, string key)
    {
        var match = Regex.Match(text, $@"{key}:\s*(.+)");
        return match.Success ? match.Groups[1].Value.Trim() : "Unknown";
    }

    private static string ExtractDmideCount(string text, string key)
    {
        var match = Regex.Match(text, $@"{key}:\s*(.+)");
        return match.Success ? match.Groups[1].Value.Trim() : "Unknown";
    }
}