using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Daraban.Agent.Core.Http;

public static class StatusEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/status", async (HttpContext ctx) =>
        {
            // TODO: replace with real task-state tracking.
            var status = "waiting"; // or "running task netdiscovery"
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync($"status: {status}\n");
        });
    }

    public static WebApplication BuildMinimalWeb(int port, string? trustIpRange = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port));
        var app = builder.Build();
        Map(app);
        return app;
    }
}