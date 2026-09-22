using Daraban.Agent.Core.Models;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Home-directory scanning shared by the local collectors (mirrors glpi-agent
/// `scan-homedirs`): finds VM definition files in user profiles on any OS, and
/// license/activation plists on macOS. All walks are depth-capped and tolerate
/// missing directories, denied access, and symlink loops.
/// </summary>
public static partial class HomeDirScanner
{
    /// <summary>Maximum directory depth below a scan root (glpi-agent equivalent bound).</summary>
    public const int MaxDepth = 5;

    private const long MaxProbeFileBytes = 4 * 1024 * 1024;

    [GeneratedRegex(@"^\s*displayName\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex VmxDisplayNameRegex();

    [GeneratedRegex(@"<key>\s*([^<>]*?)\s*</key>\s*<(?:string|date|integer|real)>\s*([^<>]+?)\s*</(?:string|date|integer|real)>", RegexOptions.IgnoreCase)]
    private static partial Regex PlistKeyedStringRegex();

    // ---------- file discovery ------------------------------------------------

    /// <summary>
    /// Breadth-first search for <paramref name="patterns"/> under <paramref name="roots"/>,
    /// descending at most <paramref name="maxDepth"/> directory levels below each root.
    /// Missing roots are skipped; per-directory errors are swallowed.
    /// </summary>
    public static IEnumerable<string> FindFiles(IEnumerable<string?> roots, IEnumerable<string> patterns, int maxDepth = MaxDepth)
    {
        var patternList = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
        if (patternList.Count == 0)
            yield break;

        var queue = new Queue<(string Dir, int Depth)>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            string full;
            try { full = Path.GetFullPath(root); }
            catch { continue; }
            if (!seenDirs.Add(full) || !Directory.Exists(full))
                continue;
            queue.Enqueue((full, 0));
        }

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();

            // Materialize inside try: yield cannot appear in a block with catch clauses.
            List<string> matched;
            try
            {
                matched = patternList
                    .SelectMany(p => Directory.EnumerateFiles(dir, p, SearchOption.TopDirectoryOnly))
                    .ToList();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { continue; }

            foreach (var file in matched)
                yield return file;

            if (depth >= maxDepth)
                continue;

            List<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(dir).ToList(); }
            catch { continue; }

            foreach (var sub in subdirs)
            {
                try
                {
                    // Skip reparse points (symlinks/junctions) to avoid infinite loops.
                    if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch { continue; }
                queue.Enqueue((sub, depth + 1));
            }
        }
    }

    // ---------- VM files (any OS) ----------------------------------------------

    /// <summary>Scans <paramref name="roots"/> for VM definition files, deduplicated by path.</summary>
    public static List<SoftwareInfo> ScanVmFiles(IEnumerable<string?> roots, IEnumerable<string> patterns, int maxDepth = MaxDepth)
    {
        var results = new List<SoftwareInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in FindFiles(roots, patterns, maxDepth))
        {
            if (!seen.Add(file))
                continue;
            results.Add(FromVmFile(file));
        }
        return results;
    }

    /// <summary>Maps one VM definition file to a Software entry. Never throws.</summary>
    public static SoftwareInfo FromVmFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var vendor = ext switch
        {
            ".vmx" => "VMware",
            ".vbox" => "VirtualBox",
            ".vmcx" => "Hyper-V",
            ".qcow2" => "QEMU",
            _ => "VM",
        };

        string? name = ext == ".vmx" ? ReadVmxDisplayName(path) : null;
        name ??= Path.GetFileNameWithoutExtension(path);

        return new SoftwareInfo
        {
            Name = name,
            Vendor = vendor,
            Version = "detected",
            Caption = path,
        };
    }

    private static string? ReadVmxDisplayName(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxProbeFileBytes)
                return null;
            var text = File.ReadAllText(path);
            var match = VmxDisplayNameRegex().Match(text);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    // ---------- macOS license plists --------------------------------------------

    /// <summary>Scans <paramref name="roots"/> for *.plist files carrying license/activation data.</summary>
    public static List<SoftwareInfo> ScanLicensePlists(IEnumerable<string?> roots, int maxDepth = MaxDepth)
    {
        var results = new List<SoftwareInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in FindFiles(roots, ["*.plist"], maxDepth))
        {
            if (!seen.Add(file))
                continue;
            var entry = TryParseLicensePlist(file);
            if (entry is not null)
                results.Add(entry);
        }
        return results;
    }

    /// <summary>
    /// Parses one plist for license/activation markers. Returns null when the file has
    /// none (or cannot be read), so callers only record real license hits.
    /// </summary>
    public static SoftwareInfo? TryParseLicensePlist(string path)
    {
        string text;
        try
        {
            if (new FileInfo(path).Length > MaxProbeFileBytes)
                return null;
            text = File.ReadAllText(path);
        }
        catch
        {
            return null;
        }

        if (text.IndexOf("license", StringComparison.OrdinalIgnoreCase) < 0 &&
            text.IndexOf("activation", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        string? serial = null;
        string? expiry = null;
        foreach (Match m in PlistKeyedStringRegex().Matches(text))
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value.Trim();
            if (string.IsNullOrEmpty(value))
                continue;
            if (serial is null && (key.Contains("serial", StringComparison.OrdinalIgnoreCase) ||
                (key.Contains("license", StringComparison.OrdinalIgnoreCase) && key.Contains("key", StringComparison.OrdinalIgnoreCase)) ||
                (key.Contains("activation", StringComparison.OrdinalIgnoreCase) && key.Contains("key", StringComparison.OrdinalIgnoreCase)) ||
                key.Contains("registration", StringComparison.OrdinalIgnoreCase)))
            {
                serial = value.Length > 64 ? value[..64] : value;
            }
            if (expiry is null && key.Contains("expir", StringComparison.OrdinalIgnoreCase))
            {
                expiry = value.Length > 32 ? value[..32] : value;
            }
            if (serial is not null && expiry is not null)
                break;
        }

        return new SoftwareInfo
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Version = expiry ?? "detected",
            Caption = serial is null ? "License key present" : $"License serial: {serial}",
        };
    }

    // ---------- scan roots -------------------------------------------------------

    /// <summary>Windows profile roots: every directory under C:\Users plus %PUBLIC%.</summary>
    public static IEnumerable<string> WindowsProfileRoots()
    {
        string? systemDir = null;
        try { systemDir = Environment.SystemDirectory; } catch { /* leave null */ }
        var systemRoot = !string.IsNullOrEmpty(systemDir) ? Path.GetPathRoot(systemDir) : null;
        var usersDir = systemRoot is not null ? Path.Combine(systemRoot, "Users") : @"C:\Users";

        List<string> dirs;
        try { dirs = Directory.Exists(usersDir) ? Directory.GetDirectories(usersDir).ToList() : []; }
        catch { dirs = []; }
        foreach (var dir in dirs)
            yield return dir;

        string? publicDir = null;
        try { publicDir = Environment.GetEnvironmentVariable("PUBLIC"); } catch { /* leave null */ }
        if (!string.IsNullOrWhiteSpace(publicDir) && Directory.Exists(publicDir))
            yield return publicDir;
    }

    /// <summary>Unix home roots: $HOME plus every directory under /home.</summary>
    public static IEnumerable<string> UnixHomeRoots()
    {
        string? home = null;
        try { home = Environment.GetEnvironmentVariable("HOME"); } catch { /* leave null */ }
        if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
            yield return home;

        List<string> dirs;
        try { dirs = Directory.Exists("/home") ? Directory.GetDirectories("/home").ToList() : []; }
        catch { dirs = []; }
        foreach (var dir in dirs)
            yield return dir;
    }
}
