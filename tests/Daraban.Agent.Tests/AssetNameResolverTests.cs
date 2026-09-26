using Daraban.Agent.Core.Tools;

namespace Daraban.Agent.Tests;

public class AssetNameResolverTests
{
    [Theory]
    [InlineData("DESKTOP-TLM14VK", "DESKTOP-TLM14VK")]
    [InlineData("host.domain.local", "host")]
    [InlineData("host.corp.example.com", "host")]
    public void Resolve_Mode1_ReturnsShortName(string rawName, string expected)
    {
        Assert.Equal(expected, AssetNameResolver.Resolve(rawName, 1));
    }

    [Theory]
    [InlineData("DESKTOP-TLM14VK", "DESKTOP-TLM14VK")]
    [InlineData("host.domain.local", "host.domain.local")]
    public void Resolve_Mode2_ReturnsNameAsFound(string rawName, string expected)
    {
        Assert.Equal(expected, AssetNameResolver.Resolve(rawName, 2));
    }

    [Fact]
    public void Resolve_Mode3_ReturnsNonEmptyName()
    {
        // DNS resolution is environment-dependent; only assert a usable name comes back.
        var result = AssetNameResolver.Resolve("localhost", 3);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NullOrWhitespace_ReturnsInputUnchanged(string? rawName)
    {
        Assert.Equal(rawName, AssetNameResolver.Resolve(rawName!, 1));
    }

    [Fact]
    public void Resolve_UnknownMode_FallsBackToShortName()
    {
        Assert.Equal("host", AssetNameResolver.Resolve("host.domain.local", 99));
    }
}
