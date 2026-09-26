using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daraban.Agent.Core.Config;

namespace Daraban.Agent.Core.Agents;

/// <summary>
/// UDP beacon for P2P deploy peer discovery (mirrors glpi-agent's UDP broadcast approach):
/// every <see cref="AnnounceInterval"/> this agent broadcasts a tiny JSON packet on
/// <see cref="AgentOptions.P2pPort"/> telling same-subnet peers which file hashes it can
/// serve. Peers that hear the announcement remember the sender's IP (see
/// <see cref="P2pServer.GetPeerIps"/>), so deploy downloads can go peer-to-peer instead of
/// hammering the central server.
///
/// Wire format (kept minimal on purpose):
///   { "agentId": "PC-01", "files": ["&lt;sha256-hex&gt;", ...] }
///
/// The listener side validates every announcement: it must be valid JSON from a
/// same-subnet sender, with a sane size. Nothing from the payload is trusted beyond the
/// sender's IP — the "files" list is informational (a future optimization can use it to
/// pre-check which peer has which file before connecting).
/// </summary>
public sealed class P2pAnnouncer : IDisposable
{
    /// <summary>
    /// How often announcements are broadcast. glpi-agent pings the subnet on a similar
    /// cadence; 30s keeps peer lists fresh well inside the 2-minute expiry window.
    /// </summary>
    public static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AgentOptions _options;
    private readonly string _agentId;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _announceLoop;
    private Task? _listenLoop;
    private readonly SemaphoreSlim _started = new(1, 1);

    /// <summary>
    /// Creates an announcer/listener pair bound to the given configuration.
    /// </summary>
    /// <param name="options">Agent options supplying <see cref="AgentOptions.P2pPort"/> and <see cref="AgentOptions.AgentId"/>.</param>
    /// <param name="agentId">Identifier broadcast with each announcement (defaults to the configured agent id).</param>
    public P2pAnnouncer(AgentOptions options, string? agentId = null)
    {
        _options = options;
        _agentId = string.IsNullOrWhiteSpace(agentId)
            ? (options.AgentId ?? Environment.MachineName)
            : agentId!;
    }

    /// <summary>
    /// Starts the 30s broadcast loop and the UDP listener that records announcing peers.
    /// Safe to call twice; the second call is a no-op.
    /// </summary>
    public void Start()
    {
        _started.Wait();
        try
        {
            if (_announceLoop is not null)
                return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Outgoing side: broadcast every AnnounceInterval.
            _announceLoop = Task.Run(() => AnnounceLoopAsync(token));

            // Incoming side: hear other agents' announcements.
            _listenLoop = Task.Run(() => ListenLoopAsync(token));
        }
        finally
        {
            _started.Release();
        }
    }

    /// <summary>
    /// Builds the announce payload from the current P2P registry contents.
    /// Internal for testability.
    /// </summary>
    internal static string BuildAnnounceJson(string agentId, IReadOnlyCollection<string> hashes)
    {
        var payload = new AnnouncePayload
        {
            AgentId = agentId,
            Files = hashes
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        // Broadcast socket: SO_BROADCAST is required for 255.255.255.255 on most stacks.
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        var target = new IPEndPoint(IPAddress.Broadcast, _options.P2pPort);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var hashes = P2pRegistry.GetHashes();
                var json = BuildAnnounceJson(_agentId, hashes);
                var bytes = Encoding.UTF8.GetBytes(json);
                await udp.SendAsync(bytes, bytes.Length, target);

                Console.WriteLine($"[p2p-announce] Announced {hashes.Count} shareable file(s) to {target}.");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                // Network down, no default route, ICMP blocked — retry next interval quietly.
                Console.WriteLine($"[p2p-announce] Broadcast failed: {ex.SocketErrorCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[p2p-announce] Announce error: {ex.Message}");
            }

            try
            {
                await Task.Delay(AnnounceInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Bind after Start() so we can react to a port change without restart.
                _listener ??= new UdpClient(_options.P2pPort);

                while (!ct.IsCancellationRequested)
                {
                    var result = await _listener.ReceiveAsync(ct);
                    HandleAnnouncement(result);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                // Another agent instance on this machine owns the port (common in dev);
                // listening is optional, broadcasting still works.
                Console.WriteLine($"[p2p-announce] UDP port {_options.P2pPort} busy; another local agent is listening. Disabling listener.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[p2p-announce] Listener error: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _listener?.Dispose();
        _listener = null;
        Console.WriteLine("[p2p-announce] Listener stopped.");
    }

    /// <summary>
    /// Test seam: when set, replaces <see cref="P2pClient.IsOwnAddress"/> in announcement
    /// validation — lets unit tests simulate announcements arriving from another machine's
    /// loopback address (on one machine, every real UDP send is "self").
    /// </summary>
    internal static Func<IPAddress, bool>? OwnAddressCheck { get; set; }

    private static bool IsSelf(IPAddress address) =>
        OwnAddressCheck is not null ? OwnAddressCheck(address) : P2pClient.IsOwnAddress(address);

    /// <summary>
    /// Validates an announcement and registers the sender as a peer.
    /// Internal so tests can feed crafted <see cref="UdpReceiveResult"/>s without real sockets.
    /// </summary>
    internal static void HandleAnnouncement(UdpReceiveResult result)
    {
        // Our own broadcast echoes back to local listeners — we are not our own peer.
        if (IsSelf(result.RemoteEndPoint.Address))
            return;

        // Trust boundary: only same-subnet senders may register as peers — the broadcast
        // could otherwise be used to poison the peer list from another network.
        if (!P2pClient.IsSameSubnet(result.RemoteEndPoint.Address))
            return;

        // Sanity bound: a legit announce is a few hundred bytes at most.
        if (result.Buffer.Length is 0 or > 4096)
            return;

        AnnouncePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AnnouncePayload>(result.Buffer, JsonOptions);
        }
        catch (JsonException)
        {
            return; // Not ours — some other UDP chatter on this port.
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.AgentId))
            return;

        // The sender's IP is the only fact we record; hashes are informational.
        P2pServer.RememberPeer(result.RemoteEndPoint.Address);
    }

    private sealed class AnnouncePayload
    {
        [JsonPropertyName("agentId")]
        public string? AgentId { get; set; }

        [JsonPropertyName("files")]
        public IReadOnlyCollection<string>? Files { get; set; }
    }

    /// <summary>
    /// Stops both loops and releases the UDP port.
    /// </summary>
    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Ignore cancellation races during shutdown.
        }

        try
        {
            // Give the loops a brief, bounded window to observe the cancellation.
            Task.WaitAny([_announceLoop ?? Task.CompletedTask, _listenLoop ?? Task.CompletedTask],
                TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown.
        }

        _cts?.Dispose();
        _started.Dispose();
    }
}
