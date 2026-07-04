using Daraban.Agent.Core.Agents;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Http;

var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureServices((ctx, services) =>
{
    // Bind agent options from config (see appsettings.json below)
    services.Configure<AgentOptions>(ctx.Configuration.GetSection("Agent"));
    services.AddSingleton<IAgentTask, LocalInventoryTask>();
    services.AddSingleton<IAgentTask, NetDiscoveryTask>();
    services.AddSingleton<IAgentTask, RemoteInventoryTask>();
    // Add others as you implement them
});

builder.ConfigureHostOptions(o =>
{
    o.ServicesStartConcurrently = true;
    o.ServicesStopConcurrently = true;
});

var host = builder.Build();

// Start HTTP status endpoint in background (like GLPI agent on :62354)【turn0search11】
_ = Task.Run(async () =>
{
    var app = StatusEndpoint.BuildMinimalWeb(62354);
    Console.WriteLine("[service] HTTP status interface listening on port 62354.");
    await app.RunAsync();
});

await host.RunAsync();
