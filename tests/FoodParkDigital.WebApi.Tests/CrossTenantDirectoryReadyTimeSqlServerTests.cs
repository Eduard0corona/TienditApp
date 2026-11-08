using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// How soon a kitchen has ready the orders its customers place on its online menu, in Explorar, on its menu and for its
/// owner (Fase 29Y, ADR 0079), through the API: the orders of its own customers that its own team marked «Listo» are what its
/// card says, never those of another business.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDirectoryReadyTimeSqlServerTests
{
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";

    private readonly TwoBusinessesApi _api;

    public CrossTenantDirectoryReadyTimeSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Card_ShouldSayHowSoonAKitchenHadItsOrdersReady_ItsOwnCustomersOnly_ItsMenuAndItsOwnerToo()
    {
        var quick = await KitchenAsync("listo-a");
        var slow = await KitchenAsync("listo-b");
        using var stranger = _api.AnonymousClient();

        // The same three customers order at both, and each team marks their orders ready: one 10 minutes after they
        // arrived, three days ago; the other 70 minutes after, a day ago, so its orders are the last each customer placed.
        for (var n = 0; n < 3; n++)
        {
            var phone = $"55700000{n:00}";
            var mine = await OrderAsync(quick, stranger, phone);
            await ReadyAsync(quick, mine, arrivedHoursAgo: 72, readyMinutes: 10);
            var theirs = await OrderAsync(slow, stranger, phone);
            await ReadyAsync(slow, theirs, arrivedHoursAgo: 24, readyMinutes: 70);
        }
        // One more of the quick kitchen, slow and later, handed over without being marked ready: it does not count.
        var handedOver = await OrderAsync(quick, stranger, "5570000000");
        await StatusAsync(quick, handedOver, OrderStatus.Confirmed);
        await StatusAsync(quick, handedOver, OrderStatus.Delivered);
        _api.ForgetDirectorySnapshot();

        var listed = await CardsAsync(stranger);
        var quickMenu = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{quick.Slug}"));
        var slowMenu = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{slow.Slug}"));
        var slowProfile = await TwoBusinessesApi.ReadAsync(await slow.Client.GetAsync("/api/v1/auth/my-tenant/directory"));

        // Within fifteen minutes; an hour or more says nothing, on the card and on the menu, but its owner reads it.
        Assert.Equal(0, listed[quick.Slug].GetInt32());
        Assert.Equal(JsonValueKind.Null, listed[slow.Slug].ValueKind);
        Assert.Equal(0, quickMenu.GetProperty("readyTime").GetInt32());
        Assert.Equal(JsonValueKind.Null, slowMenu.GetProperty("readyTime").ValueKind);
        Assert.True(slowProfile.GetProperty("countsReady").GetBoolean());
        Assert.Equal(3, slowProfile.GetProperty("readyTime").GetInt32());
    }

    private sealed record Kitchen(SignedUp Business, string Slug, Guid Product)
    {
        public HttpClient Client => Business.Client;
    }

    private sealed record Placed(Guid Id);

    /// <summary>A kitchen that takes online orders, its drawer open and each order handled by its team.</summary>
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
        return new Placed(await _api.QueryAsync(kitchen.Business.TenantId,
            db => db.Orders.Where(o => o.TrackingToken == token).Select(o => o.Id).SingleAsync()));
    }

    private static async Task StatusAsync(Kitchen kitchen, Placed order, OrderStatus status)
        => Assert.Equal(HttpStatusCode.NoContent,
            (await kitchen.Client.PutAsJsonAsync($"/api/v1/orders/{order.Id}/status", new { newStatus = (int)status })).StatusCode);

    /// <summary>
    /// The team accepts the order and marks it ready: it arrived <paramref name="arrivedHoursAgo"/>, and was ready
    /// <paramref name="readyMinutes"/> after.
    /// </summary>
    private async Task ReadyAsync(Kitchen kitchen, Placed order, int arrivedHoursAgo, int readyMinutes)
    {
        await StatusAsync(kitchen, order, OrderStatus.Confirmed);
        await StatusAsync(kitchen, order, OrderStatus.Ready);
        await _api.QueryAsync(kitchen.Business.TenantId, async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Orders SET CreatedAt = DATEADD(hour, {-arrivedHoursAgo}, SYSDATETIMEOFFSET()) WHERE Id = {order.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE OrderStatusHistory SET ChangedAt = DATEADD(minute, {readyMinutes}, (SELECT CreatedAt FROM Orders WHERE Id = {order.Id})) WHERE OrderId = {order.Id} AND NewStatus = {(int)OrderStatus.Ready}");
            return 0;
        });
    }

    /// <returns>What each card of the search says of how soon its kitchen has its orders ready, by its slug.</returns>
    private static async Task<Dictionary<string, JsonElement>> CardsAsync(HttpClient stranger)
        => (await TwoBusinessesApi.ReadAsync(await stranger.GetAsync("/api/v1/public/directory?q=listo&pageSize=48")))
            .GetProperty("businesses").GetProperty("items").EnumerateArray()
            .ToDictionary(b => b.GetProperty("slug").GetString()!, b => b.GetProperty("readyTime"));
}
