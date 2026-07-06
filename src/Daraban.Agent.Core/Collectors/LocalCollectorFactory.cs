using Daraban.Agent.Core.Models;
using System.Runtime.InteropServices;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// Picks the right platform-specific local collector at runtime, so callers
/// (LocalInventoryTask, the CLI's --method local switch, etc.) don't need
/// their own OS checks scattered around.
/// </summary>
public static class LocalCollectorFactory
{
    public static DeviceInventory CollectLocal()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new LocalWindowsCollector().CollectLocal();

        throw new PlatformNotSupportedException(
            $"No local inventory collector implemented for {RuntimeInformation.OSDescription}");
    }
}