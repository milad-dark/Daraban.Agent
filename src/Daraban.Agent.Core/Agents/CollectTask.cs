using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Tools;
using Daraban.Agent.Core.Transport;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Daraban.Agent.Core.Agents;

public sealed class CollectTask : IAgentTask
{
    public string Name => "collect";

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        Console.WriteLine("[collect] Starting collect task");

        // 1. Load jobs from local file or server
        var jobs = await FetchJobsAsync(options, ct);
        if (jobs.Count == 0)
        {
            Console.WriteLine("[collect] No collect jobs found");
            return;
        }

        Console.WriteLine($"[collect] Running {jobs.Count} job(s)");

        // 2. Run jobs — no constructor injection, collector created directly
        var collector = new CollectCollector();
        var results = new ConcurrentBag<CollectResult>();

        var semaphore = new SemaphoreSlim(Math.Max(1, options.Threads));

        await Parallel.ForEachAsync(jobs, ct, async (job, innerCt) =>
        {
            await semaphore.WaitAsync(innerCt);
            try
            {
                var result = await collector.RunAsync(job, innerCt);
                results.Add(result);
                Console.WriteLine(result.Success
                    ? $"[collect] Job {job.JobId}: ok — {result.Value?.Truncate(80)}"
                    : $"[collect] Job {job.JobId}: error — {result.Error}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        // 3. Post results or write local
        var client = DarabanClientFactory.Create(options);
        if (client is not null)
        {
            await client.PostCollectResultsAsync(results.ToList(), ct);
        }
        else
        {
            await WriteLocalAsync(results.ToList(), options, ct);
        }

        Console.WriteLine($"[collect] Done — {results.Count} result(s)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<List<CollectJob>> FetchJobsAsync(AgentOptions options, CancellationToken ct)
    {
        try
        {
            // Local mode — read collect-jobs.json from --local dir
            if (!string.IsNullOrWhiteSpace(options.Local))
            {
                var path = Path.Combine(options.Local, "collect-jobs.json");
                if (!File.Exists(path))
                {
                    Console.WriteLine($"[collect] No collect-jobs.json found in {options.Local}");
                    return [];
                }

                var json = await File.ReadAllTextAsync(path, ct);
                return JsonSerializer.Deserialize<List<CollectJob>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }

            // Server mode — fetch from server
            var client = DarabanClientFactory.Create(options);
            if (client is null)
                return [];

            return await client.GetCollectJobsAsync(ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return [];
        }
    }

    private static async Task WriteLocalAsync(
        List<CollectResult> results,
        AgentOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Local))
            return;

        Directory.CreateDirectory(options.Local);

        var file = Path.Combine(options.Local,
            $"collect-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");

        await File.WriteAllTextAsync(
            file,
            JsonSerializer.Serialize(results,
                new JsonSerializerOptions { WriteIndented = true }),
            ct);

        Console.WriteLine($"[collect] Results written to {file}");
    }
}