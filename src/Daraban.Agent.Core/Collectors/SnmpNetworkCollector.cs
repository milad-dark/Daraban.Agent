using Daraban.Agent.Core.Models;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using System.Net;
using System.Text.Json;

namespace Daraban.Agent.Core.Collectors;

public class SnmpNetworkCollector
{
    // Standard OIDs for hardware discovery
    private static readonly string OidSysDescr = "1.3.6.1.2.1.1.1.0";
    private static readonly string OidSysObjectID = "1.3.6.1.2.1.1.2.0";
    private static readonly string OidSysName = "1.3.6.1.2.1.1.5.0";

    // Interface OIDs
    private static readonly string OidIfDescr = "1.3.6.1.2.1.2.2.1.2";      // Interface description
    private static readonly string OidIfPhysAddress = "1.3.6.1.2.1.2.2.1.6"; // MAC address
    private static readonly string OidIfType = "1.3.6.1.2.1.2.2.1.3";        // Interface type
    private static readonly string OidIfAdminStatus = "1.3.6.1.2.1.2.2.1.7"; // Admin status

    // Storage OIDs (HOST-RESOURCES-MIB)
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
            // 1. Get System Info
            var sysDescr = await GetAsync(endpoint, community, OidSysDescr, timeoutMs);
            var sysName = await GetAsync(endpoint, community, OidSysName, timeoutMs);

            content.OperatingSystem = sysDescr ?? "Unknown";
            content.ComputerName = sysName ?? ipAddress;

            // 2. Walk Network Interfaces
            await DiscoverNetworkInterfacesAsync(endpoint, community, content, ipAddress, timeoutMs, ct);

            // 3. Walk Storage Devices
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
                    {
                        macAddress = BitConverter.ToString(physAddr.GetRaw()).Replace("-", ":");
                    }
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

                // Only include physical disks
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
                var walkMode = WalkMode.WithinSubtree;
                var walked = Messenger.Walk(VersionCode.V2,
                    endpoint,
                    new OctetString(community),
                    new ObjectIdentifier(rootOid),
                    results,
                    timeoutMs,
                    walkMode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SNMP] Walk failed for {rootOid}: {ex.Message}");
            }
        }, ct);

        return results;
    }
}