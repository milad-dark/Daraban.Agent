using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Differential inventory support, mirroring glpi-agent's `full-inventory-postpone`.
///
/// After the first full inventory, every category of DeviceContent is hashed and stored
/// in a local snapshot file (`last_inventory.json`). On later runs, only categories whose
/// hash changed since the last snapshot are included in the payload — GLPI then only
/// updates those sections, cutting server load substantially.
///
/// A run counter in the snapshot forces a full inventory every `FullInventoryPostpone`
/// runs (default 14), exactly like glpi-agent.
/// </summary>
public static class InventoryDiffer
{
    // ── public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies differential logic to the freshly collected inventory:
    ///  - loads the previous snapshot
    ///  - decides full vs partial (counter-driven)
    ///  - when partial, blanks every category identical to the snapshot (unless on the required list)
    /// Returns the (possibly trimmed) content plus the effective action.
    /// </summary>
    public static (DeviceContent Content, string Action, bool IsFull) Diff(
        DeviceContent current,
        AgentOptions options,
        string snapshotPath)
    {
        var categoryMap = CategoryMap();

        if (options.FullInventoryPostpone <= 0)
            return (current, "inventory", true);

        var snapshot = LoadSnapshot(snapshotPath);
        var entries = ComputeCategoryHashes(current);
        var hasSnapshot = File.Exists(snapshotPath);

        // First run (no snapshot) or the countdown reached zero → full inventory.
        // After a full run, a fresh countdown is armed (postpone partials allowed).
        if (!hasSnapshot || snapshot.RunCounter <= 0)
        {
            SaveSnapshot(snapshotPath, new InventorySnapshot
            {
                RunCounter = options.FullInventoryPostpone,
                Categories = entries
            }, current);
            return (current, "inventory", true);
        }

        // Partial run: keep only changed (or required) categories, then decrement countdown.
        var changedCategories = new List<string>();
        foreach (var (category, hash) in entries)
        {
            var prevHash = snapshot.Categories.GetValueOrDefault(category);
            var isRequired = options.RequiredCategories.Contains(category,
                StringComparer.OrdinalIgnoreCase);

            if (isRequired || prevHash is null || !string.Equals(prevHash, hash, StringComparison.Ordinal))
                changedCategories.Add(category);
        }

        var trimmed = BlankUnchangedCategories(current, changedCategories, categoryMap);

        SaveSnapshot(snapshotPath, new InventorySnapshot
        {
            RunCounter = snapshot.RunCounter - 1,
            Categories = entries
        }, current);

        return (trimmed, "partial", false);
    }

    /// <summary>Path of the diff snapshot file for a given agent.</summary>
    public static string DefaultSnapshotPath(string? agentId)
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Daraban.Agent",
            $"last_inventory_{Sanitize(agentId ?? "default")}.json");

    // ── snapshot IO ────────────────────────────────────────────────────────────

    public static InventorySnapshot LoadSnapshot(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<InventorySnapshot>(json, JsonOpts) ?? new InventorySnapshot();
            }
        }
        catch
        {
            // Corrupt/partial snapshot — start fresh with a full inventory.
        }
        return new InventorySnapshot();
    }

    public static void SaveSnapshot(string path, InventorySnapshot snapshot, DeviceContent fullContent)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = new
            {
                snapshot.RunCounter,
                snapshot.Categories,
                fullContent
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOpts));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[partial] Failed to write inventory snapshot: {ex.Message}");
        }
    }

    // ── internals ──────────────────────────────────────────────────────────────

    private static Dictionary<string, string> ComputeCategoryHashes(DeviceContent content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var map = CategoryMap();

        foreach (var (name, prop) in map.Values)
        {
            var value = prop.GetValue(content);
            var json = JsonSerializer.Serialize(value, JsonOpts);
            result[name] = HashString(json);
        }
        return result;
    }

    private static DeviceContent BlankUnchangedCategories(
        DeviceContent content,
        List<string> keepCategories,
        Dictionary<string, (string Name, System.Reflection.PropertyInfo Prop)> map)
    {
        foreach (var (category, (_, prop)) in map)
        {
            if (keepCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
                continue;

            // Blank lists with a fresh empty list; leave scalars (ComputerName etc.)
            // untouched since the server treats them as top-level identifiers.
            if (prop.PropertyType.IsGenericType &&
                prop.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                prop.SetValue(content, Activator.CreateInstance(prop.PropertyType));
            }
        }
        return content;
    }

    /// <summary>Category name (for RequiredCategories / no-category) → DeviceContent property.</summary>
    public static Dictionary<string, (string Name, System.Reflection.PropertyInfo Prop)> CategoryMap()
    {
        var map = new Dictionary<string, (string, System.Reflection.PropertyInfo)>(StringComparer.OrdinalIgnoreCase);
        var type = typeof(DeviceContent);

        var entries = new (string Alias, string PropName)[]
        {
            ("system", "ComputerName"),
            ("operating system", "OperatingSystem"),
            ("computersystem", "ComputerSystem"),
            ("bios", "Bios"),
            ("motherboard", "MotherboardSerial"),
            ("cpu", "Cpus"),
            ("memory", "Memories"),
            ("storage", "Storages"),
            ("network", "Networks"),
            ("monitor", "Monitors"),
            ("audio", "AudioDevices"),
            ("video", "VideoControllers"),
            ("user", "LocalUserAccounts"),
            ("group", "LocalGroups"),
            ("service", "Services"),
            ("desktop", "Desktops"),
            ("hotfix", "Hotfixes"),
            ("process", "Processes"),
            ("printer", "Printers"),
            ("software", "Software"),
            ("battery", "Batteries"),
        };

        foreach (var (alias, propName) in entries)
        {
            var prop = type.GetProperty(propName);
            if (prop is not null)
                map[alias] = (propName, prop);
        }

        return map;
    }

    private static string HashString(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string Sanitize(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(input.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

/// <summary>State persisted between runs for differential inventory.</summary>
public class InventorySnapshot
{
    public int RunCounter { get; set; }
    public Dictionary<string, string> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}