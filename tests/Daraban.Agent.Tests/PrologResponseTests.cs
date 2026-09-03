using Daraban.Agent.Core.Models;
using System.Text.Json;

namespace Daraban.Agent.Tests;

public class PrologResponseTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserialize_FullProlog_AllFieldsPopulated()
    {
        const string json = """
        {
          "tasks": ["local", "netinventory"],
          "workers": 4,
          "servedSince": "2024-01-01T00:00:00Z",
          "remote": [ { "url": "ssh://root:pw@10.0.0.5", "mode": "ssh" } ],
          "config": "{ \"delaytime\": 600 }",
          "delayTime": 600,
          "force": true,
          "glpiVersion": "11.0.0"
        }
        """;

        var parsed = JsonSerializer.Deserialize<PrologResponse>(json, CaseInsensitive);

        Assert.NotNull(parsed);
        Assert.Equal(["local", "netinventory"], parsed!.Tasks);
        Assert.Equal(4, parsed.Workers);
        Assert.Equal("2024-01-01T00:00:00Z", parsed.ServedSince);
        Assert.Single(parsed.Remote);
        Assert.Equal("ssh://root:pw@10.0.0.5", parsed.Remote[0].ToConnectionString());
        Assert.Equal(600, parsed.DelayTime);
        Assert.True(parsed.Force);
        Assert.Equal("11.0.0", parsed.GlpiVersion);
    }

    [Fact]
    public void Deserialize_EmptyJson_DefaultsApplied()
    {
        var parsed = JsonSerializer.Deserialize<PrologResponse>("{}", CaseInsensitive);

        Assert.NotNull(parsed);
        Assert.Null(parsed!.Tasks);
        Assert.Equal(0, parsed.Workers);
        Assert.Empty(parsed.Remote);
        Assert.False(parsed.Force);
    }

    [Fact]
    public void Deserialize_PascalCaseJson_AlsoWorks()
    {
        var parsed = JsonSerializer.Deserialize<PrologResponse>("""
        {
          "Tasks": ["local"],
          "DelayTime": 120
        }
        """, CaseInsensitive);

        Assert.NotNull(parsed);
        Assert.Equal(["local"], parsed!.Tasks);
        Assert.Equal(120, parsed.DelayTime);
    }
}