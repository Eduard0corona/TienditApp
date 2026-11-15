using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Mcp;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Orders AI assistants prepare (Fase 32B, ADR 0096) through the API, with the real handlers and transactions on SQL Server:
/// the MCP server prepares one in the business of its slug only; only the store of that business opens its link; an order
/// sent with it from another store is placed there and leaves it waiting; sent from its store, the order and its confirmation
/// are saved in the same request, and the assistant sees it.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantAssistantOrdersSqlServerTests
{
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";

    private readonly TwoBusinessesApi _api;
    private int _id;

    public CrossTenantAssistantOrdersSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task AnOrderAnAssistantPrepared_ShouldOpenOnlyInTheStoreOfItsBusiness_AndBeConfirmedOnlyByAnOrderThere()
    {
        var mine = await _api.SignUpAsync("asistente-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("asistente-b", TwoBusinessesApi.FoodBusiness);
        var myTacos = await ProductAsync(mine.Client, "Tacos de canasta");
        var theirTacos = await ProductAsync(theirs.Client, "Tacos de cochinita");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var mySlug = await MenuWithOrdersAsync(mine.Client);
        var theirSlug = await MenuWithOrdersAsync(theirs.Client);
        using var assistant = _api.AnonymousClient();

        // Prepared in their business, through the MCP server.
        var prepared = await ToolAsync(assistant, "preparar_pedido", new JsonObject
        {
            ["slug"] = theirSlug,
            ["productos"] = new JsonArray(new JsonObject { ["id"] = theirTacos.ToString(), ["cantidad"] = 3 })
        });
        var token = Regex.Match(prepared, "#pedido=([A-Za-z0-9_-]{43})").Groups[1].Value;
        Assert.Equal(43, token.Length);
        Assert.Equal(AssistantOrderStatus.Pending, (await _api.QueryAsync(theirs.TenantId, db => db.AssistantOrders.SingleAsync())).Status);
        Assert.Empty(await _api.QueryAsync(mine.TenantId, db => db.AssistantOrders.ToListAsync()));

        // My store does not open it; theirs does, with its cart.
        using var customer = _api.AnonymousClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await customer.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/assistant-order", new { token })).StatusCode);
        var open = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{theirSlug}/assistant-order", new { token }));
        Assert.Equal(0, open.GetProperty("state").GetInt32());
        var line = Assert.Single(open.GetProperty("lines").EnumerateArray());
        Assert.Equal((theirTacos, 3), (line.GetProperty("productId").GetGuid(), line.GetProperty("quantity").GetInt32()));

        // An order in my store with their link is placed in mine, and theirs keeps waiting.
        await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/orders", new
        {
            customerName = "Ana",
            customerPhone = "5512345678",
            items = new[] { new { productId = myTacos, quantity = 1 } },
            assistantOrderToken = token
        }));
        Assert.Single(await _api.QueryAsync(mine.TenantId, db => db.Orders.ToListAsync()));
        var waiting = await _api.QueryAsync(theirs.TenantId, db => db.AssistantOrders.SingleAsync());
        Assert.Equal((AssistantOrderStatus.Pending, (Guid?)null), (waiting.Status, waiting.OrderId));

        // From their store with the link in other letters: the database finds it, but it is not the link, and the order stands.
        var swapped = token.ToUpperInvariant() == token ? token.ToLowerInvariant() : token.ToUpperInvariant();
        await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{theirSlug}/orders", new
        {
            customerName = "Luis",
            customerPhone = "5512345670",
            items = new[] { new { productId = theirTacos, quantity = 1 } },
            assistantOrderToken = swapped
        }));
        Assert.Equal(AssistantOrderStatus.Pending, (await _api.QueryAsync(theirs.TenantId, db => db.AssistantOrders.SingleAsync())).Status);

        // Sent from their store: the order and its confirmation, in the same request.
        await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{theirSlug}/orders", new
        {
            customerName = "Ana",
            customerPhone = "5512345679",
            items = new[] { new { productId = theirTacos, quantity = 3 } },
            assistantOrderToken = token
        }));
        var order = await _api.QueryAsync(theirs.TenantId, db => db.Orders.SingleAsync(o => o.CustomerName == "Ana"));
        var confirmed = await _api.QueryAsync(theirs.TenantId, db => db.AssistantOrders.SingleAsync());
        Assert.Equal((AssistantOrderStatus.Confirmed, (Guid?)order.Id), (confirmed.Status, confirmed.OrderId));
        Assert.Contains($"El cliente lo envió. Folio: {order.OrderNumber}.", await ToolAsync(assistant, "ver_pedido", new JsonObject { ["enlace"] = token }));
    }

    private static async Task<string> MenuWithOrdersAsync(HttpClient owner)
        => (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync(OnlineMenu, new { mode = 2 }))).GetProperty("slug").GetString()!;

    /// <summary>One tool call, as an MCP client sends it over Streamable HTTP; the text it answers.</summary>
    private async Task<string> ToolAsync(HttpClient client, string name, JsonObject arguments)
    {
        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = name, ["arguments"] = arguments }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, McpServer.Path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {raw}");
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? string.Join("\n", raw.Split('\n').Where(text => text.StartsWith("data:", StringComparison.Ordinal)).Select(text => text[5..].Trim()))
            : raw;
        var result = JsonNode.Parse(json)!["result"]!;
        Assert.True((bool?)result["isError"] is null or false, result.ToJsonString());
        return (string)result["content"]![0]!["text"]!;
    }
}
