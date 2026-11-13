using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The deposit to accept a quote (Fase 31N, ADR 0094) through the API: the business asks it once its Mercado Pago account is
/// connected; the customer, without an account, cannot accept from the link without paying it and goes to pay it; a signed
/// notification of the account accepts the version and makes it its service order, with the deposit as «En línea», in that
/// business only. The return page answers only with the link of the version paid.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantQuoteDepositsSqlServerTests
{
    private const string OnlinePayments = "/api/v1/online-payments";

    private readonly TwoBusinessesApi _api;

    public CrossTenantQuoteDepositsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task ADepositPaid_ShouldAcceptTheQuote_AndMakeItsOrder_OnlyInItsBusiness()
    {
        var mine = await _api.SignUpAsync("anticipo-a");
        var theirs = await _api.SignUpAsync("anticipo-b");

        // Without Mercado Pago connected the share is refused; connected, it is asked (the menu's switch stays off).
        Assert.Equal(HttpStatusCode.BadRequest, (await mine.Client.PutAsJsonAsync("/api/v1/quotes/settings",
            new { validityDays = 15, onlineReplies = true, depositPercent = 30 })).StatusCode);
        var account = await ConnectAsync(mine.Client);
        var settings = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync("/api/v1/quotes/settings",
            new { validityDays = 15, onlineReplies = true, depositPercent = 30 }));
        Assert.Equal((30, true), (settings.GetProperty("depositPercent").GetInt32(), settings.GetProperty("depositAvailable").GetBoolean()));
        // Each business its own: the other one asks none.
        Assert.Equal(30, (await mine.Client.GetFromJsonAsync<JsonElement>("/api/v1/quotes/settings")).GetProperty("depositPercent").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await theirs.Client.GetFromJsonAsync<JsonElement>("/api/v1/quotes/settings")).GetProperty("depositPercent").ValueKind);

        var (quoteId, token) = await SentAsync(mine.Client);
        var (_, theirToken) = await SentAsync(theirs.Client);
        Assert.Equal(348m, (await mine.Client.GetFromJsonAsync<JsonElement>($"/api/v1/quotes/{quoteId}")).GetProperty("depositAmount").GetDecimal());

        using var customer = _api.AnonymousClient();
        var view = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync("/api/v1/public/quotes/view", new { token }));
        Assert.Equal((348m, true), (view.GetProperty("depositAmount").GetDecimal(), view.GetProperty("canPayDeposit").GetBoolean()));
        // Accepting is paying it.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await customer.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token, accepted = true })).StatusCode);
        // The other business's version asks none, and a link unknown has nothing to pay.
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/v1/public/quotes/deposit", new { token = theirToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await customer.PostAsJsonAsync("/api/v1/public/quotes/deposit", new { token = new string('x', 43) })).StatusCode);

        var started = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync("/api/v1/public/quotes/deposit", new { token }));
        var checkoutId = started.GetProperty("checkoutId").GetGuid();
        Assert.Equal(348m, started.GetProperty("amount").GetDecimal());
        Assert.StartsWith("https://", started.GetProperty("checkoutUrl").GetString());

        // Paid in Mercado Pago: the return page with the other business's link does not find it.
        var payment = _api.OnlinePayments.Approve(checkoutId, 348m, account);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync("/api/v1/public/quotes/deposit/confirm",
            new { token = theirToken, checkoutId, paymentId = payment.Id })).StatusCode);

        // A signed notification of the account accepts the quote and makes its order, in my business only.
        Assert.Equal(HttpStatusCode.OK, (await _api.PostMercadoPagoWebhookAsync(payment.Id, account)).StatusCode);
        await _api.ProcessWebhookInboxAsync();

        var quote = await _api.QueryAsync(mine.TenantId, db => db.Quotes.SingleAsync(q => q.Id == quoteId));
        Assert.Equal((QuoteStatus.Accepted, (QuoteReplyChannel?)QuoteReplyChannel.Link), (quote.Status, quote.ReplyChannel));
        var order = await _api.QueryAsync(mine.TenantId, db => db.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == quote.OrderId));
        Assert.Equal((OrderType.Service, 1160m), (order.OrderType, order.Total));
        var paid = Assert.Single(order.Payments);
        Assert.Equal((OrderPaymentMethod.Online, 348m, payment.Id), (paid.Method, paid.Amount, paid.Reference));
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.Orders.CountAsync()));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OnlineCheckouts.ToListAsync()));
        Assert.Equal(QuoteStatus.Sent, (await _api.QueryAsync(theirs.TenantId, db => db.Quotes.SingleAsync())).Status);

        // The customer's return says it was paid and which order it became; the business sees it paid.
        var status = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync("/api/v1/public/quotes/deposit/confirm",
            new { token, checkoutId, paymentId = payment.Id }));
        Assert.Equal(((int)OnlineCheckoutStatus.Placed, order.OrderNumber), (status.GetProperty("status").GetInt32(), status.GetProperty("orderNumber").GetString()));
        Assert.True((await mine.Client.GetFromJsonAsync<JsonElement>($"/api/v1/quotes/{quoteId}")).GetProperty("depositPaid").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync("/api/v1/public/quotes/view", new { token })))
            .GetProperty("depositPaid").GetBoolean());

        // Mercado Pago stays while the deposit is asked.
        Assert.Equal(HttpStatusCode.BadRequest, (await mine.Client.DeleteAsync($"{OnlinePayments}/account")).StatusCode);
    }

    /// <summary>A quote of 1,160 sent to Valeria, and its link.</summary>
    private static async Task<(Guid Id, string Token)> SentAsync(HttpClient owner)
    {
        var draft = await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync("/api/v1/quotes",
            new { customerName = "Valeria Ruiz", customerPhone = "5522107765" }));
        var id = draft.GetProperty("id").GetGuid();
        await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync($"/api/v1/quotes/{id}", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = "5522107765",
            validityDays = 15,
            lines = new[] { new { productId = (Guid?)null, description = "Cambio de calentador", quantity = 1m, unitPrice = 1160m } }
        }));
        var sent = await TwoBusinessesApi.ReadAsync(await owner.PostAsync($"/api/v1/quotes/{id}/send", null));
        return (id, sent.GetProperty("trackingToken").GetString()!);
    }

    /// <returns>The account Mercado Pago connected.</returns>
    private static async Task<long> ConnectAsync(HttpClient owner)
    {
        var started = await TwoBusinessesApi.ReadAsync(await owner.PostAsync($"{OnlinePayments}/connect", null));
        var state = Guid.ParseExact(HttpUtility.ParseQueryString(new Uri(started.GetProperty("authorizationUrl").GetString()!).Query)["state"]!, "N");
        var connected = await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync($"{OnlinePayments}/connect/complete", new { state, code = "TG-code" }));
        Assert.False(connected.GetProperty("acceptsPayments").GetBoolean());
        return connected.GetProperty("accountId").GetInt64();
    }
}
