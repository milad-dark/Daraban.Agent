using Daraban.Agent.Core.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Minimal HTTP file server that lets peer agents on the same subnet download deploy
/// files this agent has already staged, instead of every agent pulling from the central
/// server (mirrors glpi-agent's P2P deploy mode).
///
/// Protocol (deliberately trivial so any HTTP client works):
///   GET /{sha256-hex}  → 200 with the file bytes, or 404 if this agent does not have
///                        a staged file with that hash.
///
/// The <see cref="P2pRegistry"/> hash → path index is populated by <see cref="DeployTask"/>
/// as files are staged and verified, so peers can only ever download content that already
/// passed the manifest checksum gate — never arbitrary files from disk.
///
/// Unlike the /status interface (Kestrel, loopback by default), the P2P server binds all
/// interfaces by design: peers are other machines on the subnet. On Windows without admin
/// rights this is still plain Kestrel (no http.sys URL ACL needed, unlike HttpListener's
/// http://+:port/ prefix); a non-admin dev machine gets the same LAN binding.
/// </summary>
public static class P2pServer
{
    private static int _activeUploads;
    private static WebApplication? _app;

    // 4.2b: peers discovered via P2pAnnouncer's UDP announcements (IP → last seen).
    private static readonly ConcurrentDictionary<IPAddress, DateTime> Peers = new();

    /// <summary>Peers not seen within this window are dropped from the peer list.</summary>
    public static readonly TimeSpan PeerExpiry = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Records an announcing peer. Called by <see cref="P2pAnnouncer"/>'s listener for
    /// every validated same-subnet announcement.
    /// </summary>
    public static void RememberPeer(IPAddress ip) => RememberPeer(ip, DateTime.UtcNow);

    /// <summary>
    /// Records a peer with an explicit last-seen timestamp (internal: lets tests
    /// simulate entries that should already be expired).
    /// </summary>
    internal static void RememberPeer(IPAddress ip, DateTime lastSeenUtc)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return;
        Peers[ip] = lastSeenUtc;
    }

    /// <summary>
    /// Drops all remembered peers (tests and subnet-change resets).
    /// </summary>
    internal static void ClearPeers() => Peers.Clear();

    /// <summary>
    /// Current live peers: UDP-announced IPs (expired entries dropped) merged with the
    /// ARP-table candidates from <see cref="P2pClient"/>. Deduplicated, order-stable.
    /// This is the list DeployTask feeds into P2P-first downloads.
    /// </summary>
    public static List<string> GetPeerIps()
    {
        var now = DateTime.UtcNow;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var key in Peers.Keys)
        {
            if (Peers[key] < now - PeerExpiry)
            {
                Peers.TryRemove(key, out _);
                continue;
            }
            var s = key.ToString();
            if (seen.Add(s))
                result.Add(s);
        }

        foreach (var arpPeer in P2pClient.GetCandidatePeers())
        {
            if (seen.Add(arpPeer))
                result.Add(arpPeer);
        }
        return result;
    }

    /// <summary>
    /// Starts the P2P file server on the given port. Returns immediately; the server runs
    /// in the background until <see cref="StopAsync"/>. Safe to call when already running
    /// (a different port is requested, the old instance is stopped first).
    /// </summary>
    /// <param name="options">Agent options; <see cref="AgentOptions.P2pPort"/> and
    /// <see cref="AgentOptions.P2pMaxConcurrent"/> are honored.</param>
    public static void Start(AgentOptions options)
    {
        StopAsync().GetAwaiter().GetResult();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(options.P2pPort));
        // No console logging noise from ASP.NET for what is a background file server.
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.MapGet("/{**sha}", async (HttpContext ctx) =>
        {
            var sha = ctx.Request.RouteValues["sha"] as string ?? string.Empty;

            // 1. Concurrency guard — protects upload bandwidth (glpi-agent p2p-ratio-role).
            if (_activeUploads >= Math.Max(1, options.P2pMaxConcurrent))
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsync("503 BUSY\n");
                return;
            }

            // 2. Only agents in the same /24 subnet may download. This is the P2P trust
            //    boundary: the index must never serve the LAN at large, only local peers.
            if (!P2pClient.IsSameSubnet(ctx.Connection.RemoteIpAddress))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("403 FORBIDDEN\n");
                return;
            }

            // 3. Look the file up by hash — 404 unless we have exactly this content staged.
            if (!P2pRegistry.TryGetPath(sha, out var path) || path is null || !File.Exists(path))
            {
                P2pRegistry.Forget(sha);
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsync("404 NOT_FOUND\n");
                return;
            }

            Interlocked.Increment(ref _activeUploads);
            try
            {
                ctx.Response.ContentType = "application/octet-stream";
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, bufferSize: 64 * 1024, useAsync: true);
                await fs.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
                // Peer went away mid-transfer — nothing to report, the registry entry stays.
            }
            finally
            {
                Interlocked.Decrement(ref _activeUploads);
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine($"[p2p-server] Serving staged deploy files on port {options.P2pPort} " +
                                  $"(max {options.P2pMaxConcurrent} concurrent peers).");
                await _app.RunAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[p2p-server] Failed: {ex.Message}");
                _app = null;
            }
        });
    }

    /// <summary>
    /// Stops the server if running. Called on agent shutdown so the port is released.
    /// </summary>
    public static async Task StopAsync()
    {
        if (_app is null)
            return;

        var app = _app;
        _app = null;
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await app.StopAsync(timeoutCts.Token);
        }
        catch
        {
            // Best-effort shutdown; the process is exiting anyway.
        }
    }
}

/// <summary>
/// Static index of deploy files this agent can share with peers, keyed by SHA-256.
/// Entries are added only after a downloaded file passed the manifest checksum gate,
/// so the P2P surface can never serve unverified content.
/// </summary>
public static class P2pRegistry
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, string> HashToPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a staged, checksum-verified file as shareable.
    /// </summary>
    /// <param name="sha256">Expected lowercase SHA-256 of the file.</param>
    /// <param name="path">Full path of the staged file on disk.</param>
    /// <returns>true if the file exists on disk and was indexed.</returns>
    public static bool Register(string sha256, string path)
    {
        if (string.IsNullOrWhiteSpace(sha256) || !File.Exists(path))
            return false;

        lock (Lock)
            HashToPath[sha256.ToLowerInvariant()] = path;
        return true;
    }

    /// <summary>
    /// Looks up a staged file by hash.
    /// </summary>
    public static bool TryGetPath(string sha256, out string? path)
    {
        lock (Lock)
            return HashToPath.TryGetValue((sha256 ?? string.Empty).ToLowerInvariant(), out path);
    }

    /// <summary>
    /// Removes an index entry (e.g. the staged file disappeared from disk).
    /// </summary>
    public static void Forget(string sha256)
    {
        lock (Lock)
            HashToPath.Remove((sha256 ?? string.Empty).ToLowerInvariant());
    }

    /// <summary>
    /// Clears all entries — used when the deploy work directory changes between runs.
    /// </summary>
    public static void Clear()
    {
        lock (Lock)
            HashToPath.Clear();
    }

    /// <summary>
    /// Snapshot of every shareable hash — the P2pAnnouncer broadcast payload.
    /// </summary>
    public static IReadOnlyList<string> GetHashes()
    {
        lock (Lock)
            return HashToPath.Keys.ToList();
    }
}
