using Daraban.Agent.Core.Config;

namespace Daraban.Agent.Tests;

public class AgentOptionsTests
{
    [Fact]
    public void Server_Get_ReturnsFirstConfiguredServer()
    {
        var options = new AgentOptions
        {
            Servers = ["http://a.local", "http://b.local"]
        };

        Assert.Equal("http://a.local", options.Server);
    }

    [Fact]
    public void Server_Get_ReturnsNullWhenNoServers()
    {
        var options = new AgentOptions();

        Assert.Null(options.Server);
    }

    [Fact]
    public void Server_Set_SingleValueReplacesServerList()
    {
        var options = new AgentOptions
        {
            Servers = ["http://a.local", "http://b.local"]
        };

        options.Server = "http://c.local";

        Assert.Single(options.Servers);
        Assert.Equal("http://c.local", options.Servers[0]);
    }

    [Fact]
    public void Server_Set_ClearsListWhenNullOrWhitespace()
    {
        var options = new AgentOptions
        {
            Servers = ["http://a.local"]
        };

        options.Server = null;

        Assert.Empty(options.Servers);
    }
}