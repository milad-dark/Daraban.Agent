using Daraban.Agent.Core.Config;

namespace Daraban.Agent.Core.Transport;

/// <summary>
/// Centralizes multi-target delivery (mirrors glpi-agent's comma-separated `server`):
/// results are posted to every configured server, and one failing target never blocks
/// the others. Falls back to a plain single-target call when only one server is set.
/// </summary>
public static class MultiTargetDelivery
{
    /// <summary>
    /// Runs <paramref name="send"/> against every configured server. The delegate receives
    /// a client already bound to that server. Failures are logged, not thrown, so a
    /// single unreachable target cannot prevent delivery to the rest.
    /// </summary>
    public static async Task ForEachServerAsync(
        AgentOptions options,
        Func<DarabanClient, Task> send,
        CancellationToken ct)
    {
        foreach (var client in DarabanClientFactory.CreateAll(options))
        {
            try
            {
                await send(client);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[transport] Delivery to {client.ServerUrl} failed: {ex.Message}");
            }
        }
    }
}