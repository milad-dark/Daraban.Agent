using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Transport;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// The piece that was missing from Daraban.Agent.Service: something that actually calls
/// the registered tasks on a schedule. Worker.cs previously just logged a heartbeat every
/// second and never touched IAgentTask at all. Both the CLI (daemon/loop mode) and the
/// Service now delegate to this class so the scheduling logic exists in exactly one place.
/// </summary>
public sealed class AgentRunner(IEnumerable<IAgentTask> tasks, AgentStatusTracker status)
{
    private readonly List<IAgentTask> _tasks = [.. tasks];

    /// <summary>Runs the configured tasks immediately, then again every DelayTimeSeconds, until cancelled.</summary>
    public async Task RunForeverAsync(AgentOptions options, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.DelayTimeSeconds));
        Console.WriteLine($"[agent] Scheduler starting: every {interval.TotalSeconds:0}s, tasks=[{string.Join(", ", ResolveTaskNames(options))}]");

        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunOnceAsync(options, ct);
        }
        while (!ct.IsCancellationRequested && await WaitNextTickAsync(timer, options, ct));
    }

    /// <summary>Runs the prolog handshake (if a server is configured) followed by every selected task, once.</summary>
    public async Task RunOnceAsync(AgentOptions options, CancellationToken ct)
    {
        var deviceId = options.Tag ?? Environment.MachineName;

        if (!string.IsNullOrWhiteSpace(options.Server))
        {
            try
            {
                var client = DarabanClientFactory.Create(options);
                var config = await client.PrologAsync(deviceId, ct);
                if (!string.IsNullOrWhiteSpace(config))
                    Console.WriteLine($"[agent] Prolog config from server: {config}");
            }
            catch (Exception ex)
            {
                // A failed prolog shouldn't block the run — glpi-agent falls back to its
                // local schedule too when the server is briefly unreachable.
                Console.WriteLine($"[agent] Prolog handshake failed (continuing with local config): {ex.Message}");
            }
        }

        foreach (var name in ResolveTaskNames(options))
        {
            var task = _tasks.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (task is null)
            {
                Console.Error.WriteLine($"[agent] Unknown task '{name}' — skipped. Known tasks: {string.Join(", ", _tasks.Select(t => t.Name))}");
                continue;
            }

            status.SetRunning(name);
            Console.WriteLine($"[agent] Running task: {name}");
            try
            {
                await task.RunAsync(options, ct);
                status.RecordResult(name, success: true);
            }
            catch (OperationCanceledException)
            {
                throw; // shutdown in progress — don't record this as a task failure
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[agent] Task '{name}' failed: {ex.Message}");
                status.RecordResult(name, success: false, ex.Message);
            }
        }

        status.SetIdle();
    }

    /// <summary>Which task names apply this run: --tasks if given, else just "local", minus anything in --no-task.</summary>
    public static List<string> ResolveTaskNames(AgentOptions options)
    {
        var selected = options.Tasks.Count > 0 ? options.Tasks : new List<string> { "local" };
        return selected
            .Where(t => !options.NoTasks.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<bool> WaitNextTickAsync(PeriodicTimer timer, AgentOptions options, CancellationToken ct)
    {
        try
        {
            // "Lazy" mirrors daraban-agent's --lazy: add a small random jitter so a fleet of
            // agents restarted at the same time (e.g. after a patch reboot wave) doesn't
            // all hit the server in the same second.
            if (options.Lazy)
            {
                var jitterMs = Random.Shared.Next(0, 30_000);
                await Task.Delay(jitterMs, ct);
            }
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
