using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Couriers;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Errors;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The orders that go out with Paquetenvia and the cash it collects (Fase 22D) through the API, on SQL Server: each
/// business sends out only its own orders and sees and settles only what the courier owes it (the business header of an
/// owner changes nothing); whoever takes the orders sends one out, whoever handles the cash reads what the courier owes,
/// and only whoever also charges orders settles it, in their own drawer.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantCourierSettlementsSqlServerTests
{
    private const string Settlements = "/api/v1/courier-settlements";
    private const string Password = "Secreto1";
    private const string Address = "Av. Reforma 123, Centro";

    private readonly TwoBusinessesApi _api;

    public CrossTenantCourierSettlementsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task CourierCash_OfAnotherBusiness_ShouldBeInvisible_AndNeverSettledByIt()
    {
        var mine = await _api.SignUpAsync("repartidor-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("repartidor-b", TwoBusinessesApi.FoodBusiness);
        var myDrawer = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var orderId = await DeliveryOrderAsync(mine.Client, "Tacos de canasta");
        var theirOrder = await DeliveryOrderAsync(theirs.Client, "Tacos de cabeza");

        // Sending my order out, even with the header of my business, finds no such order.
        using (var dispatch = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/orders/{orderId}/delivery/dispatch")
               {
                   Content = JsonContent.Create(Dispatch("PQ9999", collectsCash: true))
               })
        {
            dispatch.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(dispatch)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await mine.Client.PutAsJsonAsync($"/api/v1/orders/{orderId}/delivery/dispatch", Dispatch("pq 1234", collectsCash: true, paysWith: 100m))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await theirs.Client.PutAsJsonAsync($"/api/v1/orders/{theirOrder}/delivery/dispatch", Dispatch("PQ5555", collectsCash: true))).StatusCode);
        await DeliverAsync(mine.Client, orderId);
        await DeliverAsync(theirs.Client, theirOrder);

        var read = (await mine.Client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}")).GetProperty("delivery");
        Assert.Equal(("PQ1234", 65m, 100m, true),
            (read.GetProperty("courierFolio").GetString(), read.GetProperty("cashToCollect").GetDecimal(),
                read.GetProperty("paysWith").GetDecimal(), read.GetProperty("awaitsCourierCash").GetBoolean()));

        // Each business sees what the courier owes it, nothing of the other one.
        foreach (var (business, folio) in new[] { (mine, "PQ1234"), (theirs, "PQ5555") })
        {
            var receivable = (await business.Client.GetFromJsonAsync<JsonElement>($"{Settlements}/receivable")).EnumerateArray().Single();
            Assert.Equal((1, 65m), (receivable.GetProperty("owedOrders").GetInt32(), receivable.GetProperty("owedAmount").GetDecimal()));
            var pending = await business.Client.GetFromJsonAsync<JsonElement>($"{Settlements}/pending-orders?courier=Paquetenvia");
            Assert.Equal([folio], pending.GetProperty("owed").EnumerateArray().Select(o => o.GetProperty("courierFolio").GetString()));
        }

        // Their settlement with my order finds it is not owed to them; mine goes into my drawer.
        var refused = await theirs.Client.PostAsJsonAsync(Settlements, Settlement(65m, [orderId], []));
        Assert.Equal((HttpStatusCode.Conflict, ApiErrorCodes.Conflict, CourierSettlementErrors.NotOwed),
            (refused.StatusCode, Problem(await refused.Content.ReadAsStringAsync()).Code, Problem(await refused.Content.ReadAsStringAsync()).Detail));
        // A screen that saw another balance is told to reload instead of charging what it did not show.
        var stale = await mine.Client.PostAsJsonAsync(Settlements, Settlement(50m, [orderId], [new { kind = 1, amount = -10m }]) with { ordersTotal = 60m });
        Assert.Equal((HttpStatusCode.Conflict, CourierSettlementErrors.NotOwed), (stale.StatusCode, Problem(await stale.Content.ReadAsStringAsync()).Detail));
        var response = await mine.Client.PostAsJsonAsync(Settlements, Settlement(55m, [orderId], [new { kind = 1, amount = -10m }]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await TwoBusinessesApi.ReadAsync(response)).GetProperty("settlement");
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal((55m, 65m, -10m, 1), (created.GetProperty("handedAmount").GetDecimal(), created.GetProperty("ordersTotal").GetDecimal(),
            created.GetProperty("adjustmentsTotal").GetDecimal(), created.GetProperty("orderCount").GetInt32()));

        using (var detail = new HttpRequestMessage(HttpMethod.Get, $"{Settlements}/{id}"))
        {
            detail.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(detail)).StatusCode);
        }
        Assert.Equal(0, (await theirs.Client.GetFromJsonAsync<JsonElement>(Settlements)).GetProperty("totalCount").GetInt32());
        Assert.Equal(id, (await mine.Client.GetFromJsonAsync<JsonElement>(Settlements)).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Equal(orderId, (await mine.Client.GetFromJsonAsync<JsonElement>($"{Settlements}/{id}")).GetProperty("orders").EnumerateArray().Single().GetProperty("orderId").GetGuid());

        // The same order is not settled twice.
        var twice = await mine.Client.PostAsJsonAsync(Settlements, Settlement(65m, [orderId], []));
        Assert.Equal((HttpStatusCode.Conflict, ApiErrorCodes.Conflict), (twice.StatusCode, Problem(await twice.Content.ReadAsStringAsync()).Code));

        Assert.Equal(
            new[] { (mine.TenantId, myDrawer) },
            (await _api.QueryAsync(mine.TenantId, db => db.CourierSettlements.ToListAsync())).Select(s => (s.TenantId, s.CashSessionId)));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.CourierSettlements.ToListAsync()));
        var movements = await _api.QueryAsync(mine.TenantId, db => db.CashTransactions
            .Where(t => t.CashSessionId == myDrawer).OrderBy(t => t.Amount).Select(t => new { t.TenantId, t.Amount, t.Notes }).ToListAsync());
        Assert.Equal([(mine.TenantId, -10m, "Viaje de Paquetenvia"), (mine.TenantId, 65m, "Liquidación de Paquetenvia")],
            movements.Select(m => (m.TenantId, m.Amount, m.Notes)));
        Assert.Equal(1, await _api.QueryAsync(theirs.TenantId, db => db.Orders.CountAsync(o => o.PaidAmount == 0m)));
    }

    [SqlServerFact]
    public async Task CourierCash_ShouldBeSentOutByWhoeverTakesOrders_AndSettledByWhoeverChargesThemAndHandlesTheCash()
    {
        var owner = await _api.SignUpAsync("repartidor-equipo", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var orderId = await DeliveryOrderAsync(owner.Client, "Tortas ahogadas");
        var waiter = await EmployeeAsync(owner, "ManageOrders");
        var charger = await EmployeeAsync(owner, "ChargeOrders");
        var keeper = await EmployeeAsync(owner, "ManageCash");
        var cashier = await EmployeeAsync(owner, "ChargeOrders", "ManageCash");

        Assert.Equal(HttpStatusCode.NoContent,
            (await waiter.PutAsJsonAsync($"/api/v1/orders/{orderId}/delivery/dispatch", Dispatch("PQ1", collectsCash: true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await keeper.PutAsJsonAsync($"/api/v1/orders/{orderId}/delivery/dispatch", Dispatch("PQ2", collectsCash: true))).StatusCode);
        await DeliverAsync(waiter, orderId);

        foreach (var url in new[] { Settlements, $"{Settlements}/receivable", $"{Settlements}/pending-orders?courier=Paquetenvia" })
        {
            Assert.Equal(HttpStatusCode.OK, (await keeper.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await charger.GetAsync(url)).StatusCode);
        }
        // Settling charges the orders and moves the drawer: both are needed.
        foreach (var staff in new[] { waiter, charger, keeper })
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(Settlements, Settlement(65m, [orderId], []))).StatusCode);

        // The owner's drawer is open, but the cashier receives the cash in theirs.
        var withoutDrawer = await cashier.PostAsJsonAsync(Settlements, Settlement(65m, [orderId], []));
        Assert.Equal((HttpStatusCode.BadRequest, CourierSettlementErrors.NoOwnDrawer),
            (withoutDrawer.StatusCode, Problem(await withoutDrawer.Content.ReadAsStringAsync()).Detail));
        var cashierDrawer = await TwoBusinessesApi.IdAsync(cashier, "/api/v1/cash/open", new { openingAmount = 0m });
        var settled = await cashier.PostAsJsonAsync(Settlements, Settlement(65m, [orderId], []));
        Assert.Equal(HttpStatusCode.Created, settled.StatusCode);

        Assert.Equal(cashierDrawer, await _api.QueryAsync(owner.TenantId, db => db.CourierSettlements.Select(s => s.CashSessionId).SingleAsync()));
        Assert.Equal(65m, await _api.QueryAsync(owner.TenantId, db => db.CashTransactions.Where(t => t.CashSessionId == cashierDrawer).SumAsync(t => t.Amount)));
    }

    [SqlServerFact]
    public async Task CourierCash_ShouldBeRefused_WhenNotSaidWhole_OrWhenItDoesNotAddUp()
    {
        var owner = await _api.SignUpAsync("repartidor-validas", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var orderId = await DeliveryOrderAsync(owner.Client, "Pozole");

        var withoutCollectsCash = await owner.Client.PutAsJsonAsync($"/api/v1/orders/{orderId}/delivery/dispatch", new { courier = 1, folio = "PQ1" });
        Assert.Equal(HttpStatusCode.BadRequest, withoutCollectsCash.StatusCode);
        var notEnough = await owner.Client.PutAsJsonAsync($"/api/v1/orders/{orderId}/delivery/dispatch", Dispatch("PQ1", collectsCash: true, paysWith: 50m));
        Assert.Equal((HttpStatusCode.BadRequest, HomeDeliveryRules.PaysWithNotEnoughMessage(50m, 65m)),
            (notEnough.StatusCode, Problem(await notEnough.Content.ReadAsStringAsync()).Detail));
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.Client.PutAsJsonAsync($"/api/v1/orders/{orderId}/delivery/dispatch", Dispatch("PQ1", collectsCash: true))).StatusCode);
        await DeliverAsync(owner.Client, orderId);

        var nullAdjustment = await owner.Client.PostAsync(Settlements, new StringContent(
            $"{{\"courier\":1,\"method\":0,\"handedAmount\":65,\"orderIds\":[\"{orderId}\"],\"ordersTotal\":65,\"adjustments\":[null]}}",
            System.Text.Encoding.UTF8, "application/json"));
        var withoutHanded = await owner.Client.PostAsJsonAsync(Settlements, new { courier = 1, method = 0, orderIds = new[] { orderId }, ordersTotal = 65m, adjustments = Array.Empty<object>() });
        var byCard = await owner.Client.PostAsJsonAsync(Settlements, Settlement(65m, [orderId], []) with { method = 1 });
        var unbalanced = await owner.Client.PostAsJsonAsync(Settlements, Settlement(60m, [orderId], []));

        Assert.Equal((HttpStatusCode.BadRequest, ApiErrorCodes.ValidationFailed, CourierSettlement.AdjustmentKindMessage),
            (nullAdjustment.StatusCode, Problem(await nullAdjustment.Content.ReadAsStringAsync()).Code, Problem(await nullAdjustment.Content.ReadAsStringAsync()).Detail));
        Assert.Equal((HttpStatusCode.BadRequest, ApiErrorCodes.ValidationFailed), (withoutHanded.StatusCode, Problem(await withoutHanded.Content.ReadAsStringAsync()).Code));
        Assert.Equal((HttpStatusCode.BadRequest, ApiErrorCodes.ValidationFailed), (byCard.StatusCode, Problem(await byCard.Content.ReadAsStringAsync()).Code));
        Assert.Equal((HttpStatusCode.BadRequest, CourierSettlement.DoesNotBalanceMessage(60m, 65m)),
            (unbalanced.StatusCode, Problem(await unbalanced.Content.ReadAsStringAsync()).Detail));
        Assert.Equal(0, await _api.QueryAsync(owner.TenantId, db => db.CourierSettlements.CountAsync()));
        Assert.Equal(0m, await _api.QueryAsync(owner.TenantId, db => db.Orders.Where(o => o.Id == orderId).Select(o => o.PaidAmount).SingleAsync()));
    }

    /// <summary>An order to 72000 of two products at $15 with the $35 fee of the business's zone: $65, unpaid.</summary>
    private static async Task<Guid> DeliveryOrderAsync(HttpClient owner, string productName)
    {
        var zones = await owner.GetFromJsonAsync<JsonElement>("/api/v1/delivery-zones");
        if (zones.GetArrayLength() == 0)
        {
            await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync("/api/v1/delivery-zones", new
            {
                name = "Centro",
                fee = 35m,
                minimumOrder = 0m,
                freeDeliveryFrom = (decimal?)null,
                postalCodes = new[] { "72000" },
                isActive = true,
                revision = 0
            }));
        }

        var product = await ProductAsync(owner, productName);
        var order = await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync("/api/v1/orders", new
        {
            customerName = "Ana",
            customerPhone = "5512345678",
            items = new[] { new { productId = product, quantity = 2 } },
            delivery = new { postalCode = "72000", addressLine = Address }
        }));
        Assert.Equal(65m, order.GetProperty("total").GetDecimal());
        return order.GetProperty("id").GetGuid();
    }

    private static async Task DeliverAsync(HttpClient client, Guid orderId)
    {
        var delivered = await client.PutAsJsonAsync($"/api/v1/orders/{orderId}/status", new { newStatus = 6 });
        Assert.True(delivered.IsSuccessStatusCode, await delivered.Content.ReadAsStringAsync());
    }

    private static object Dispatch(string folio, bool collectsCash, decimal? paysWith = null)
        => new { courier = 1, folio, trackingUrl = (string?)null, collectsCash, paysWith };

    private sealed record SettlementBody(int courier, int method, decimal handedAmount, Guid[] orderIds, decimal ordersTotal, object[] adjustments, string? reference);

    private static SettlementBody Settlement(decimal handedAmount, Guid[] orderIds, object[] adjustments)
        => new(1, 0, handedAmount, orderIds, 65m * orderIds.Length, adjustments, "Corte");

    /// <summary>A member of the owner's team with the permissions given, signed in with its own password.</summary>
    private async Task<HttpClient> EmployeeAsync(SignedUp owner, params string[] permissions)
    {
        var email = $"equipo-{Guid.NewGuid():N}@demo.com";
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password = Password,
            firstName = "Empleado",
            lastName = "De prueba",
            appRole = 3,
            tenantRole = 2,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }

    private static (string? Code, string? Detail) Problem(string body)
    {
        var problem = JsonDocument.Parse(body).RootElement;
        return (
            problem.TryGetProperty("code", out var code) ? code.GetString() : null,
            problem.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }
}
