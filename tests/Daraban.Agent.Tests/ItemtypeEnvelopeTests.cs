using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Models;
using Daraban.Agent.Core.Transport;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Daraban.Agent.Tests;

/// <summary>
/// Verifies the GLPI 11+ itemtype (task 2.5) reaches the server envelope, both for the
/// default ("Computer") and for configured custom asset types.
/// </summary>
public class ItemtypeEnvelopeTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private static (DarabanClient Client, CapturingHandler Handler) BuildClient(AgentOptions options)
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        return (new DarabanClient(http, options), handler);
    }

    private static string? ExtractItemtype(string? body)
    {
        if (body is null) return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("itemtype", out var el) ? el.GetString() : null;
    }

    private static AgentOptions BaseOptions() => new()
    {
        Servers = ["http://localhost:5000"],
        FullInventoryPostpone = 0,   // avoid touching %APPDATA% snapshots from the differ
    };

    [Fact]
    public async Task PostInventory_Object_DefaultsToComputer()
    {
        var (client, handler) = BuildClient(BaseOptions());

        await client.PostInventoryAsync("dev1", new DeviceContent { ComputerName = "x" });

        Assert.Equal("Computer", ExtractItemtype(handler.LastBody));
    }

    [Fact]
    public async Task PostInventory_Object_CustomItemtype_FlowsToEnvelope()
    {
        var (client, handler) = BuildClient(BaseOptions());

        await client.PostInventoryAsync("dev1", new DeviceContent(), itemtype: @"Glpi\CustomAsset\ServerAsset");

        Assert.Equal(@"Glpi\CustomAsset\ServerAsset", ExtractItemtype(handler.LastBody));
    }

    [Fact]
    public async Task PostInventory_Json_CustomItemtype_FlowsToEnvelope()
    {
        var (client, handler) = BuildClient(BaseOptions());
        var payload = JsonSerializer.Serialize(new DeviceInventory { DeviceId = "dev1", Content = "{}" });

        await client.PostInventoryAsync(payload, itemtype: @"Glpi\CustomAsset\NetworkAsset");

        Assert.Equal(@"Glpi\CustomAsset\NetworkAsset", ExtractItemtype(handler.LastBody));
    }

    [Fact]
    public async Task PostEsxInventory_DefaultsToEsxHost()
    {
        var (client, handler) = BuildClient(BaseOptions());

        await client.PostEsxInventoryAsync("dev1", new EsxHostInfo());

        Assert.Equal("EsxHost", ExtractItemtype(handler.LastBody));
    }

    [Fact]
    public async Task PostEsxInventory_CustomItemtype_FlowsToEnvelope()
    {
        var (client, handler) = BuildClient(BaseOptions());

        await client.PostEsxInventoryAsync("dev1", new EsxHostInfo(), @"Glpi\CustomAsset\Esx");

        Assert.Equal(@"Glpi\CustomAsset\Esx", ExtractItemtype(handler.LastBody));
    }

    [Fact]
    public void AgentOptions_Itemtype_FieldsExistAndDefaultNull()
    {
        var options = new AgentOptions();

        Assert.Null(options.Itemtype);
        Assert.Null(options.EsxItemtype);

        options.Itemtype = "Custom";
        options.EsxItemtype = "CustomEsx";
        Assert.Equal("Custom", options.Itemtype);
        Assert.Equal("CustomEsx", options.EsxItemtype);
    }
}