using Daraban.Agent.Core.Agents;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Http;
using Daraban.Agent.Service;
using Microsoft.Extensions.Options;

var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureServices((ctx, services) =>
{
    // Bind agent options from config (see appsettings.json)
    services.Configure<AgentOptions>(ctx.Configuration.GetSection("Agent"));

    services.AddSingleton<AgentStatusTracker>();

    // Every task the CLI knows about, so the service and CLI never drift apart on capability.
    services.AddSingleton<IAgentTask, LocalInventoryTask>();
    services.AddSingleton<IAgentTask, NetDiscoveryTask>();
    services.AddSingleton<IAgentTask, NetInventoryTask>();
    services.AddSingleton<IAgentTask, RemoteInventoryTask>();
    services.AddSingleton<IAgentTask, WakeOnLanTask>();
    services.AddSingleton<IAgentTask, DeployTask>();
    services.AddSingleton<IAgentTask, EsxInventoryTask>();

    // This line was missing before, which meant Worker never ran at all —
    // the service process stayed up but did no scheduled work.
    services.AddHostedService<Worker>();
});

builder.ConfigureHostOptions(o =>
{
    o.ServicesStartConcurrently = true;
    o.ServicesStopConcurrently = true;
});

var host = builder.Build();

var agentOptions = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
if (!agentOptions.NoHttpd)
{
    var tracker = host.Services.GetRequiredService<AgentStatusTracker>();
    _ = Task.Run(async () =>
    {
        try
        {
            var app = StatusEndpoint.BuildMinimalWeb(agentOptions.HttpPort, tracker, agentOptions.HttpTrust);
            Console.WriteLine($"[service] HTTP status interface listening on port {agentOptions.HttpPort} (/status).");
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[service] Failed to start HTTP status interface: {ex.Message}");
        }
    });
}

await host.RunAsync();
