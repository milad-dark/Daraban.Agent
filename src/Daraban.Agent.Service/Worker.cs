using Daraban.Agent.Core.Agents;
using Daraban.Agent.Core.Config;
using Microsoft.Extensions.Options;

namespace Daraban.Agent.Service;

/// <summary>
/// Was previously: a heartbeat that logged once a second and never touched IAgentTask,
/// so the Windows service / system unit never actually collected or sent anything.
/// Now delegates all scheduling to AgentRunner (shared with the CLI's daemon mode).
/// </summary>
public class Worker(IEnumerable<IAgentTask> tasks, AgentStatusTracker status, IOptions<AgentOptions> options, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runner = new AgentRunner(tasks, status);
        logger.LogInformation("Agent service starting. Tasks: {Tasks}", string.Join(", ", AgentRunner.ResolveTaskNames(options.Value)));

        // Serve staged deploy files to same-subnet peers (P2P deploy sharing) and
        // announce/listen for peers. Started here (not in Program.cs) so the host's
        // graceful shutdown also tears the P2P surface down.
        P2pAnnouncer? p2pAnnouncer = null;
        if (!options.Value.NoP2p)
        {
            P2pClient.ConfigurePort(options.Value.P2pPort);
            P2pServer.Start(options.Value);
            p2pAnnouncer = new P2pAnnouncer(options.Value);
            p2pAnnouncer.Start();
        }

        try
        {
            await runner.RunForeverAsync(options.Value, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent scheduler crashed unexpectedly.");
            throw;
        }
        finally
        {
            p2pAnnouncer?.Dispose();
        }

        logger.LogInformation("Agent service stopping.");
    }
}
