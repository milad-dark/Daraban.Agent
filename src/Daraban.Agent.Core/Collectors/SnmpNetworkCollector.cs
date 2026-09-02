using Daraban.Agent.Core.Models;
using Lextm.SharpSnmpLib;
using System.Net;
using System.Text.Json;

namespace Daraban.Agent.Core.Collectors;

public class SnmpNetworkCollector
{
    // ── OIDs ───────────────────────────────────────────────────────────────────
    private static readonly string OidSysDescr = "1.3.6.1.2.1.1.1.0";
    private static readonly string OidSysObjectID = "1.3.6.1.2.1.1.2.0";
    private static readonly string OidSysName = "1.3.6.1.2.1.1.5.0";

    private static readonly string OidIfDescr = "1.3.6.1.2.1.2.2.1.2";
    private static readonly string OidIfPhysAddress = "1.3.6.1.2.1.2.2.1.6";
    private static readonly string OidIfType = "1.3.6.1.2.1.2.2.1.3";
    private static readonly string OidIfAdminStatus = "1.3.6.1.2.1.2.2.1.7";

    private static readonly string OidHrStorageDescr = "1.3.6.1.2.1.25.2.3.1.3";
    private static readonly string OidHrStorageSize = "1.3.6.1.2.1.25.2.3.1.5";
    private static readonly string OidHrStorageUsed = "1.3.6.1.2.1.25.2.3.1.6";
    private static readonly string OidHrStorageType = "1.3.6.1.2.1.25.2.3.1.2";

    private readonly SnmpSession? _session;

    /// <summary>Creates a collector using the default v1/v2c "public" credentials.</summary>
    public SnmpNetworkCollector()
    {
    }

    /// <summary>Creates a collector bound to a pre-built session (enables SNMPv3 USM).</summary>
    public SnmpNetworkCollector(SnmpSession session)
    {
        _session = session;
    }

    public async Task<DeviceInventory> DiscoverAsync(string ipAddress, string community = "public", int timeoutMs = 2000, CancellationToken ct = default)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), 161);
        var content = new DeviceContent { ComputerName = ipAddress };

        try
        {
            var sysDescr = await SessionGetAsync(endpoint, community, timeoutMs, OidSysDescr, ct);
            var sysName = await SessionGetAsync(endpoint, community, timeoutMs, OidSysName, ct);

            content.OperatingSystem = sysDescr ?? "Unknown";
            content.ComputerName = sysName ?? ipAddress;

            await DiscoverNetworkInterfacesAsync(endpoint, community, content, ipAddress, timeoutMs, ct);
            await DiscoverStorageAsync(endpoint, community, content, timeoutMs, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SNMP] Failed to query {ipAddress}: {ex.Message}");
        }

        return new DeviceInventory { Content = JsonSerializer.Serialize(content) };
    }

    private async Task DiscoverNetworkInterfacesAsync(IPEndPoint endpoint, string community, DeviceContent content, string ipAddress, int timeoutMs, CancellationToken ct)
    {
        try
        {
            var ifDescriptions = await SessionWalkAsync(endpoint, community, OidIfDescr, timeoutMs, ct);
            var ifPhysAddresses = await SessionWalkAsync(endpoint, community, OidIfPhysAddress, timeoutMs, ct);

            for (int i = 0; i < ifDescriptions.Count; i++)
            {
                var description = ifDescriptions[i].Data.ToString();
                var macAddress = "";

                if (i < ifPhysAddresses.Count)
                {
                    var physAddr = ifPhysAddresses[i].Data as OctetString;
                    if (physAddr != null)
                        macAddress = BitConverter.ToString(physAddr.GetRaw()).Replace("-", ":");
                }

                if (!string.IsNullOrWhiteSpace(description))
                {
                    content.Networks.Add(new NetworkInterfaceInfo
                    {
                        Description = description,
                        MACAddress = macAddress,
                        IPAddress = string.IsNullOrWhiteSpace(macAddress) ? "" : ipAddress,
                        Status = "Up"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SNMP] Failed to discover network interfaces: {ex.Message}");
        }
    }

    private async Task DiscoverStorageAsync(IPEndPoint endpoint, string community, DeviceContent content, int timeoutMs, CancellationToken ct)
    {
        try
        {
            var storageDescriptions = await SessionWalkAsync(endpoint, community, OidHrStorageDescr, timeoutMs, ct);
            var storageSizes = await SessionWalkAsync(endpoint, community, OidHrStorageSize, timeoutMs, ct);
            var storageTypes = await SessionWalkAsync(endpoint, community, OidHrStorageType, timeoutMs, ct);

            for (int i = 0; i < storageDescriptions.Count && i < storageSizes.Count; i++)
            {
                var descr = storageDescriptions[i].Data.ToString();
                var sizeStr = storageSizes[i].Data.ToString();
                var storageType = i < storageTypes.Count ? storageTypes[i].Data.ToString() : "";

                if (descr.Contains("Physical") || descr.Contains("Disk") || descr.Contains("/"))
                {
                    if (long.TryParse(sizeStr, out var size) && size > 0)
                    {
                        content.Storages.Add(new StorageInfo
                        {
                            Model = descr,
                            Size = $"{size / 1024 / 1024} MB",
                            Serial = storageType.Contains("4") ? "SSD" : "HDD",
                            InterfaceType = "SNMP"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SNMP] Failed to discover storage: {ex.Message}");
        }
    }

    // ── Session-aware GET/WALK (v1/v2c default, v3 when a session is bound) ────

    private async Task<string?> SessionGetAsync(IPEndPoint endpoint, string community, int timeoutMs, string oid, CancellationToken ct)
    {
        if (_session is not null)
            return await _session.GetAsync(endpoint, oid, timeoutMs, ct);

        // Default path: fresh v1/v2c session per call (matches legacy behavior).
        using var session = new SnmpSession(new SnmpCredentials { Community = community, TimeoutMs = timeoutMs });
        return await session.GetAsync(endpoint, oid, timeoutMs, ct);
    }

    private async Task<List<Variable>> SessionWalkAsync(IPEndPoint endpoint, string community, string rootOid, int timeoutMs, CancellationToken ct)
    {
        if (_session is not null)
            return await _session.WalkAsync(endpoint, rootOid, timeoutMs, ct);

        using var session = new SnmpSession(new SnmpCredentials { Community = community, TimeoutMs = timeoutMs });
        return await session.WalkAsync(endpoint, rootOid, timeoutMs, ct);
    }

    // ── Lightweight 3-OID probe used by NetDiscoveryTask for fingerprinting ────

    /// <summary>Compatibility overload using v2c community credentials (no v3).</summary>
    public static Task<SnmpFingerprint?> ProbeForDiscoveryAsync(string ipAddress, string community, int timeoutMs)
        => ProbeWithSessionAsync(ipAddress, new SnmpSession(new SnmpCredentials { Community = community, TimeoutMs = timeoutMs }), timeoutMs);

    /// <summary>Probe using a session built from AgentOptions (supports v3 USM).</summary>
    public static async Task<SnmpFingerprint?> ProbeForDiscoveryAsync(string ipAddress, SnmpSession session, int timeoutMs)
        => await ProbeWithSessionAsync(ipAddress, session, timeoutMs);

    private static async Task<SnmpFingerprint?> ProbeWithSessionAsync(string ipAddress, SnmpSession session, int timeoutMs)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), 161);

        try
        {
            var descrTask = session.GetAsync(endpoint, OidSysDescr, timeoutMs);
            var objectIdTask = session.GetAsync(endpoint, OidSysObjectID, timeoutMs);
            var nameTask = session.GetAsync(endpoint, OidSysName, timeoutMs);

            await Task.WhenAll(descrTask, objectIdTask, nameTask);

            var sysDescr = await descrTask;
            var sysObjectId = await objectIdTask;
            var sysName = await nameTask;

            if (sysDescr is null && sysObjectId is null && sysName is null)
                return null;

            return new SnmpFingerprint
            {
                SysDescr = sysDescr,
                SysObjectId = sysObjectId,
                SysName = sysName
            };
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Lightweight result from a 3-OID SNMP probe during NetDiscovery.
/// </summary>
public sealed class SnmpFingerprint
{
    public string? SysDescr { get; init; }
    public string? SysObjectId { get; init; }
    public string? SysName { get; init; }
}