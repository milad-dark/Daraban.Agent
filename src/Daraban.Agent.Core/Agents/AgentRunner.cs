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

    /// <summary>Server-provided delay (seconds) from the last prolog; 0 = use local DelayTimeSeconds.</summary>
    private volatile int _serverDelaySeconds;

    /// <summary>Runs the configured tasks immediately, then again every DelayTimeSeconds (or the server-provided delay), until cancelled.</summary>
    public async Task RunForeverAsync(AgentOptions options, CancellationToken ct)
    {
        Console.WriteLine($"[agent] Scheduler starting: every {Math.Max(5, options.DelayTimeSeconds)}s, tasks=[{string.Join(", ", ResolveTaskNames(options))}]");

        do
        {
            await RunOnceAsync(options, ct);
            if (ct.IsCancellationRequested)
                break;
            var interval = TimeSpan.FromSeconds(Math.Max(5, _serverDelaySeconds > 0 ? _serverDelaySeconds : options.DelayTimeSeconds));
            await WaitNextTickAsync(interval, options, ct);
        }
        while (!ct.IsCancellationRequested);
    }

    /// <summary>Runs the prolog handshake (if a server is configured) followed by every selected task, once.</summary>
    public async Task RunOnceAsync(AgentOptions options, CancellationToken ct)
    {
        var deviceId = options.Tag ?? Environment.MachineName;
        List<string>? prologTasks = null;
        var prologRemotes = new List<string>();

        if (options.Servers.Count > 0)
        {
            foreach (var client in DarabanClientFactory.CreateAll(options))
            {
                try
                {
                    var prolog = await client.PrologAsync(deviceId, ct);
                    if (prolog is null)
                        continue;

                    // First server that answers wins the schedule; later ones still get
                    // their prolog logged but do not override an already-applied one.
                    if (prolog.Tasks is { Count: > 0 } && prologTasks is null)
                        prologTasks = prolog.Tasks;

                    foreach (var target in prolog.Remote)
                    {
                        if (!string.IsNullOrWhiteSpace(target.ToConnectionString()))
                            prologRemotes.Add(target.ToConnectionString());
                    }

                    if (prolog.DelayTime > 0)
                        _serverDelaySeconds = prolog.DelayTime;

                    Console.WriteLine($"[agent] Prolog config from {client.ServerUrl}: " +
                        $"tasks=[{string.Join(",", prolog.Tasks ?? [])}], " +
                        $"remotes=[{string.Join(",", prologRemotes)}]");
                }
                catch (Exception ex)
                {
                    // A failed prolog shouldn't block the run — glpi-agent falls back to its
                    // local schedule too when the server is briefly unreachable.
                    Console.WriteLine($"[agent] Prolog handshake failed for {client.ServerUrl} (continuing with local config): {ex.Message}");
                }
            }
        }

        // Merge server-provided remote targets into this run's remote list so
        // RemoteInventoryTask picks them up.
        if (prologRemotes.Count > 0)
        {
            var known = new HashSet<string>(options.RemoteHosts, StringComparer.OrdinalIgnoreCase);
            options.RemoteHosts.AddRange(prologRemotes.Where(r => known.Add(r)));
        }

        var taskNames = prologTasks ?? ResolveTaskNames(options);

        foreach (var name in taskNames)
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

    private static async Task WaitNextTickAsync(TimeSpan interval, AgentOptions options, CancellationToken ct)
    {
        try
        {
            // "Lazy" mirrors daraban-agent's --lazy: add a small random jitter so a fleet of
            // agents restarted at the same time (e.g. after a patch reboot wave) doesn't
            // all hit the server in the same second.
            if (options.Lazy)
                await Task.Delay(Random.Shared.Next(0, 30_000), ct);

            await Task.Delay(interval, ct);
        }
        catch (OperationCanceledException)
        {
            // swallow — caller checks ct.IsCancellationRequested
        }
    }
}
