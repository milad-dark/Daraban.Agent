using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using System.Net;

namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// A reusable SNMP session that hides the difference between v1/v2c (community string,
/// simple Messenger API) and v3 (USM user, engine discovery via ReportMessage, then
/// message-based Get/GetNext). Callers only deal with GetAsync / WalkAsync.
/// </summary>
public sealed class SnmpSession : IDisposable
{
    private readonly VersionCode _version;
    private readonly OctetString _community;
    private readonly OctetString _userName;
    private readonly IPrivacyProvider _privacy;
    private readonly UserRegistry _users = new();
    private volatile OctetString? _contextEngineId;

    public SnmpSession(SnmpCredentials credentials)
    {
        Credentials = credentials;
        _version = GetVersion(credentials.Version);
        _community = new OctetString(credentials.Community ?? "public");
        _userName = new OctetString(credentials.UserName ?? string.Empty);

        if (IsV3)
        {
            var auth = BuildAuthentication(credentials);
            _privacy = BuildPrivacy(credentials, auth);
            _users.Add(new User(_userName, _privacy));
        }
        else
        {
            _privacy = DefaultPrivacyProvider.DefaultPair;
        }
    }

    public SnmpCredentials Credentials { get; }

    public bool IsV3 => _version == VersionCode.V3;

    /// <summary>Returns the value of a single OID, or null when the device does not respond.</summary>
    public async Task<string?> GetAsync(IPEndPoint endpoint, string oid, int timeoutMs, CancellationToken ct = default)
    {
        try
        {
            if (IsV3)
            {
                await EnsureEngineAsync(endpoint, timeoutMs, ct);
                var variables = new List<Variable> { new(new ObjectIdentifier(oid)) };
                var msg = new GetRequestMessage(VersionCode.V3, NextId(), NextId(), _userName,
                    new OctetString(""), variables, _privacy, 65535, null);
                var response = await msg.GetResponseAsync(endpoint, _users, ct);
                return response.Scope.Pdu.Variables.FirstOrDefault()?.Data.ToString();
            }

            return await Task.Run(() =>
            {
                var vars = new List<Variable> { new(new ObjectIdentifier(oid)) };
                return Messenger.Get(_version, endpoint, _community, vars, timeoutMs)
                    .FirstOrDefault()?.Data.ToString();
            }, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Walks the subtree under <paramref name="rootOid"/>, returning all variables.</summary>
    public async Task<List<Variable>> WalkAsync(IPEndPoint endpoint, string rootOid, int timeoutMs, CancellationToken ct = default)
    {
        var results = new List<Variable>();

        if (IsV3)
        {
            await EnsureEngineAsync(endpoint, timeoutMs, ct);
            var root = new ObjectIdentifier(rootOid);
            var current = rootOid;
            for (int i = 0; i < 1000; i++)
            {
                ct.ThrowIfCancellationRequested();
                var next = await GetNextAsync(endpoint, current, timeoutMs, ct);
                if (next is null)
                    break;
                if (!next.Id.ToString().StartsWith(root.ToString(), StringComparison.Ordinal))
                    break; // left the subtree
                results.Add(next);
                if (next.Data is EndOfMibView)
                    break;
                current = next.Id.ToString();
            }
        }
        else
        {
            await Task.Run(() =>
            {
                Messenger.Walk(_version, endpoint, _community, new ObjectIdentifier(rootOid), results,
                    timeoutMs, WalkMode.WithinSubtree);
            }, ct);
        }

        return results;
    }

    public void Dispose()
    {
        // SharpSnmpLib opens/closes its UDP sockets per operation; nothing to release here.
    }

    // ── internals ─────────────────────────────────────────────────────────────

    private int _nextId = new Random().Next(1, int.MaxValue / 2 - 1);
    private int NextId() => _nextId++;

    /// <summary>SNMPv3: discover the engine ID by sending a Discovery (Unconfirmed) message.</summary>
    private async Task EnsureEngineAsync(IPEndPoint endpoint, int timeoutMs, CancellationToken ct)
    {
        if (_contextEngineId is not null)
            return;

        var discovery = new Discovery(NextId(), NextId(), NextId());
        var report = await discovery.GetResponseAsync(endpoint, ct);
        _contextEngineId = report.Parameters.EngineId;
        _privacy.EngineIds.Add(_contextEngineId);
    }

    /// <summary>SNMPv3: send one GetNext request and return the variable, or null at end of MIB.</summary>
    private async Task<Variable?> GetNextAsync(IPEndPoint endpoint, string currentOid, int timeoutMs, CancellationToken ct)
    {
        var variables = new List<Variable> { new(new ObjectIdentifier(currentOid)) };
        var msg = new GetNextRequestMessage(VersionCode.V3, NextId(), NextId(), _userName,
            new OctetString(""), variables, _privacy, 65535, null);
        var response = await msg.GetResponseAsync(endpoint, _users, ct);
        return response.Scope.Pdu.Variables.FirstOrDefault();
    }

    private static VersionCode GetVersion(string version)
        => version.Trim().ToLowerInvariant() switch
        {
            "v1" => VersionCode.V1,
            "v3" => VersionCode.V3,
            _ => VersionCode.V2,
        };

    private static IAuthenticationProvider BuildAuthentication(SnmpCredentials c)
    {
        var pass = new OctetString(c.AuthPass ?? string.Empty);
        return c.AuthProtocol.Trim().ToUpperInvariant() switch
        {
            "MD5" => new MD5AuthenticationProvider(pass),
            "SHA1" or "SHA" => new SHA1AuthenticationProvider(pass),
            "SHA256" => new SHA256AuthenticationProvider(pass),
            _ => new MD5AuthenticationProvider(pass),
        };
    }

    private static IPrivacyProvider BuildPrivacy(SnmpCredentials c, IAuthenticationProvider auth)
    {
        var privPass = c.PrivPass ?? string.Empty;
        return c.PrivProtocol.Trim().ToUpperInvariant() switch
        {
            "AES" when privPass.Length > 0 => new AESPrivacyProvider(new OctetString(privPass), auth),
            "DES" when privPass.Length > 0 => new DESPrivacyProvider(new OctetString(privPass), auth),
            _ => new DefaultPrivacyProvider(auth),
        };
    }
}

/// <summary>
/// Holds the SNMP access parameters (v1/v2c community, or v3 USM user + auth/priv).
/// </summary>
public sealed class SnmpCredentials
{
    public string Version { get; set; } = "v2c";
    public string? Community { get; set; } = "public";
    public string? UserName { get; set; }
    public string? AuthPass { get; set; }
    public string AuthProtocol { get; set; } = "MD5";
    public string? PrivPass { get; set; }
    public string PrivProtocol { get; set; } = "AES";
    public int TimeoutMs { get; set; } = 2000;

    /// <summary>Maps AgentOptions SNMP settings to a credentials object once, at call sites.</summary>
    public static SnmpCredentials FromAgentOptions(Daraban.Agent.Core.Config.AgentOptions options)
        => new()
        {
            Version = options.SnmpVersion,
            Community = options.SnmpCommunity,
            UserName = options.SnmpV3User,
            AuthPass = options.SnmpV3AuthPass,
            AuthProtocol = options.SnmpV3AuthProtocol,
            PrivPass = options.SnmpV3PrivPass,
            PrivProtocol = options.SnmpV3PrivProtocol,
            TimeoutMs = options.SnmpTimeoutMs,
        };
}