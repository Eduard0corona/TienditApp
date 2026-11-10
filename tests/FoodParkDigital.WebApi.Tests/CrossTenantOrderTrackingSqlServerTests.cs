using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.SignalR.Client;
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
    private const string Cancel = "/api/v1/public/orders/tracking/cancel";

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

    [SqlServerFact]
    public async Task Link_ShouldCancelOnlyItsOrder_TheAccountOnlyItsOwn_AndOnlyTheirBusinessShouldHearIt()
    {
        // Fase 29R (ADR 0072).
        var mine = await _api.SignUpAsync("cancela-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("cancela-b", TwoBusinessesApi.FoodBusiness);
        var myTacos = await ProductAsync(mine.Client, "Tacos de canasta");
        var theirTacos = await ProductAsync(theirs.Client, "Tacos de cabeza");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var mySlug = await MenuWithOrdersAsync(mine.Client);
        var theirSlug = await MenuWithOrdersAsync(theirs.Client);
        var ana = await _api.SignUpCustomerAsync("cancela-ana");
        var beto = await _api.SignUpCustomerAsync("cancela-beto");
        using var stranger = _api.AnonymousClient();
        var heardByMine = new ConcurrentQueue<(Guid OrderId, bool ByCustomer)>();
        var heardByTheirs = new ConcurrentQueue<(Guid OrderId, bool ByCustomer)>();
        await using var myScreen = await CancellationsAsync(mine.AccessToken, heardByMine);
        await using var theirScreen = await CancellationsAsync(theirs.AccessToken, heardByTheirs);

        // A guest orders twice in one business; Ana orders with her session in the other. None accepted yet.
        var guestsLink = await OrderAsync(stranger, mySlug, myTacos);
        var acceptedLink = await OrderAsync(stranger, mySlug, myTacos);
        var anasLink = await OrderAsync(ana.Client, theirSlug, theirTacos);
        var guestsId = await _api.QueryAsync(mine.TenantId, db => db.Orders.Where(o => o.TrackingToken == guestsLink).Select(o => o.Id).SingleAsync());
        var acceptedId = await _api.QueryAsync(mine.TenantId, db => db.Orders.Where(o => o.TrackingToken == acceptedLink).Select(o => o.Id).SingleAsync());
        var anasId = await _api.QueryAsync(theirs.TenantId, db => db.Orders.Where(o => o.TrackingToken == anasLink).Select(o => o.Id).SingleAsync());
        Assert.True((await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(Tracking, new { token = guestsLink }))).GetProperty("canCancel").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync($"/api/v1/me/orders/{anasId}"))).GetProperty("canCancel").GetBoolean());

        // A link unknown or written with other case cancels nothing, and says the same as following it.
        var swapped = await stranger.PostAsJsonAsync(Cancel, new { token = SwapCase(guestsLink) });
        var unknown = await stranger.PostAsJsonAsync(Cancel, new { token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE" });
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (swapped.StatusCode, unknown.StatusCode));
        Assert.Equal((OrderTracking.NotFoundMessage, OrderTracking.NotFoundMessage), (await DetailAsync(swapped), await DetailAsync(unknown)));

        // The guest cancels theirs with its link, without a session: its business hears it was the customer, the other one nothing.
        var cancelled = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(Cancel, new { token = guestsLink }));
        Assert.Equal((false, false), (cancelled.GetProperty("canCancel").GetBoolean(), cancelled.GetProperty("inProgress").GetBoolean()));
        // Tapped twice: the same answer.
        Assert.Equal(HttpStatusCode.OK, (await stranger.PostAsJsonAsync(Cancel, new { token = guestsLink })).StatusCode);

        // The detail of Ana's order is cancelled only by Ana: not another account, the team of a business, or no session.
        var betos = await beto.Client.PostAsync($"/api/v1/me/orders/{anasId}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, betos.StatusCode);
        Assert.Equal("No encontramos ese pedido en tu cuenta.", await DetailAsync(betos));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/me/orders/{anasId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.Client.PostAsync($"/api/v1/me/orders/{guestsId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.PostAsync($"/api/v1/me/orders/{anasId}/cancel", null)).StatusCode);
        var anas = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsync($"/api/v1/me/orders/{anasId}/cancel", null));
        Assert.Equal((99, false), (anas.GetProperty("status").GetInt32(), anas.GetProperty("canCancel").GetBoolean()));

        // Once the business accepted it, the customer calls: the link says so and cancels nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await mine.Client.PutAsJsonAsync($"/api/v1/orders/{acceptedId}/status", new { newStatus = 1 })).StatusCode);
        var accepted = await stranger.PostAsJsonAsync(Cancel, new { token = acceptedLink });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, accepted.StatusCode);
        Assert.Equal(FoodParkDigital.Domain.Orders.Entities.Order.CustomerCancelAcceptedMessage, await DetailAsync(accepted));

        await WaitUntilAsync(() => heardByMine.Count >= 1 && heardByTheirs.Count >= 1);
        Assert.Equal([(guestsId, true)], heardByMine.ToArray());
        Assert.Equal([(anasId, true)], heardByTheirs.ToArray());
        Assert.Equal(1, await _api.QueryAsync(mine.TenantId, db => db.Orders.Where(o => o.Id == guestsId).SelectMany(o => o.StatusHistory)
            .CountAsync(h => h.NewStatus == FoodParkDigital.Domain.Orders.Enums.OrderStatus.Cancelled)));
    }

    [SqlServerFact]
    public async Task MyOrders_ShouldHearOnlyThatTheirOwnOrdersChanged_ByTheirId()
    {
        // Fase 31A (ADR 0083): the hub of an account, without any business in its token.
        var business = await _api.SignUpAsync("vivo-a", TwoBusinessesApi.FoodBusiness);
        var tacos = await ProductAsync(business.Client, "Tacos al pastor");
        await TwoBusinessesApi.IdAsync(business.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var slug = await MenuWithOrdersAsync(business.Client);
        var ana = await _api.SignUpCustomerAsync("vivo-ana");
        var beto = await _api.SignUpCustomerAsync("vivo-beto");
        using var stranger = _api.AnonymousClient();
        var heardByAna = new ConcurrentQueue<JsonElement>();
        var heardByBeto = new ConcurrentQueue<JsonElement>();
        await using var anasScreen = await MyOrdersAsync(ana.Client, heardByAna);
        await using var betosScreen = await MyOrdersAsync(beto.Client, heardByBeto);

        var anasLink = await OrderAsync(ana.Client, slug, tacos);
        var guestsLink = await OrderAsync(stranger, slug, tacos);
        var betosLink = await OrderAsync(beto.Client, slug, tacos);
        var ids = await _api.QueryAsync(business.TenantId, db => db.Orders
            .Where(o => o.TrackingToken == anasLink || o.TrackingToken == guestsLink || o.TrackingToken == betosLink)
            .Select(o => new { o.Id, o.TrackingToken })
            .ToListAsync());
        var anasId = ids.Single(o => o.TrackingToken == anasLink).Id;
        var guestsId = ids.Single(o => o.TrackingToken == guestsLink).Id;
        var betosId = ids.Single(o => o.TrackingToken == betosLink).Id;

        // The business accepts the three, Beto's last, and then prepares Ana's: what reached Ana's screen before that
        // would have arrived first.
        foreach (var id in new[] { anasId, guestsId, betosId })
            Assert.Equal(HttpStatusCode.NoContent, (await business.Client.PutAsJsonAsync($"/api/v1/orders/{id}/status", new { newStatus = 1 })).StatusCode);
        await WaitUntilAsync(() => heardByBeto.Any(heard => OrderOf(heard) == betosId));
        var heardBefore = heardByAna.Count;
        Assert.Equal(HttpStatusCode.NoContent, (await business.Client.PutAsJsonAsync($"/api/v1/orders/{anasId}/status", new { newStatus = 2 })).StatusCode);
        await WaitUntilAsync(() => heardByAna.Count > heardBefore);

        Assert.NotEmpty(heardByAna);
        Assert.All(heardByAna, heard => Assert.Equal(anasId, OrderOf(heard)));
        Assert.All(heardByBeto, heard => Assert.Equal(betosId, OrderOf(heard)));
        // Only the id travels: nothing of the business or the order.
        Assert.All(heardByAna.Concat(heardByBeto), heard => Assert.Equal(["orderId"], heard.EnumerateObject().Select(p => p.Name)));

        // Without a session the hub of the accounts does not open.
        await using var anonymous = _api.Hub("/hubs/my-orders", "");
        await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());
    }

    private async Task<HubConnection> MyOrdersAsync(HttpClient customer, ConcurrentQueue<JsonElement> heard)
    {
        var connection = _api.Hub("/hubs/my-orders", customer.DefaultRequestHeaders.Authorization!.Parameter!);
        connection.On<JsonElement>("MyOrderChanged", payload => heard.Enqueue(payload.Clone()));
        await connection.StartAsync();
        return connection;
    }

    private static Guid OrderOf(JsonElement heard) => heard.GetProperty("orderId").GetGuid();

    private async Task<HubConnection> CancellationsAsync(string accessToken, ConcurrentQueue<(Guid OrderId, bool ByCustomer)> heard)
    {
        var connection = _api.Hub("/hubs/orders", accessToken);
        connection.On<JsonElement>("OrderCancelled", payload =>
            heard.Enqueue((payload.GetProperty("orderId").GetGuid(), payload.GetProperty("byCustomer").GetBoolean())));
        await connection.StartAsync();
        return connection;
    }

    /// <returns>The link of the order placed in the menu of <paramref name="slug"/>.</returns>
    private static async Task<string> OrderAsync(HttpClient customer, string slug, Guid product)
        => (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{slug}/orders", new
        {
            customerName = "Cliente Cancela",
            customerPhone = "5512345678",
            items = new[] { new { productId = product, quantity = 1 } }
        }))).GetProperty("trackingToken").GetString()!;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        // What else arrives with them would come right behind: a moment more before saying nothing else came.
        await Task.Delay(300);
    }

    /// <returns>The slug of the menu, now taking orders.</returns>
    private static async Task<string> MenuWithOrdersAsync(HttpClient owner)
        => (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync(OnlineMenu, new { mode = 2 }))).GetProperty("slug").GetString()!;

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();

    private static string SwapCase(string token)
        => new(token.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
}
