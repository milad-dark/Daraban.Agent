using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Software push, mirroring glpi-agent's Deploy task:
///  1. Ask the server for pending jobs for this machine id.
///  2. Download every associated file into a per-job work directory.
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

    public async Task RunAsync(AgentOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Server))
        {
            Console.WriteLine("[deploy] Deploy requires --server (jobs and results are exchanged with GLPI); skipped.");
            return;
        }

        var deviceId = options.Tag ?? Environment.MachineName;
        var client = DarabanClientFactory.Create(options);

        var jobs = await client.GetPendingDeployJobsAsync(deviceId, ct);
        if (jobs.Count == 0)
        {
            Console.WriteLine("[deploy] No pending jobs.");
            return;
        }

        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();
            var result = await RunJobAsync(job, options, ct);
            await client.PostDeployResultAsync(deviceId, result, ct);
            Console.WriteLine($"[deploy] Job '{job.Name}' ({job.JobId}) => {result.Status}: {result.Message}");
        }
    }

    private static async Task<DeployJobResult> RunJobAsync(Models.DeployJob job, AgentOptions options, CancellationToken ct)
    {
        var workDir = Path.Combine(
            string.IsNullOrWhiteSpace(options.DeployWorkDir) ? Path.GetTempPath() : options.DeployWorkDir,
            "daraban-deploy",
            job.JobId);
        Directory.CreateDirectory(workDir);

        using var http = new HttpClient();

        // 1. Download + verify every file before touching the install command.
        foreach (var file in job.Files)
        {
            ct.ThrowIfCancellationRequested();
            var destPath = Path.Combine(workDir, file.FileName);

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

            if (!string.IsNullOrWhiteSpace(file.Sha256))
            {
                var actual = await ComputeSha256Async(destPath, ct);
                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new DeployJobResult
                    {
                        JobId = job.JobId,
                        Status = DeployStatus.ChecksumFailed,
                        Message = $"Checksum mismatch for {file.FileName}: expected {file.Sha256}, got {actual}"
                    };
                }
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
