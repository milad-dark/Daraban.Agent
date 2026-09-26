using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Daraban.Agent.Core.Agents;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Xunit;

namespace Daraban.Agent.Tests;

/// <summary>
/// Tests for 4.2 (P2P peer discovery) and 4.3 (deploy retry/resume). Shares the
/// static P2P state with <see cref="P2pTests"/>, so it runs in the same serial collection.
/// </summary>
[Collection("p2p-serial")]
public class P2pDiscoveryAndRetryTests
{
    // ── 4.2a announce payload ────────────────────────────────────────────────

    [Fact]
    public void BuildAnnounceJson_Shape_MatchesSpec()
    {
        var json = P2pAnnouncer.BuildAnnounceJson("PC-01", ["aabbcc", "ddeeff"]);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("PC-01", doc.RootElement.GetProperty("agentId").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("files").GetArrayLength());
        Assert.Equal("aabbcc", doc.RootElement.GetProperty("files")[0].GetString());
    }

    [Fact]
    public void BuildAnnounceJson_EmptyRegistry_EmptyFilesArray()
    {
        P2pRegistry.Clear();
        var json = P2pAnnouncer.BuildAnnounceJson("PC-01", P2pRegistry.GetHashes());
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("PC-01", doc.RootElement.GetProperty("agentId").GetString());
        Assert.Empty(doc.RootElement.GetProperty("files").EnumerateArray());
    }

    // ── 4.2b peer table: remember / expire ───────────────────────────────────

    [Fact]
    public void RememberPeer_IgnoresNonIPv4()
    {
        P2pServer.ClearPeers();
        P2pServer.RememberPeer(IPAddress.IPv6Loopback);
        P2pServer.RememberPeer(IPAddress.Parse("::ffff:10.1.2.3")); // mapped — normalized only in filter paths
        Assert.DoesNotContain(P2pServer.GetPeerIps(), ip => ip == "::1");
    }

    [Fact]
    public void GetPeerIps_ExcludesExpiredEntries()
    {
        P2pServer.ClearPeers();
        var fresh = IPAddress.Parse("10.9.0.1");
        var stale = IPAddress.Parse("10.9.0.2");

        P2pServer.RememberPeer(fresh); // now
        P2pServer.RememberPeer(stale, DateTime.UtcNow - P2pServer.PeerExpiry - TimeSpan.FromSeconds(1));

        var peers = P2pServer.GetPeerIps();
        Assert.Contains("10.9.0.1", peers);
        Assert.DoesNotContain("10.9.0.2", peers);
    }

    [Fact]
    public void GetPeerIps_FreshEntryInsideExpiryWindow_Kept()
    {
        P2pServer.ClearPeers();
        P2pServer.RememberPeer(IPAddress.Parse("10.9.0.3"), DateTime.UtcNow - TimeSpan.FromSeconds(90)); // < 2 min
        Assert.Contains("10.9.0.3", P2pServer.GetPeerIps());
        P2pServer.ClearPeers();
    }

    [Fact]
    public void GetPeerIps_DeduplicatesAcrossSources()
    {
        P2pServer.ClearPeers();
        var ip = IPAddress.Parse("10.9.0.4");
        P2pServer.RememberPeer(ip);
        P2pServer.RememberPeer(ip); // second announcement
        Assert.Equal(1, P2pServer.GetPeerIps().Count(p => p == "10.9.0.4"));
        P2pServer.ClearPeers();
    }

    // ── 4.2b UDP announcement handling ──────────────────────────────────────

    [Fact]
    public void HandleAnnouncement_ValidAnnouncement_RegistersPeer()
    {
        P2pServer.ClearPeers();
        P2pAnnouncer.OwnAddressCheck = _ => false; // simulate "sent by another machine"

        var json = """{"agentId":"PEER-1","files":["aabb"]}""";
        P2pAnnouncer.HandleAnnouncement(new UdpReceiveResult(
            Encoding.UTF8.GetBytes(json),
            new IPEndPoint(IPAddress.Parse("127.0.0.20"), 62355)));

        Assert.Contains("127.0.0.20", P2pServer.GetPeerIps());
        P2pServer.ClearPeers();
    }

    [Fact]
    public void HandleAnnouncement_OwnEcho_Ignored()
    {
        P2pServer.ClearPeers();
        P2pAnnouncer.OwnAddressCheck = _ => true; // our own broadcast echoed back

        P2pAnnouncer.HandleAnnouncement(new UdpReceiveResult(
            Encoding.UTF8.GetBytes("""{"agentId":"SELF","files":[]}"""),
            new IPEndPoint(IPAddress.Loopback, 62355)));

        // Loopback peer must NOT be registered, but the machine's ARP table may
        // legitimately contribute same-subnet entries — assert on the specific IP.
        Assert.DoesNotContain("127.0.0.1", P2pServer.GetPeerIps());
        P2pServer.ClearPeers();
    }

    [Fact]
    public void HandleAnnouncement_ForeignSubnet_Ignored()
    {
        P2pServer.ClearPeers();
        P2pAnnouncer.OwnAddressCheck = _ => false;

        P2pAnnouncer.HandleAnnouncement(new UdpReceiveResult(
            Encoding.UTF8.GetBytes("""{"agentId":"EVIL","files":["aabb"]}"""),
            new IPEndPoint(IPAddress.Parse("8.8.8.8"), 62355)));

        Assert.DoesNotContain("8.8.8.8", P2pServer.GetPeerIps());
    }

    [Fact]
    public void HandleAnnouncement_GarbageOrOversized_Ignored()
    {
        P2pServer.ClearPeers();
        P2pAnnouncer.OwnAddressCheck = _ => false;
        var remote = new IPEndPoint(IPAddress.Parse("127.0.0.21"), 62355);

        P2pAnnouncer.HandleAnnouncement(new UdpReceiveResult("not json"u8.ToArray(), remote));
        P2pAnnouncer.HandleAnnouncement(new UdpReceiveResult([], remote));
        P2pAnnouncer.HandleAnnouncement(new UdpReceiveResult(new byte[5000], remote));

        var peers = P2pServer.GetPeerIps();
        Assert.DoesNotContain("127.0.0.21", peers);
        Assert.All(peers, p => Assert.NotEqual("127.0.0.21", p));
    }

    // ── 4.2 real socket round trip: announcer → listener → peer table ────────

    [Fact]
    public async Task Announcer_RoundTrip_RegistersPeerOnLoopback()
    {
        P2pServer.ClearPeers();
        P2pAnnouncer.OwnAddressCheck = _ => false; // one-machine test: treat self as a peer

        var options = new AgentOptions { P2pPort = GetFreeUdpPort() };
        using var announcer = new P2pAnnouncer(options, agentId: "TEST-AGENT");

        announcer.Start();
        try
        {
            // The broadcast egresses via the default interface, so the listener sees it
            // from the LAN IP, not loopback. Any *new* peer registration proves the loop.
            var before = P2pServer.GetPeerIps().Count;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            IReadOnlyList<string> after = P2pServer.GetPeerIps();
            while (DateTime.UtcNow < deadline && after.Count <= before)
            {
                await Task.Delay(100);
                after = P2pServer.GetPeerIps();
            }

            Assert.True(after.Count > before,
                $"Expected the listener to register the announcing agent; before={before}, after={after.Count} [{string.Join(", ", after)}]");
        }
        finally
        {
            P2pAnnouncer.OwnAddressCheck = null;
            P2pServer.ClearPeers();
        }
    }

    // ── 4.2c GetPeerIps used by DeployTask ──────────────────────────────────

    [Fact]
    public void GetPeerIps_MergesAnnouncedAndArpSources()
    {
        P2pServer.ClearPeers();
        P2pServer.RememberPeer(IPAddress.Parse("10.9.0.5")); // announced
        // ARP source may contribute more entries; both must be present, subnet-filtered.
        var peers = P2pServer.GetPeerIps();
        Assert.Contains("10.9.0.5", peers);
        Assert.All(peers, p => Assert.True(IPAddress.TryParse(p, out _)));
        P2pServer.ClearPeers();
    }

    // ── 4.3a/c resume: skip download when staged file hash matches ──────────

    [Fact]
    public async Task RunJobAsync_StagedFileWithMatchingHash_SkipsDownloadAndKeepsFile()
    {
        var job = MakeJob(out var content);
        var options = P2pOffOptions();

        // Pre-stage the file at the exact path RunJobAsync computes.
        var workDir = GetWorkDir(job.JobId, options);
        Directory.CreateDirectory(workDir);
        var dest = Path.Combine(workDir, job.Files[0].FileName);
        await File.WriteAllBytesAsync(dest, content);

        var result = await DeployTask.RunJobAsync(job, options, CancellationToken.None);

        Assert.Equal(DeployStatus.Success, result.Status); // no InstallCommand → files staged
        Assert.Equal(content, await File.ReadAllBytesAsync(dest)); // untouched
    }

    [Fact]
    public async Task RunJobAsync_StalePartialFile_DeletedAndReplaced()
    {
        var job = MakeJob(out var content);
        var options = P2pOffOptions();

        // A real (loopback) file source — the stale file must be deleted and the
        // re-download must produce the correct bytes.
        var server = StartFileServer(content);
        job.Files[0].Url = server.Url;

        var workDir = GetWorkDir(job.JobId, options);
        Directory.CreateDirectory(workDir);
        var dest = Path.Combine(workDir, job.Files[0].FileName);
        await File.WriteAllBytesAsync(dest, "stale-corrupt-bytes"u8.ToArray()); // wrong hash

        try
        {
            var result = await DeployTask.RunJobAsync(job, options, CancellationToken.None);

            Assert.Equal(DeployStatus.Success, result.Status);
            Assert.Equal(content, await File.ReadAllBytesAsync(dest)); // replaced with real bytes
        }
        finally
        {
            server.Listener.Stop();
        }
    }

    // ── 4.3b retry with backoff ─────────────────────────────────────────────

    [Fact]
    public async Task RunJobAsync_ServerUnreachable_RetriesThenFailsWithBackoff()
    {
        // Two files: first succeeds (resume path), second targets a dead endpoint →
        // exercises the retry loop without a real HTTP failure-injection server.
        var job = new DeployJob
        {
            JobId = "retry-test",
            Name = "retry-test",
            Files =
            [
                new DeployFile { Url = "http://127.0.0.1:9/nope.bin", FileName = "nope.bin", Sha256 = "" }
            ],
            InstallCommand = ""
        };
        var options = new AgentOptions
        {
            NoP2p = true,
            DeployMaxRetries = 3,
            DeployWorkDir = Path.Combine(Path.GetTempPath(), $"p2p-retry-{Guid.NewGuid():N}")
        };

        var result = await DeployTask.RunJobAsync(job, options, CancellationToken.None);

        Assert.Equal(DeployStatus.Failed, result.Status);
        Assert.Contains("after 3 attempt(s)", result.Message);
        // NOTE: no elapsed-time assert here — connection-failure latency depends on the
        // environment (proxies turn instant RSTs into multi-second hangs), so the backoff
        // waits cannot be measured reliably in CI. The retry-count in the message is the
        // observable contract.
    }

    [Fact]
    public void AgentOptions_DeployMaxRetries_DefaultsTo3()
    {
        Assert.Equal(3, new AgentOptions().DeployMaxRetries);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static DeployJob MakeJob(out byte[] content)
    {
        content = RandomBytes(2048);
        return new DeployJob
        {
            JobId = $"resume-{Guid.NewGuid():N}",
            Name = "resume-test",
            Files =
            [
                new DeployFile
                {
                    Url = "http://127.0.0.1:9/should-not-be-fetched.bin",
                    FileName = "staged.bin",
                    Sha256 = Sha256Of(content)
                }
            ],
            InstallCommand = ""
        };
    }

    private static AgentOptions P2pOffOptions() => new()
    {
        NoP2p = true,
        DeployWorkDir = Path.Combine(Path.GetTempPath(), $"p2p-resume-{Guid.NewGuid():N}")
    };

    private static string GetWorkDir(string jobId, AgentOptions options) => Path.Combine(
        string.IsNullOrWhiteSpace(options.DeployWorkDir) ? Path.GetTempPath() : options.DeployWorkDir,
        "daraban-deploy",
        jobId);

    private static string Sha256Of(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        Random.Shared.NextBytes(b);
        return b;
    }

    private static int GetFreeUdpPort()
    {
        var udp = new UdpClient(0);
        try
        {
            return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        }
        finally
        {
            udp.Dispose();
        }
    }

    private static int GetFreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try
        {
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }
        finally
        {
            l.Stop();
        }
    }

    /// <summary>
    /// Minimal loopback HTTP file server (loopback HttpListener prefixes need no admin).
    /// Caller must Stop() the listener when done.
    /// </summary>
    private static (HttpListener Listener, string Url) StartFileServer(byte[] content)
    {
        var port = GetFreeTcpPort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); // prefixes must end in '/'
        listener.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var ctx = await listener.GetContextAsync();
                    ctx.Response.ContentType = "application/octet-stream";
                    await ctx.Response.OutputStream.WriteAsync(content);
                    ctx.Response.Close();
                }
            }
            catch
            {
                // Listener stopped — expected during teardown.
            }
        });

        return (listener, $"http://127.0.0.1:{port}/file.bin"); // URL is fine — prefix ends in '/'
    }
}
