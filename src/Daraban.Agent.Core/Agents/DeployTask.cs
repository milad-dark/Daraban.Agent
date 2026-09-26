using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Software push, mirroring glpi-agent's Deploy task:
///  1. Ask the server for pending jobs for this machine id.
///  2. Download every associated file into a per-job work directory — first from
///     same-subnet peers that already staged the file (P2P, unless <see cref="AgentOptions.NoP2p"/>),
///     falling back to the central server. This keeps a fleet from saturating the
///     server's bandwidth on popular packages.
///  3. Verify each file's SHA-256 against the manifest before running anything
///     (refuses to execute if a checksum doesn't match — this is the integrity
///     gate that makes remote software push safe to leave switched on).
///  4. Run the job's install command and capture its exit code/output.
///  5. Report the outcome back to the server.
/// Only runs against jobs your own GLPI server queued — it does not accept or
/// execute anything that wasn't in the signed job manifest.
/// </summary>
public sealed class DeployTask : IAgentTask
{
    public string Name => "deploy";

    // Tracks which deploy root directory the P2P registry currently indexes, so switching
    // DeployWorkDir between runs never serves files from a wiped temp folder.
    private static string? _p2pIndexedDeployRoot;

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        if (options.Servers.Count == 0)
        {
            Console.WriteLine("[deploy] Deploy requires --server (jobs and results are exchanged with GLPI); skipped.");
            return;
        }

        // The P2P port is a fleet-wide fact: what we ask peers for must match what
        // P2pServer.Start binds on them. Static client + options-driven server stay in sync here.
        if (!options.NoP2p && options.P2pPort > 0)
            P2pClient.ConfigurePort(options.P2pPort);

        var deviceId = options.Tag ?? Environment.MachineName;
        var clients = DarabanClientFactory.CreateAll(options);

        foreach (var client in clients)
        {
            var jobs = await client.GetPendingDeployJobsAsync(deviceId, ct);
            if (jobs.Count == 0)
            {
                Console.WriteLine($"[deploy] No pending jobs from {client.ServerUrl}.");
                continue;
            }

            foreach (var job in jobs)
            {
                ct.ThrowIfCancellationRequested();
                var result = await RunJobAsync(job, options, ct);
                await client.PostDeployResultAsync(deviceId, result, ct);
                Console.WriteLine($"[deploy] Job '{job.Name}' ({job.JobId}) => {result.Status}: {result.Message}");
            }
        }
    }

    private static async Task<DeployJobResult> RunJobAsync(Models.DeployJob job, AgentOptions options, CancellationToken ct)
    {
        var deployRoot = Path.Combine(
            string.IsNullOrWhiteSpace(options.DeployWorkDir) ? Path.GetTempPath() : options.DeployWorkDir,
            "daraban-deploy");
        var workDir = Path.Combine(deployRoot, job.JobId);
        Directory.CreateDirectory(workDir);

        // If the deploy root moved (config change, temp cleanup), the hash index points at
        // files that no longer exist — drop it so we never serve stale paths.
        if (!options.NoP2p)
        {
            var indexedRoot = _p2pIndexedDeployRoot;
            if (indexedRoot is not null &&
                !string.Equals(indexedRoot, deployRoot, StringComparison.OrdinalIgnoreCase))
            {
                P2pRegistry.Clear();
            }
            _p2pIndexedDeployRoot = deployRoot;
        }

        using var http = new HttpClient();

        // 1. Download + verify every file before touching the install command.
        foreach (var file in job.Files)
        {
            ct.ThrowIfCancellationRequested();
            var destPath = Path.Combine(workDir, file.FileName);

            // 1a. P2P first: same-subnet peers that already verified and staged this exact
            //     content (hash-keyed) serve it to us, keeping the server out of the hot path.
            var cameFromPeer = false;
            if (!options.NoP2p && options.P2pPort > 0 && !string.IsNullOrWhiteSpace(file.Sha256))
            {
                var peers = P2pClient.GetCandidatePeers();
                if (peers.Count > 0)
                {
                    var servedBy = await P2pClient.TryDownloadAsync(
                        file.Sha256, destPath, peers, P2pClient.DefaultTimeoutMs, ct);
                    if (servedBy is not null)
                    {
                        cameFromPeer = true;
                        Console.WriteLine($"[deploy] {file.FileName}: downloaded from peer {servedBy} (server bypassed).");
                    }
                }
            }

            // 1b. Fall back to the central server when no peer had the file.
            if (!cameFromPeer)
            {
                try
                {
                    await using var stream = await http.GetStreamAsync(file.Url, ct);
                    await using var fs = File.Create(destPath);
                    await stream.CopyToAsync(fs, ct);
                }
                catch (Exception ex)
                {
                    return new DeployJobResult
                    {
                        JobId = job.JobId,
                        Status = DeployStatus.Failed,
                        Message = $"Download failed for {file.FileName}: {ex.Message}"
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(file.Sha256))
            {
                var actual = await ComputeSha256Async(destPath, ct);
                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    // A peer that served bad content must never serve it again — the client
                    // already rejected its bytes; drop any registry entry pointing at them.
                    if (cameFromPeer)
                        P2pRegistry.Forget(file.Sha256);

                    return new DeployJobResult
                    {
                        JobId = job.JobId,
                        Status = DeployStatus.ChecksumFailed,
                        Message = $"Checksum mismatch for {file.FileName}: expected {file.Sha256}, got {actual}"
                    };
                }

                // Verified content becomes shareable to same-subnet peers (P2P seed).
                if (!options.NoP2p)
                    P2pRegistry.Register(file.Sha256, destPath);
            }
        }

        // 2. Run the install command from inside the job's work directory so relative paths resolve.
        if (string.IsNullOrWhiteSpace(job.InstallCommand))
        {
            return new DeployJobResult { JobId = job.JobId, Status = DeployStatus.Success, Message = "Files staged; no install command specified." };
        }

        try
        {
            var (exitCode, output) = await RunInstallCommandAsync(job.InstallCommand, workDir, job.TimeoutSeconds, ct);
            return new DeployJobResult
            {
                JobId = job.JobId,
                Status = exitCode == 0 ? DeployStatus.Success : DeployStatus.Failed,
                ExitCode = exitCode,
                Message = output.Length > 4000 ? output[..4000] + "...(truncated)" : output
            };
        }
        catch (Exception ex)
        {
            return new DeployJobResult { JobId = job.JobId, Status = DeployStatus.Failed, Message = $"Install command error: {ex.Message}" };
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<(int ExitCode, string Output)> RunInstallCommandAsync(string command, string workDir, int timeoutSeconds, CancellationToken ct)
    {
        // Splits "shell" and "-c/args" per OS so the same job manifest (e.g. "msiexec /i app.msi /quiet"
        // on Windows, "dpkg -i app.deb" on Linux, "installer -pkg app.pkg -target /" on macOS) just works.
        var (fileName, argsPrefix) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c ")
            : ("/bin/sh", "-c ");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = argsPrefix + command,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(true);
            return (-1, $"Install command timed out after {timeoutSeconds}s.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, string.Join('\n', new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))));
    }
}
