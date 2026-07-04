namespace Daraban.Agent.Core.Transport;

public interface IGlpiClient
{
    Task PostInventoryAsync(string jsonPayload, CancellationToken ct = default);
    Task PostDiscoveryAsync(string jsonPayload, CancellationToken ct = default);
    Task PostNetInventoryAsync(string jsonPayload, CancellationToken ct = default);
}