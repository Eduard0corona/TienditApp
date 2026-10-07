using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// A round of a table's tab leaves the stock as it goes to the kitchen, through the API and SQL Server: refused as a sale
/// is when the business blocks (leaving nothing behind, not even a folio), sent with the warning in the answer when it
/// warns, a closing that takes nothing twice and a cancellation that gives everything back.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class TabRoundStockSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public TabRoundStockSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Rounds_ShouldLeaveTheStockAsTheyGo_RefusedOrWarnedAsInASale()
    {
        var fonda = await _api.SignUpAsync("rondas", TwoBusinessesApi.FoodBusiness);
        var client = fonda.Client;
        var taco = await StockedProductAsync(client, "Taco", 4m);

        // Opening the tab sends its first round: 3 of the 4 tacos leave the stock.
        var tab = (await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/orders", Tab(taco, 3)))).GetProperty("id").GetGuid();
        Assert.Equal(1m, await OnHandAsync(client, taco));

        // The business blocks sales without stock (the default): a round of 2 is refused naming what is missing, and
        // nothing of it stays.
        var refused = await client.PostAsJsonAsync($"/api/v1/orders/{tab}/rounds", Round(taco, 2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Contains("No alcanza Taco", await refused.Content.ReadAsStringAsync());
        Assert.Equal(1m, await OnHandAsync(client, taco));
        Assert.Equal(1, await _api.QueryAsync(fonda.TenantId, db => db.KitchenTickets.CountAsync(t => t.OrderId == tab)));
        Assert.Equal(3m, await _api.QueryAsync(fonda.TenantId, db => db.OrderItems.Where(i => i.OrderId == tab).SumAsync(i => i.Quantity)));

        // Selling and warning instead: the round goes, says what ran out, and the stock goes below zero.
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/my-tenant");
        await TwoBusinessesApi.ReadAsync(await client.PutAsJsonAsync("/api/v1/auth/my-tenant/sales-policy", new
        {
            pricesIncludeTax = profile.GetProperty("pricesIncludeTax").GetBoolean(),
            defaultTaxRate = profile.GetProperty("defaultTaxRate").GetDecimal(),
            autoCloseCounterSales = profile.GetProperty("autoCloseCounterSales").GetBoolean(),
            insufficientStockPolicy = 1
        }));
        var warned = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync($"/api/v1/orders/{tab}/rounds", Round(taco, 2)));
        Assert.Equal(["Taco quedó en -1"], warned.GetProperty("stockWarnings").EnumerateArray().Select(w => w.GetString()));
        Assert.Equal(2, warned.GetProperty("kitchenTickets").GetArrayLength());
        Assert.Equal(-1m, await OnHandAsync(client, taco));

        // Paid and closed: nothing leaves the stock twice.
        await PayAndCloseAsync(client, tab, warned.GetProperty("balance").GetDecimal());
        Assert.Equal(-1m, await OnHandAsync(client, taco));

        // One exit per round sent, and the folios of the business's movements in a row: the refused round took none.
        var movements = await _api.QueryAsync(fonda.TenantId, db => db.InventoryMovements.AsNoTracking()
            .OrderBy(m => m.MovementNumber)
            .Select(m => new { m.MovementNumber, m.MovementType, m.MovementReason, m.ExternalReference })
            .ToListAsync());
        var exits = movements.Where(m => m.MovementType == InventoryMovementType.Exit && m.MovementReason == InventoryMovementReason.Sale).ToList();
        Assert.Equal(2, exits.Count);
        Assert.All(exits, exit => Assert.StartsWith($"ORDER:{tab:N}:ROUND:", exit.ExternalReference));
        Assert.Equal(
            Enumerable.Range(1, movements.Count),
            movements.Select(m => int.Parse(m.MovementNumber[(m.MovementNumber.LastIndexOf('-') + 1)..])));
    }

    [SqlServerFact]
    public async Task CancellingAnOpenTab_ShouldGiveBackWhatItsRoundsTook()
    {
        var fonda = await _api.SignUpAsync("cancela", TwoBusinessesApi.FoodBusiness);
        var client = fonda.Client;
        var taco = await StockedProductAsync(client, "Taco", 5m);
        var tab = (await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/orders", Tab(taco, 2)))).GetProperty("id").GetGuid();
        await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync($"/api/v1/orders/{tab}/rounds", Round(taco, 1)));
        Assert.Equal(2m, await OnHandAsync(client, taco));

        var cancelled = await client.PutAsJsonAsync($"/api/v1/orders/{tab}/cancel", new { reason = "Se fueron" });

        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal(5m, await OnHandAsync(client, taco));
    }

    private static async Task<Guid> StockedProductAsync(HttpClient client, string name, decimal onHand)
    {
        var product = await ProductAsync(client, name);
        await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync($"/api/v1/inventory/products/{product}/stock", new { initialQuantity = onHand, unitCost = 10m }));
        return product;
    }

    private static async Task<decimal> OnHandAsync(HttpClient client, Guid product)
        => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/products/{product}/stock")).GetProperty("onHand").GetDecimal();

    private static async Task PayAndCloseAsync(HttpClient client, Guid tab, decimal balance)
    {
        if ((await client.GetAsync("/api/v1/cash/current-session")).StatusCode == HttpStatusCode.NoContent)
            await TwoBusinessesApi.IdAsync(client, "/api/v1/cash/open", new { openingAmount = 0m });
        await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync($"/api/v1/orders/{tab}/payments", new { amount = balance, method = 1 }));
        await TwoBusinessesApi.ReadAsync(await client.PostAsync($"/api/v1/orders/{tab}/close-tab", null));
    }

    private static object Tab(Guid product, decimal quantity) => new
    {
        orderType = 0,
        channel = 0,
        createdByBusiness = true,
        openTab = true,
        tableNumber = "Barra",
        items = new[] { new { productId = product, quantity, optionIds = Array.Empty<Guid>() } },
        payments = Array.Empty<object>()
    };

    private static object Round(Guid product, decimal quantity)
        => new { items = new[] { new { productId = product, quantity, optionIds = Array.Empty<Guid>() } } };
}
