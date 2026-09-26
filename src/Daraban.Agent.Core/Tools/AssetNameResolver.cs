using System.Net;
using System.Net.NetworkInformation;

namespace Daraban.Agent.Core.Tools;

/// <summary>
/// Normalizes the reported computer name, mirroring glpi-agent's `assetname-support`:
///   1 = short name (default): "host.domain" → "host"
///   2 = as-found: return the name exactly as the collector reported it
///   3 = always FQDN: resolve to the fully qualified domain name when possible
/// </summary>
public static class AssetNameResolver
{
    public static string Resolve(string rawName, int mode)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return rawName;

        return mode switch
        {
            2 => rawName,
            3 => ResolveFqdn(rawName),
            _ => rawName.Split('.')[0],
        };
    }

    private static string ResolveFqdn(string rawName)
    {
        try
        {
            var fqdn = Dns.GetHostEntry(rawName).HostName;
            if (!string.IsNullOrWhiteSpace(fqdn))
                return fqdn;
        }
        catch
        {
            // DNS resolution failed (offline, no reverse record, etc.) — fall through.
        }

        // Best effort: if the name already carries a dot, treat it as an FQDN.
        if (rawName.Contains('.'))
            return rawName;

        // Otherwise append the primary DNS suffix when one is configured.
        var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        return string.IsNullOrWhiteSpace(domain) ? rawName : $"{rawName}.{domain}";
    }
}
