using Daraban.Agent.Core.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Daraban.Agent.Core.Http;

public static class StatusEndpoint
{
    public static void Map(WebApplication app, AgentStatusTracker? tracker = null)
    {
        app.MapGet("/status", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(tracker?.ToStatusText() ?? "status: waiting\n");
        });
    }

    public static WebApplication BuildMinimalWeb(int port, AgentStatusTracker? tracker = null, string? trustIpRange = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port));
        var app = builder.Build();

        // TODO: honor trustIpRange (glpi-agent's httpd-trust) by rejecting requests from
        // outside the configured CIDR — left permissive for now since this is a status
        // readout, not a control surface.
        Map(app, tracker);
        return app;
    }
}