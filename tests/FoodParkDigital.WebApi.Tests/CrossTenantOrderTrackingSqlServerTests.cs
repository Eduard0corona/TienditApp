using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Following an order (Fase 24C, ADR 0023) through the API: the link of a receipt opens its order, in its business, without
/// a session and without anything of the customer; the same link written with other case, or one unknown, is not found; and
/// the detail of an order is only for the account that placed it, never for another account or the business's team.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantOrderTrackingSqlServerTests
{
    private const string Tracking = "/api/v1/public/orders/tracking";
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";

    private readonly TwoBusinessesApi _api;

    public CrossTenantOrderTrackingSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Link_ShouldOpenOnlyItsOrder_AndTheDetailShouldBeOnlyForItsAccount()
    {
        var mine = await _api.SignUpAsync("seguimiento-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("seguimiento-b", TwoBusinessesApi.FoodBusiness);
        var myTacos = await ProductAsync(mine.Client, "Tacos al pastor");
        var theirTacos = await ProductAsync(theirs.Client, "Tacos de suadero");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var mySlug = await MenuWithOrdersAsync(mine.Client);
        var theirSlug = await MenuWithOrdersAsync(theirs.Client);
        var ana = await _api.SignUpCustomerAsync("seguimiento-ana");
        var beto = await _api.SignUpCustomerAsync("seguimiento-beto");
        using var stranger = _api.AnonymousClient();

        // Ana orders with her session in one business; a guest orders in the other.
        var anas = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/orders", new
        {
            customerName = "Ana Seguimiento",
            customerPhone = "5512345678",
            items = new[] { new { productId = myTacos, quantity = 2 } }
        }));
        var guests = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync($"/api/v1/public/menu/{theirSlug}/orders", new
        {
            customerName = "Invitado Seguimiento",
            customerPhone = "5587654321",
            items = new[] { new { productId = theirTacos, quantity = 1 } }
        }));
        var anasLink = anas.GetProperty("trackingToken").GetString()!;
        var guestsLink = guests.GetProperty("trackingToken").GetString()!;

        // Each link opens its own order, in its business, without a session and without anything of the customer.
        var tracked = await stranger.PostAsJsonAsync(Tracking, new { token = anasLink });
        var body = await tracked.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, tracked.StatusCode);
        var order = JsonDocument.Parse(body).RootElement;
        Assert.Equal((anas.GetProperty("orderNumber").GetString(), mySlug),
            (order.GetProperty("orderNumber").GetString(), order.GetProperty("businessSlug").GetString()));
        Assert.Equal("Recibido", order.GetProperty("steps")[0].GetProperty("label").GetString());
        Assert.DoesNotContain("Ana Seguimiento", body);
        Assert.DoesNotContain("5512345678", body);
        var theirOrder = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(Tracking, new { token = guestsLink }));
        Assert.Equal(theirSlug, theirOrder.GetProperty("businessSlug").GetString());

        // The database compares without case; the link is the exact one. Unknown and changed answer the same.
        var swapped = await stranger.PostAsJsonAsync(Tracking, new { token = SwapCase(anasLink) });
        var unknown = await stranger.PostAsJsonAsync(Tracking, new { token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE" });
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (swapped.StatusCode, unknown.StatusCode));
        Assert.Equal(OrderTracking.NotFoundMessage, await DetailAsync(swapped));
        Assert.Equal(OrderTracking.NotFoundMessage, await DetailAsync(unknown));

        // The detail of her order is Ana's: neither another account nor the team of the business opens it by its id.
        var id = await _api.QueryAsync(mine.TenantId, db => db.Orders.Where(o => o.TrackingToken == anasLink).Select(o => o.Id).SingleAsync());
        var guestsId = await _api.QueryAsync(theirs.TenantId, db => db.Orders.Where(o => o.TrackingToken == guestsLink).Select(o => o.Id).SingleAsync());
        var detail = await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync($"/api/v1/me/orders/{id}"));
        Assert.Equal((anas.GetProperty("orderNumber").GetString(), true),
            (detail.GetProperty("orderNumber").GetString(), detail.GetProperty("inProgress").GetBoolean()));
        Assert.Equal(HttpStatusCode.NotFound, (await beto.Client.GetAsync($"/api/v1/me/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Client.GetAsync($"/api/v1/me/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.Client.GetAsync($"/api/v1/me/orders/{guestsId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync($"/api/v1/me/orders/{id}")).StatusCode);

        // In her list, it is in progress, not before.
        var inProgress = await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync("/api/v1/me/orders?scope=1&pageSize=50"));
        var past = await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync("/api/v1/me/orders?scope=2&pageSize=50"));
        Assert.Equal(id, Assert.Single(inProgress.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(0, past.GetProperty("items").GetArrayLength());
    }

    /// <returns>The slug of the menu, now taking orders.</returns>
    private static async Task<string> MenuWithOrdersAsync(HttpClient owner)
        => (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync(OnlineMenu, new { mode = 2 }))).GetProperty("slug").GetString()!;

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();

    private static string SwapCase(string token)
        => new(token.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
}
