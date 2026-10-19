using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Paying online (Fase 24I, ADR 0031) through the API: each business connects, reads, turns on and disconnects only its
/// own Mercado Pago account (the business header of an owner changes nothing, and an authorization started in one business
/// is not finished in another); a customer's payment is confirmed only through the menu of its business; and a signed
/// notification of an account places the order only in the business that connected it, with an «En línea» payment.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantOnlinePaymentsSqlServerTests
{
    private const string Password = "Secreto1";
    private const string Settings = "/api/v1/online-payments";
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";

    private readonly TwoBusinessesApi _api;

    public CrossTenantOnlinePaymentsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Account_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("pagos-cuenta-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("pagos-cuenta-b", TwoBusinessesApi.FoodBusiness);
        var myState = await StartAsync(mine.Client);
        var account = await ConnectAsync(mine.Client, myState);
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"{Settings}/accepts-payments", new { accepts = true }));

        // The other owner, even with the header of the business, sees no account, cannot turn it on or disconnect it,
        // and cannot finish an authorization started in my business.
        Assert.False((await SendAsOtherAsync(theirs, mine, HttpMethod.Get, Settings)).GetProperty("connected").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Put, $"{Settings}/accepts-payments", new { accepts = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Delete, $"{Settings}/account"))).StatusCode);
        var stolenState = await StartAsync(mine.Client);
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PostAsJsonAsync($"{Settings}/connect/complete", new { state = stolenState, code = "TG-code" })).StatusCode);

        var myView = await mine.Client.GetFromJsonAsync<JsonElement>(Settings);
        Assert.Equal((true, account, true), (
            myView.GetProperty("connected").GetBoolean(),
            myView.GetProperty("accountId").GetInt64(),
            myView.GetProperty("acceptsPayments").GetBoolean()));
        var stored = await _api.QueryAsync(mine.TenantId, db => db.OnlinePaymentAccounts.SingleAsync());
        Assert.Equal(mine.TenantId, stored.TenantId);
        Assert.DoesNotContain($"APP_USR-{account}", stored.ProtectedAccessToken);
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OnlinePaymentAccounts.ToListAsync()));
        Assert.Equal(2, await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.CountAsync(a => a.EntityName == "OnlinePaymentAccount")));
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.AuditLogs.CountAsync(a => a.EntityName == "OnlinePaymentAccount")));

        // Staff of my business do not manage it either.
        var cashier = await EmployeeAsync(mine, appRole: 2, "ManageOrders", "ChargeOrders", "ManageCash");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(Settings)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.DeleteAsync($"{Settings}/account")).StatusCode);
    }

    [SqlServerFact]
    public async Task Payment_ShouldPlaceTheOrder_OnlyInTheBusinessOfItsAccount()
    {
        var mine = await _api.SignUpAsync("pagos-cobro-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("pagos-cobro-b", TwoBusinessesApi.FoodBusiness);
        var tacos = await ProductAsync(mine.Client, "Tacos de canasta");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var mySlug = await MenuWithOrdersAsync(mine.Client);
        var theirSlug = await MenuWithOrdersAsync(theirs.Client);
        var account = await ConnectAsync(mine.Client, await StartAsync(mine.Client));
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"{Settings}/accepts-payments", new { accepts = true }));
        using var customer = _api.AnonymousClient();

        Assert.True((await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{mySlug}"))).GetProperty("paysOnline").GetBoolean());
        Assert.False((await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{theirSlug}"))).GetProperty("paysOnline").GetBoolean());

        var started = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/checkouts", new
        {
            customerName = "Ana",
            customerPhone = "5512345678",
            items = new[] { new { productId = tacos, quantity = 2 } }
        }));
        var checkoutId = started.GetProperty("checkoutId").GetGuid();
        var amount = started.GetProperty("amount").GetDecimal();
        Assert.StartsWith("https://", started.GetProperty("checkoutUrl").GetString());
        // Nothing to pay in the other business: its menu takes no online payments.
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync($"/api/v1/public/menu/{theirSlug}/checkouts", new
        {
            customerName = "Ana",
            customerPhone = "5512345678",
            items = new[] { new { productId = tacos, quantity = 2 } }
        })).StatusCode);

        // Paid in Mercado Pago; through the other business's menu the payment is not found.
        var payment = _api.OnlinePayments.Approve(checkoutId, amount, account);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{theirSlug}/checkouts/{checkoutId}/confirm", new { paymentId = payment.Id })).StatusCode);
        Assert.Equal(0, await _api.QueryAsync(mine.TenantId, db => db.Orders.CountAsync()));

        // An unsigned notification is refused; a signed one of the account places the order in my business only.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostMercadoPagoWebhookAsync(payment.Id, account, signature: "ts=1,v1=00")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.PostMercadoPagoWebhookAsync(payment.Id, account)).StatusCode);
        await _api.ProcessWebhookInboxAsync();

        var order = await _api.QueryAsync(mine.TenantId, db => db.Orders.Include(o => o.Payments).SingleAsync());
        Assert.Equal((OrderChannel.Online, amount), (order.Channel, order.Total));
        var paid = Assert.Single(order.Payments);
        Assert.Equal((OrderPaymentMethod.Online, amount, payment.Id), (paid.Method, paid.Amount, paid.Reference));
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.Orders.CountAsync()));
        var checkout = await _api.QueryAsync(mine.TenantId, db => db.OnlineCheckouts.SingleAsync());
        Assert.Equal((OnlineCheckoutStatus.Placed, order.Id, (string?)null, (string?)null), (checkout.Status, checkout.OrderId, checkout.CustomerPhone, checkout.Request));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OnlineCheckouts.ToListAsync()));

        // The customer's return answers the order placed, and the same notification again changes nothing.
        var status = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/checkouts/{checkoutId}/confirm",
            new { paymentId = payment.Id, returnKey = started.GetProperty("returnKey").GetString() }));
        Assert.Equal(order.TrackingToken, status.GetProperty("trackingToken").GetString());
        Assert.Equal(((int)OnlineCheckoutStatus.Placed, order.OrderNumber), (status.GetProperty("status").GetInt32(), status.GetProperty("orderNumber").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await _api.PostMercadoPagoWebhookAsync(payment.Id, account)).StatusCode);
        await _api.ProcessWebhookInboxAsync();
        Assert.Equal(1, await _api.QueryAsync(mine.TenantId, db => db.Orders.CountAsync()));
        Assert.Empty(await _api.QueryAsync(mine.TenantId, db => db.OnlinePaymentRefunds.ToListAsync()));
    }

    /// <summary>A member of the owner's team with the permissions given, signed in with its own password.</summary>
    private async Task<HttpClient> EmployeeAsync(SignedUp owner, int appRole, params string[] permissions)
    {
        var email = $"equipo-{Guid.NewGuid():N}@demo.com";
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password = Password,
            firstName = "Empleado",
            lastName = "De prueba",
            appRole,
            tenantRole = 2,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<Guid> StartAsync(HttpClient owner)
    {
        var started = await TwoBusinessesApi.ReadAsync(await owner.PostAsync($"{Settings}/connect", null));
        var url = new Uri(started.GetProperty("authorizationUrl").GetString()!);
        return Guid.ParseExact(HttpUtility.ParseQueryString(url.Query)["state"]!, "N");
    }

    /// <returns>The account Mercado Pago connected.</returns>
    private static async Task<long> ConnectAsync(HttpClient owner, Guid state)
    {
        var connected = await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync($"{Settings}/connect/complete", new { state, code = "TG-code" }));
        Assert.True(connected.GetProperty("connected").GetBoolean());
        Assert.False(connected.GetProperty("acceptsPayments").GetBoolean());
        return connected.GetProperty("accountId").GetInt64();
    }

    private static HttpRequestMessage AsOther(SignedUp business, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-Tenant-Id", business.TenantId.ToString());
        return request;
    }

    private static async Task<JsonElement> SendAsOtherAsync(SignedUp caller, SignedUp business, HttpMethod method, string url)
    {
        using var request = AsOther(business, method, url);
        return await TwoBusinessesApi.ReadAsync(await caller.Client.SendAsync(request));
    }

    /// <returns>The slug of the menu, now taking orders.</returns>
    private static async Task<string> MenuWithOrdersAsync(HttpClient owner)
        => (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync(OnlineMenu, new { mode = 2 }))).GetProperty("slug").GetString()!;
}
