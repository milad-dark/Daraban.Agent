using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;

namespace Daraban.Agent.Tests;

public class SnmpCredentialsTests
{
    [Fact]
    public void FromAgentOptions_MapsAllFields()
    {
        var options = new AgentOptions
        {
            SnmpVersion = "v3",
            SnmpCommunity = "private",
            SnmpV3User = "operator",
            SnmpV3AuthPass = "authpass",
            SnmpV3AuthProtocol = "SHA256",
            SnmpV3PrivPass = "privpass",
            SnmpV3PrivProtocol = "AES",
            SnmpTimeoutMs = 5000,
        };

        var creds = SnmpCredentials.FromAgentOptions(options);

        Assert.Equal("v3", creds.Version);
        Assert.Equal("private", creds.Community);
        Assert.Equal("operator", creds.UserName);
        Assert.Equal("authpass", creds.AuthPass);
        Assert.Equal("SHA256", creds.AuthProtocol);
        Assert.Equal("privpass", creds.PrivPass);
        Assert.Equal("AES", creds.PrivProtocol);
        Assert.Equal(5000, creds.TimeoutMs);
    }

    [Fact]
    public void FromAgentOptions_Defaults_ToV2cPublic()
    {
        var options = new AgentOptions();

        var creds = SnmpCredentials.FromAgentOptions(options);

        Assert.Equal("v2c", creds.Version);
        Assert.Equal("public", creds.Community);
        Assert.Null(creds.UserName);
        Assert.Null(creds.AuthPass);
    }

    [Fact]
    public void Session_IsV3_OnlyWhenVersionIsV3()
    {
        using var v2 = new SnmpSession(new SnmpCredentials { Version = "v2c" });
        using var v1 = new SnmpSession(new SnmpCredentials { Version = "v1" });
        using var v3 = new SnmpSession(new SnmpCredentials
        {
            Version = "v3",
            UserName = "u",
            AuthPass = "a",
            PrivPass = "p"
        });

        Assert.False(v2.IsV3);
        Assert.False(v1.IsV3);
        Assert.True(v3.IsV3);
    }

    [Fact]
    public void Session_UnknownVersion_FallsBackToV2()
    {
        using var session = new SnmpSession(new SnmpCredentials { Version = "bogus" });

        Assert.False(session.IsV3);
    }
}