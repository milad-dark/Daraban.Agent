using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Transport;

namespace Daraban.Agent.Tests;

public class DarabanClientFactoryMultiTargetTests
{
    [Fact]
    public void CreateAll_ReturnsClientPerServer()
    {
        var options = new AgentOptions
        {
            Servers = ["http://a.local", "http://b.local"]
        };

        var clients = DarabanClientFactory.CreateAll(options);

        Assert.Equal(2, clients.Count);
        Assert.All(clients, c => Assert.NotEmpty(c.ServerUrl));
    }

    [Fact]
    public void CreateAll_SkipsBlankServers()
    {
        var options = new AgentOptions
        {
            Servers = ["http://a.local", "", "  "]
        };

        var clients = DarabanClientFactory.CreateAll(options);

        Assert.Single(clients);
    }

    [Fact]
    public void CreateAll_ReturnsEmptyWhenNoServers()
    {
        var options = new AgentOptions { Servers = [] };

        var clients = DarabanClientFactory.CreateAll(options);

        Assert.Empty(clients);
    }
}