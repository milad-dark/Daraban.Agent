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

        logger.LogInformation("Agent service stopping.");
    }
}
