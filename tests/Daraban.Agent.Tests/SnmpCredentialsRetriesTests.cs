using Daraban.Agent.Core.Collectors;

namespace Daraban.Agent.Tests;

public class SnmpCredentialsRetriesTests
{
    [Fact]
    public void FromAgentOptions_MapsRetries()
    {
        var options = new Daraban.Agent.Core.Config.AgentOptions
        {
            SnmpRetries = 3,
        };

        var creds = SnmpCredentials.FromAgentOptions(options);

        Assert.Equal(3, creds.Retries);
    }

    [Fact]
    public void FromAgentOptions_RetriesDefaultsToZero()
    {
        var options = new Daraban.Agent.Core.Config.AgentOptions();

        var creds = SnmpCredentials.FromAgentOptions(options);

        Assert.Equal(0, creds.Retries);
    }

    [Fact]
    public async Task GetAsync_UnreachableEndpoint_ReturnsNullWithinRetries()
    {
        // A non-routable address that will never answer. With retries=2 the session
        // attempts the GET up to 3 times and ultimately returns null instead of throwing.
        using var session = new SnmpSession(new SnmpCredentials
        {
            Version = "v1",
            Community = "public",
            TimeoutMs = 100,
            Retries = 2,
        });

        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.0.2.123"), 161);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await session.GetAsync(endpoint, "1.3.6.1.2.1.1.1.0", 100, cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task WalkAsync_UnreachableEndpoint_ReturnsEmptyWithinRetries()
    {
        using var session = new SnmpSession(new SnmpCredentials
        {
            Version = "v1",
            Community = "public",
            TimeoutMs = 100,
            Retries = 1,
        });

        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.0.2.123"), 161);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await session.WalkAsync(endpoint, "1.3.6.1.2.1.2.2.1.2", 100, cts.Token);

        Assert.Empty(result);
    }
}