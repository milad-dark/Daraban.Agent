using Daraban.Agent.Core.Models;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using System.Net;
using System.Text.Json;

namespace Daraban.Agent.Core.Collectors;

public class SnmpNetworkCollector
{
    // ── all your existing fields and methods stay exactly as they are ─────────
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

    public async Task<DeviceInventory> DiscoverAsync(string ipAddress, string community = "public", int timeoutMs = 2000, CancellationToken ct = default)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), 161);
        var content = new DeviceContent { ComputerName = ipAddress };

        try
        {
            var sysDescr = await GetAsync(endpoint, community, OidSysDescr, timeoutMs);
            var sysName = await GetAsync(endpoint, community, OidSysName, timeoutMs);

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
            var ifDescriptions = await WalkAsync(endpoint, community, OidIfDescr, timeoutMs, ct);
            var ifPhysAddresses = await WalkAsync(endpoint, community, OidIfPhysAddress, timeoutMs, ct);

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
            var storageDescriptions = await WalkAsync(endpoint, community, OidHrStorageDescr, timeoutMs, ct);
            var storageSizes = await WalkAsync(endpoint, community, OidHrStorageSize, timeoutMs, ct);
            var storageTypes = await WalkAsync(endpoint, community, OidHrStorageType, timeoutMs, ct);

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

    private static async Task<string?> GetAsync(IPEndPoint endpoint, string community, string oid, int timeoutMs)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                var variables = new List<Variable> { new(new ObjectIdentifier(oid)) };
                var response = Messenger.Get(VersionCode.V2,
                    endpoint,
                    new OctetString(community),
                    variables,
                    timeoutMs);

                return response.FirstOrDefault()?.Data.ToString();
            });

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<Variable>> WalkAsync(IPEndPoint endpoint, string community, string rootOid, int timeoutMs, CancellationToken ct)
    {
        var results = new List<Variable>();

        await Task.Run(() =>
        {
            try
            {
                Messenger.Walk(VersionCode.V2,
                    endpoint,
                    new OctetString(community),
                    new ObjectIdentifier(rootOid),
                    results,
                    timeoutMs,
                    WalkMode.WithinSubtree);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SNMP] Walk failed for {rootOid}: {ex.Message}");
            }
        }, ct);

        return results;
    }

    // ── NEW: lightweight 3-OID probe used by NetDiscoveryTask for fingerprinting ──
    // Does NOT do a full walk — only fetches sysDescr, sysObjectID, sysName.
    // Called once per discovered host, after ICMP/ARP already found it.
    // Returns null if the host does not respond to SNMP at all.
    public static async Task<SnmpFingerprint?> ProbeForDiscoveryAsync(
        string ipAddress,
        string community,
        int timeoutMs)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), 161);

        try
        {
            // Fire all 3 GETs concurrently — faster than sequential
            var descrTask = GetAsync(endpoint, community, OidSysDescr, timeoutMs);
            var objectIdTask = GetAsync(endpoint, community, OidSysObjectID, timeoutMs);
            var nameTask = GetAsync(endpoint, community, OidSysName, timeoutMs);

            await Task.WhenAll(descrTask, objectIdTask, nameTask);

            var sysDescr = await descrTask;
            var sysObjectId = await objectIdTask;
            var sysName = await nameTask;

            // If all 3 came back null the host is not speaking SNMP
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
            return null;    // host not reachable on SNMP — not an error, just not an SNMP device
        }
    }
}

/// <summary>
/// Lightweight result from a 3-OID SNMP probe during NetDiscovery.
/// Only the fields needed for fingerprinting — not a full inventory.
/// Full inventory is done later by NetInventoryTask.
/// </summary>
public sealed class SnmpFingerprint
{
    public string? SysDescr { get; init; }
    public string? SysObjectId { get; init; }
    public string? SysName { get; init; }
}