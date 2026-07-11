using Daraban.Agent.Core.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Collectors;

public sealed class CollectCollector
{
    // NO constructor — matches your project pattern

    public async Task<CollectResult> RunAsync(CollectJob job, CancellationToken ct)
    {
        try
        {
            var value = job.Type switch
            {
                CollectJobType.RegistryKey => ReadRegistry(job),
                CollectJobType.WmiQuery => RunWmi(job),
                CollectJobType.FileContent => ReadFile(job),
                CollectJobType.Command => await RunCommandAsync(job, ct),
                _ => throw new NotSupportedException($"Unknown collect type: {job.Type}")
            };

            return new CollectResult
            {
                JobId = job.JobId,
                Success = true,
                Value = value,
                CollectedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[collect] Job {job.JobId} failed: {ex.Message}");
            return new CollectResult
            {
                JobId = job.JobId,
                Success = false,
                Error = ex.Message,
                CollectedAt = DateTime.UtcNow
            };
        }
    }

    private static string? ReadRegistry(CollectJob job)
    {
        // RUNTIME check — not compile-time #if
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException(
                "Registry reads are only supported on Windows.");

        var hive = job.RegistryHive?.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            _ => throw new ArgumentException($"Unknown registry hive: {job.RegistryHive}")
        };

        using var key = hive.OpenSubKey(
            job.RegistryPath ?? throw new ArgumentNullException(nameof(job.RegistryPath)));

        return key?.GetValue(job.RegistryValue)?.ToString();
    }


    private static string? RunWmi(CollectJob job)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("WMI queries are only supported on Windows.");

        var query = job.WmiQuery ?? throw new ArgumentNullException(nameof(job.WmiQuery));
        var scope = new ManagementScope(
            $"\\\\localhost\\{(job.WmiNamespace ?? "root\\cimv2").Replace("/", "\\")}");
        var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));

        var results = new List<string>();
        foreach (ManagementObject obj in searcher.Get())
        {
            var prop = string.IsNullOrWhiteSpace(job.WmiProperty)
                ? obj.Properties.Cast<PropertyData>().FirstOrDefault()?.Value
                : obj[job.WmiProperty];

            if (prop is not null)
                results.Add(prop.ToString()!);
        }

        return results.Count > 0 ? string.Join("; ", results) : null;
    }


    private static string? ReadFile(CollectJob job)
    {
        string path = job.FilePath ?? throw new ArgumentNullException(nameof(job.FilePath));

        if (!File.Exists(path))
            return null;

        var lines = File.ReadAllLines(path);

        if (string.IsNullOrWhiteSpace(job.FileRegex))
            return string.Join(Environment.NewLine, lines);

        var rx = new Regex(job.FileRegex,
            RegexOptions.Multiline | RegexOptions.Compiled);

        foreach (var line in lines)
        {
            var m = rx.Match(line);
            if (!m.Success)
                continue;
            return m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
        }

        return null;
    }

    private static async Task<string?> RunCommandAsync(CollectJob job, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = job.Command
                                     ?? throw new ArgumentNullException(nameof(job.Command)),
            Arguments = job.Arguments ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start: {job.Command}");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
    }
}