using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Daraban.Agent.Core.Agents;
using Daraban.Agent.Core.Config;
using Xunit;

namespace Daraban.Agent.Tests;

/// <summary>
/// P2P deploy sharing tests. The server, client and registry all hold static state,
/// so every test here lives in the same collection and runs serially.
/// </summary>
[Collection("p2p-serial")]
public class P2pTests
{
    // ── P2pRegistry ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_ExistingFile_IndexesByLowercaseHash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"p2p-reg-{Guid.NewGuid():N}.bin");
        var content = RandomBytes(128);
        await File.WriteAllBytesAsync(path, content);
        try
        {
            var sha = Sha256Of(content);

            Assert.True(P2pRegistry.Register(sha, path));
            Assert.True(P2pRegistry.TryGetPath(sha.ToUpperInvariant(), out var resolved)); // case-insensitive
            Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(resolved!));

            P2pRegistry.Forget(sha);
            Assert.False(P2pRegistry.TryGetPath(sha, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Register_MissingFile_ReturnsFalse()
    {
        P2pRegistry.Clear();
        Assert.False(P2pRegistry.Register("aabb", Path.Combine(Path.GetTempPath(), "does-not-exist.bin")));
        Assert.False(P2pRegistry.TryGetPath("aabb", out _));
    }

    [Fact]
    public void Register_EmptyHash_ReturnsFalse()
    {
        P2pRegistry.Clear();
        Assert.False(P2pRegistry.Register("", "anything"));
    }

    // ── P2pClient ────────────────────────────────────────────────────────────

    [Fact]
    public void RememberPeers_DeduplicatesAndNormalizes()
    {
        P2pClient.ForgetPeers();
        P2pClient.RememberPeers(["10.0.0.5", "10.0.0.5", "10.0.0.7", "not-an-ip", "::1", ""]);

        Assert.Equal(["10.0.0.5", "10.0.0.7"], P2pClient.PeekPeers());
        P2pClient.ForgetPeers();
    }

    [Fact]
    public void RememberPeers_CapsCacheSize()
    {
        P2pClient.ForgetPeers();
        P2pClient.RememberPeers(Enumerable.Range(1, 100).Select(i => $"10.0.{i / 256}.{i % 256}"), maxPeers: 20);
        Assert.Equal(20, P2pClient.PeekPeers().Count);
        P2pClient.ForgetPeers();
    }

    [Fact]
    public async Task TryDownloadAsync_NullOrEmptyInputs_ReturnsNull()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"p2p-dl-{Guid.NewGuid():N}.bin");
        Assert.Null(await P2pClient.TryDownloadAsync(null!, dest, ["127.0.0.1"], 1000, CancellationToken.None));
        Assert.Null(await P2pClient.TryDownloadAsync("abc", dest, null, 1000, CancellationToken.None));
        Assert.Null(await P2pClient.TryDownloadAsync("abc", dest, [], 1000, CancellationToken.None));
    }

    [Fact]
    public async Task TryDownloadAsync_NoPeerHasFile_ReturnsNullAndLeavesNoFile()
    {
        P2pClient.ForgetPeers();
        var dest = Path.Combine(Path.GetTempPath(), $"p2p-dl-{Guid.NewGuid():N}.bin");
        var peers = new List<string> { "127.0.0.1", "127.0.0.2" }; // loopback passes the subnet filter

        var result = await P2pClient.TryDownloadAsync(Sha256Of("nobody-has-this"), dest, peers, 500, CancellationToken.None);

        Assert.Null(result);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".p2p-part"));
    }

    [Fact]
    public async Task TryDownloadAsync_PeerServesWrongContent_RejectsAndCleansUp()
    {
        // The server serves whatever the registry maps — the integrity gate is the client's
        // hash check. Register a file whose bytes hash to something OTHER than what we ask
        // for, simulating a peer that lies about its content.
        var port = GetFreePort();
        var options = new AgentOptions { P2pPort = port };
        P2pClient.ConfigurePort(port);

        var wrongContent = RandomBytes(256);
        var staged = Path.Combine(Path.GetTempPath(), $"p2p-corrupt-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(staged, wrongContent);

        var requestedHash = Sha256Of("expected-content");
        P2pRegistry.Register(requestedHash, staged); // registry maps hash → path; bytes differ

        P2pServer.Start(options);
        try
        {
            var dest = Path.Combine(Path.GetTempPath(), $"p2p-dl-{Guid.NewGuid():N}.bin");

            var result = await P2pClient.TryDownloadAsync(requestedHash, dest, ["127.0.0.1"], 2000, CancellationToken.None);

            Assert.Null(result); // hash mismatch → rejected
            Assert.False(File.Exists(dest));
            Assert.False(File.Exists(dest + ".p2p-part"));
        }
        finally
        {
            await P2pServer.StopAsync();
            P2pRegistry.Clear();
            File.Delete(staged);
        }
    }

    // ── Subnet filter ────────────────────────────────────────────────────────

    [Fact]
    public void IsSameSubnet_LoopbackV4Passes()
    {
        Assert.True(P2pClient.IsSameSubnet(IPAddress.Loopback));
        Assert.True(P2pClient.IsSameSubnet(IPAddress.Parse("127.0.0.2")));
    }

    [Fact]
    public void IsSameSubnet_IPv6AndForeignAddressesRejected()
    {
        // Peers are discovered as IPv4 only; v6 and public addresses are outside the trust boundary.
        Assert.False(P2pClient.IsSameSubnet(IPAddress.IPv6Loopback));
        Assert.False(P2pClient.IsSameSubnet(IPAddress.Parse("8.8.8.8")));
        Assert.False(P2pClient.IsSameSubnet(null));
    }

    // ── Full server round trip ───────────────────────────────────────────────

    [Fact]
    public async Task P2pServer_ServesRegisteredFileToSameSubnetPeer()
    {
        var port = GetFreePort();
        var options = new AgentOptions { P2pPort = port };
        P2pClient.ConfigurePort(port);

        var content = RandomBytes(4096);
        var sha = Sha256Of(content);
        var staged = Path.Combine(Path.GetTempPath(), $"p2p-serve-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(staged, content);
        P2pRegistry.Register(sha, staged);

        P2pServer.Start(options);
        try
        {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync($"http://127.0.0.1:{port}/{sha.ToLowerInvariant()}");

            Assert.Equal(content, bytes);
        }
        finally
        {
            await P2pServer.StopAsync();
            P2pRegistry.Clear();
            File.Delete(staged);
        }
    }

    [Fact]
    public async Task P2pServer_UnknownHash_Returns404()
    {
        var port = GetFreePort();
        var options = new AgentOptions { P2pPort = port };
        P2pClient.ConfigurePort(port);

        P2pServer.Start(options);
        try
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/{Sha256Of("missing").ToLowerInvariant()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("404", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await P2pServer.StopAsync();
        }
    }

    [Fact]
    public async Task P2pServer_StagedFileDeletedOnDisk_Serves404AndDropsEntry()
    {
        var port = GetFreePort();
        var options = new AgentOptions { P2pPort = port };
        P2pClient.ConfigurePort(port);

        var sha = Sha256Of("will-be-deleted");
        var staged = Path.Combine(Path.GetTempPath(), $"p2p-gone-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(staged, "will-be-deleted"u8.ToArray());
        P2pRegistry.Register(sha, staged);
        File.Delete(staged); // staged file vanishes (temp cleanup), registry entry remains

        P2pServer.Start(options);
        try
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/{sha.ToLowerInvariant()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(P2pRegistry.TryGetPath(sha, out _)); // entry dropped after the miss
        }
        finally
        {
            await P2pServer.StopAsync();
            P2pRegistry.Clear();
        }
    }

    // ── AgentOptions wiring ──────────────────────────────────────────────────

    [Fact]
    public void AgentOptions_P2pDefaults_MirrorGlpiAgent()
    {
        var options = new AgentOptions();
        Assert.False(options.NoP2p);
        Assert.Equal(62355, options.P2pPort);
        Assert.Equal(4, options.P2pMaxConcurrent);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Sha256Of(string s) => Sha256Of(System.Text.Encoding.UTF8.GetBytes(s));

    private static string Sha256Of(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        Random.Shared.NextBytes(b);
        return b;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
