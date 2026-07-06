using Daraban.Agent.Core.Models;

namespace Daraban.Agent.Core.Transport;

public interface IDarabanClient
{
    /// <summary>
    /// Mirrors the real agent's "prolog": a handshake the server uses to tell the agent
    /// which tasks/schedule apply to it. Call this before running scheduled tasks.
    /// Returns the raw JSON config the server replied with, or null if unreachable.
    /// </summary>
    Task<string?> PrologAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Sends a local/remote computer inventory using GLPI's native JSON inventory format.</summary>
    Task PostInventoryAsync(string deviceId, object contentObject, string itemtype = "Computer", CancellationToken ct = default);

    /// <summary>Backward-compatible overload for callers that already serialized JSON.</summary>
    Task PostInventoryAsync(string jsonPayload, CancellationToken ct = default);

    Task PostDiscoveryAsync(string deviceId, IEnumerable<DiscoveredHost> hosts, CancellationToken ct = default);

    Task PostNetInventoryAsync(string deviceId, IEnumerable<NetworkDeviceInventory> devices, CancellationToken ct = default);

    Task PostWakeOnLanResultAsync(string deviceId, IEnumerable<WakeOnLanResult> results, CancellationToken ct = default);

    Task PostEsxInventoryAsync(string deviceId, EsxHostInfo host, CancellationToken ct = default);

    /// <summary>Polls the server for any deploy jobs queued for this agent.</summary>
    Task<List<DeployJob>> GetPendingDeployJobsAsync(string deviceId, CancellationToken ct = default);

    Task PostDeployResultAsync(string deviceId, DeployJobResult result, CancellationToken ct = default);
}