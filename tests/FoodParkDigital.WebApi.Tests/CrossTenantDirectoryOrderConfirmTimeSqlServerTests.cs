using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// How soon a business confirms the orders its customers place on its online menu, in Explorar and on its menu (Fase 29T,
/// ADR 0074), through the API: the orders of its own customers and the answers of its own team are what its card says, never
/// those of another business.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDirectoryOrderConfirmTimeSqlServerTests
{
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";
    private const string Cancel = "/api/v1/public/orders/tracking/cancel";

    private readonly TwoBusinessesApi _api;

    public CrossTenantDirectoryOrderConfirmTimeSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Card_ShouldSayHowSoonABusinessConfirmedItsOrders_ItsOwnCustomersOnly_AndItsMenuToo()
    {
        var quick = await KitchenAsync("confirma-a");
        var slow = await KitchenAsync("confirma-b");
        using var stranger = _api.AnonymousClient();

        // The same three customers order at both, and each team accepts their orders: one within two minutes, three days ago;
        // the other after 40 minutes, a day ago, so its orders are the last each customer placed at either. Each counts its own.
        for (var n = 0; n < 3; n++)
        {
            var phone = $"55400000{n:00}";
            var mine = await OrderAsync(quick, stranger, phone);
            await AcceptAsync(quick, mine);
            await AnsweredAsync(quick, mine, arrivedHoursAgo: 72, waitedMinutes: 2);
            var theirs = await OrderAsync(slow, stranger, phone);
            await AcceptAsync(slow, theirs);
            await AnsweredAsync(slow, theirs, arrivedHoursAgo: 24, waitedMinutes: 40);
        }
        _api.ForgetDirectorySnapshot();

        var listed = await CardsAsync(stranger);
        var quickMenu = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{quick.Slug}"));
        var slowMenu = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{slow.Slug}"));

        // Within five minutes; half an hour or more says nothing, on the card and on the menu.
        Assert.Equal(0, listed[quick.Slug].GetInt32());
        Assert.Equal(JsonValueKind.Null, listed[slow.Slug].ValueKind);
        Assert.Equal(0, quickMenu.GetProperty("orderConfirmTime").GetInt32());
        Assert.Equal(JsonValueKind.Null, slowMenu.GetProperty("orderConfirmTime").ValueKind);
    }

    [SqlServerFact]
    public async Task TheWait_ShouldRunUntilTheTeamAnswered_NotTheCustomerCancelling_WithLimitsThatLeaveOutExactlyFiveMinutes()
    {
        var kitchen = await KitchenAsync("confirma-c");
        using var stranger = _api.AnonymousClient();
        // Answered after 4 minutes, exactly 5 and exactly 15: half is within 15, not five.
        var n = 0;
        foreach (var minutes in new[] { 4, 5, 15 })
        {
            var order = await OrderAsync(kitchen, stranger, $"55500000{n++:00}");
            await AcceptAsync(kitchen, order);
            await AnsweredAsync(kitchen, order, arrivedHoursAgo: 5, waitedMinutes: minutes);
        }
        // Its customer cancelled one after 2 minutes: no answer of the team, and not half an hour of waiting.
        var cancelled = await OrderAsync(kitchen, stranger, $"55500000{n:00}");
        Assert.Equal(HttpStatusCode.OK, (await stranger.PostAsJsonAsync(Cancel, new { token = cancelled.Token })).StatusCode);
        await AnsweredAsync(kitchen, cancelled, arrivedHoursAgo: 5, waitedMinutes: 2);
        _api.ForgetDirectorySnapshot();

        Assert.Equal(1, (await CardsAsync(stranger))[kitchen.Slug].GetInt32());
    }

    private sealed record Kitchen(SignedUp Business, string Slug, Guid Product)
    {
        public HttpClient Client => Business.Client;
    }

    private sealed record Placed(Guid Id, string Token);

    /// <summary>A kitchen that takes online orders, its drawer open and each order accepted by its team.</summary>
    private async Task<Kitchen> KitchenAsync(string name)
    {
        var business = await _api.SignUpAsync(name, TwoBusinessesApi.FoodBusiness);
        var product = await ProductAsync(business.Client, "Tacos al pastor");
        await TwoBusinessesApi.IdAsync(business.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var slug = (await TwoBusinessesApi.ReadAsync(await business.Client.PutAsJsonAsync(OnlineMenu, new { mode = 2 })))
            .GetProperty("slug").GetString()!;
        return new Kitchen(business, slug, product);
    }

    private async Task<Placed> OrderAsync(Kitchen kitchen, HttpClient customer, string phone)
    {
        var token = (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{kitchen.Slug}/orders", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = phone,
            items = new[] { new { productId = kitchen.Product, quantity = 1 } }
        }))).GetProperty("trackingToken").GetString()!;
        var id = await _api.QueryAsync(kitchen.Business.TenantId, db => db.Orders.Where(o => o.TrackingToken == token).Select(o => o.Id).SingleAsync());
        return new Placed(id, token);
    }

    private static async Task AcceptAsync(Kitchen kitchen, Placed order)
        => Assert.Equal(HttpStatusCode.NoContent,
            (await kitchen.Client.PutAsJsonAsync($"/api/v1/orders/{order.Id}/status", new { newStatus = 1 })).StatusCode);

    /// <summary>
    /// The order arrived <paramref name="arrivedHoursAgo"/> and its first change out of «Por confirmar» (the team's answer, or
    /// its customer cancelling it) came <paramref name="waitedMinutes"/> later.
    /// </summary>
    private async Task AnsweredAsync(Kitchen kitchen, Placed order, int arrivedHoursAgo, int waitedMinutes)
        => await _api.QueryAsync(kitchen.Business.TenantId, async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Orders SET CreatedAt = DATEADD(hour, {-arrivedHoursAgo}, SYSDATETIMEOFFSET()) WHERE Id = {order.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE OrderStatusHistory SET ChangedAt = DATEADD(minute, {waitedMinutes}, (SELECT CreatedAt FROM Orders WHERE Id = {order.Id})) WHERE OrderId = {order.Id} AND PreviousStatus = {(int)OrderStatus.Pending} AND NewStatus <> {(int)OrderStatus.Pending}");
            return 0;
        });

    /// <returns>What each card of the search says of how soon its business confirms its orders, by its slug.</returns>
    private static async Task<Dictionary<string, JsonElement>> CardsAsync(HttpClient stranger)
        => (await TwoBusinessesApi.ReadAsync(await stranger.GetAsync("/api/v1/public/directory?q=confirma&pageSize=48")))
            .GetProperty("businesses").GetProperty("items").EnumerateArray()
            .ToDictionary(b => b.GetProperty("slug").GetString()!, b => b.GetProperty("orderConfirmTime"));
}
