using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Transport;

namespace Daraban.Agent.Tests;

public class DarabanClientFactoryTests
{
    [Fact]
    public void BuildHandler_ExplicitProxy_SetsWebProxy()
    {
        var options = new AgentOptions
        {
            Proxy = "http://proxy.local:8080"
        };

        using var handler = Assert.IsType<HttpClientHandler>(DarabanClientFactory.BuildHandler(options));

        Assert.NotNull(handler.Proxy);
        Assert.Equal("http://proxy.local:8080/", handler.Proxy.GetProxy(new Uri("http://example.com")).ToString());
    }

    [Fact]
    public void BuildHandler_NoProxy_DisablesProxy()
    {
        var options = new AgentOptions
        {
            Proxy = "none"
        };

        using var handler = Assert.IsType<HttpClientHandler>(DarabanClientFactory.BuildHandler(options));

        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void BuildHandler_NullProxy_LeavesNullProxy()
    {
        var options = new AgentOptions();

        using var handler = Assert.IsType<HttpClientHandler>(DarabanClientFactory.BuildHandler(options));

        Assert.Null(handler.Proxy);
    }
}