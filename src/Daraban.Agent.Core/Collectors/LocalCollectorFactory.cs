using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Tools;
using System.Runtime.InteropServices;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Picks the right platform-specific local collector at runtime, so callers
/// (LocalInventoryTask, the CLI's --method local switch, etc.) don't need
/// their own OS checks scattered around.
/// </summary>
public static class LocalCollectorFactory
{
    public static DeviceInventory CollectLocal(AgentOptions? options = null)
    {
        DeviceInventory inventory;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            inventory = new LocalWindowsCollector().CollectLocal(options);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            inventory = new LocalLinuxCollector().CollectLocal(options);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            inventory = new LocalMacCollector().CollectLocal(options);
        else
            throw new PlatformNotSupportedException(
                $"No local inventory collector implemented for {RuntimeInformation.OSDescription}");

        // Normalize the reported computer name (mirrors glpi-agent `assetname-support`).
        // The collector serializes DeviceContent as JSON, so rewrite it there.
        if (options is not null)
        {
            var content = System.Text.Json.JsonSerializer.Deserialize<DeviceContent>(inventory.Content);
            if (content is not null)
            {
                content.ComputerName = AssetNameResolver.Resolve(content.ComputerName ?? "", options.AssetNameSupport);
                inventory.Content = System.Text.Json.JsonSerializer.Serialize(content);
            }
        }

        return inventory;
    }
}