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
        // A diagnostic listener must not be exposed to the entire network by default.
        builder.WebHost.ConfigureKestrel(o =>
        {
            if (string.IsNullOrWhiteSpace(trustIpRange))
                o.ListenLocalhost(port);
            else
                o.ListenAnyIP(port);
        });

        var app = builder.Build();

        if (!string.IsNullOrWhiteSpace(trustIpRange))
        {
            var allowed = ParseCidr(trustIpRange);
            app.Use(async (context, next) =>
            {
                if (context.Connection.RemoteIpAddress is not { } remote || !allowed(remote))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next();
            });
        }

        //builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port));
        //var app = builder.Build();

        // TODO: honor trustIpRange (glpi-agent's httpd-trust) by rejecting requests from
        // outside the configured CIDR — left permissive for now since this is a status
        // readout, not a control surface.
        Map(app, tracker);
        return app;
    }
    private static Func<System.Net.IPAddress, bool> ParseCidr(string cidr)
    {
        var parts = cidr.Split('/', 2);
        var network = System.Net.IPAddress.Parse(parts[0]);
        var prefix = parts.Length == 2 ? int.Parse(parts[1]) : network.GetAddressBytes().Length * 8;
        var bytes = network.GetAddressBytes();
        return address =>
        {
            var candidate = address.MapToIPv4().GetAddressBytes();
            if (candidate.Length != bytes.Length || prefix is < 0 or > 32)
                return false;
            for (var i = 0; i < bytes.Length; i++)
            {
                var remaining = prefix - (i * 8);
                if (remaining <= 0)
                    break;
                var mask = (byte)(0xFF << Math.Max(0, 8 - remaining));
                if ((candidate[i] & mask) != (bytes[i] & mask))
                    return false;
            }
            return true;
        };
    }
}