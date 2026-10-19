using System.Net;
using System.Text;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Infrastructure.OnlinePayments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// What TienditApp sends to Mercado Pago and how it reads the answers (Fase 24I, ADR 0031), against a recorded HTTP
/// handler: the checkout only takes payments decided at once, every call carries the business's key, and an answer that
/// is not a clear yes is never read as one.
/// </summary>
public class MercadoPagoGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid CheckoutId = Guid.Parse("6f1c2b8e-3d4a-4b5c-9e7f-0a1b2c3d4e5f");

    [Fact]
    public void AuthorizationUrl_ShouldSendTheOwnerWithTheStateAndTheChallenge()
    {
        var (gateway, _) = Create();
        var state = Guid.NewGuid();

        var url = gateway.AuthorizationUrl(state, "challenge-value_123").ToString();

        Assert.StartsWith("https://auth.mercadopago.com.mx/authorization?", url);
        Assert.Contains("client_id=app-123", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains($"state={state:N}", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Ftienditapp.test%2Fapi%2Fv1%2Fpayments%2Fmercadopago%2Fconnect%2Fcallback", url);
        Assert.Contains("code_challenge=challenge-value_123", url);
        Assert.Contains("code_challenge_method=S256", url);
    }

    [Fact]
    public async Task ConnectAsync_ShouldExchangeTheCodeWithItsVerifier_AndReadTheKeys()
    {
        var (gateway, http) = Create(_ => Json(HttpStatusCode.OK, """
            {"access_token":"APP_USR-access","token_type":"Bearer","expires_in":15552000,"scope":"offline_access payments read write",
             "user_id":987654321,"refresh_token":"TG-refresh","public_key":"APP_USR-public","live_mode":true}
            """));

        var credentials = await gateway.ConnectAsync("TG-code", "verifier-abc", CancellationToken.None);

        Assert.Equal(new OnlinePaymentCredentials(987654321, "APP_USR-access", "TG-refresh", Now.AddSeconds(15552000), true), credentials);
        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.mercadopago.com/oauth/token", request.Url);
        Assert.Null(request.Authorization);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("authorization_code", body.RootElement.GetProperty("grant_type").GetString());
        Assert.Equal("TG-code", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("verifier-abc", body.RootElement.GetProperty("code_verifier").GetString());
        Assert.Equal("app-123", body.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("https://tienditapp.test/api/v1/payments/mercadopago/connect/callback", body.RootElement.GetProperty("redirect_uri").GetString());
    }

    [Fact]
    public async Task RenewAsync_ShouldUseTheRefreshKey()
    {
        var (gateway, http) = Create(_ => Json(HttpStatusCode.OK, """
            {"access_token":"APP_USR-new","expires_in":15552000,"user_id":987654321,"refresh_token":"TG-new","live_mode":true}
            """));

        var credentials = await gateway.RenewAsync("TG-refresh", CancellationToken.None);

        Assert.Equal("APP_USR-new", credentials.AccessToken);
        using var body = JsonDocument.Parse(Assert.Single(http.Requests).Body!);
        Assert.Equal("refresh_token", body.RootElement.GetProperty("grant_type").GetString());
        Assert.Equal("TG-refresh", body.RootElement.GetProperty("refresh_token").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ConnectAsync_ShouldBeRefused_WhenMercadoPagoSaysNo(HttpStatusCode status)
    {
        var (gateway, _) = Create(_ => Json(status, """{"error":"invalid_grant","message":"invalid authorization code","status":400}"""));

        await Assert.ThrowsAsync<OnlinePaymentGatewayRefusedException>(
            () => gateway.ConnectAsync("TG-used", "verifier-abc", CancellationToken.None));
    }

    [Fact]
    public async Task ConnectAsync_ShouldBeUnavailable_WhenMercadoPagoFailsOrGivesNoKeys()
    {
        var (failing, _) = Create(_ => Json(HttpStatusCode.BadGateway, "{}"));
        var (incomplete, _) = Create(_ => Json(HttpStatusCode.OK, """{"access_token":"APP_USR-access","expires_in":15552000}"""));

        await Assert.ThrowsAsync<OnlinePaymentGatewayUnavailableException>(
            () => failing.ConnectAsync("TG-code", "verifier-abc", CancellationToken.None));
        await Assert.ThrowsAsync<OnlinePaymentGatewayUnavailableException>(
            () => incomplete.ConnectAsync("TG-code", "verifier-abc", CancellationToken.None));
    }

    [Fact]
    public async Task CreateCheckoutAsync_ShouldTakeOnlyPaymentsDecidedAtOnce_InTheBusinessAccount()
    {
        var (gateway, http) = Create(_ => Json(HttpStatusCode.Created, """
            {"id":"123-pref","init_point":"https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=123-pref",
             "sandbox_init_point":"https://sandbox.mercadopago.com.mx/checkout/v1/redirect?pref_id=123-pref"}
            """));

        var page = await gateway.CreateCheckoutAsync("APP_USR-business", new OnlinePaymentCheckoutRequest(
            CheckoutId, "Pedido en Café Luna", 235.50m, "https://tienditapp.test/m/cafe-luna/pago/abc",
            Now.AddMinutes(30), "Café Luna"), CancellationToken.None);

        Assert.Equal(new OnlinePaymentCheckoutPage("123-pref", "https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=123-pref"), page);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://api.mercadopago.com/checkout/preferences", request.Url);
        Assert.Equal("Bearer APP_USR-business", request.Authorization);
        Assert.Equal(CheckoutId.ToString(), request.IdempotencyKey);

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(235.50m, item.GetProperty("unit_price").GetDecimal());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());
        Assert.Equal("MXN", item.GetProperty("currency_id").GetString());
        Assert.Equal("Pedido en Café Luna", item.GetProperty("title").GetString());
        Assert.Equal(CheckoutId.ToString(), root.GetProperty("external_reference").GetString());
        Assert.True(root.GetProperty("binary_mode").GetBoolean());
        Assert.Equal(1, root.GetProperty("payment_methods").GetProperty("installments").GetInt32());
        Assert.Equal(
            ["ticket", "atm", "bank_transfer"],
            root.GetProperty("payment_methods").GetProperty("excluded_payment_types").EnumerateArray()
                .Select(type => type.GetProperty("id").GetString()));
        Assert.Equal("approved", root.GetProperty("auto_return").GetString());
        Assert.Equal("https://tienditapp.test/m/cafe-luna/pago/abc", root.GetProperty("back_urls").GetProperty("success").GetString());
        Assert.Equal("https://tienditapp.test/m/cafe-luna/pago/abc", root.GetProperty("back_urls").GetProperty("failure").GetString());
        Assert.True(root.GetProperty("expires").GetBoolean());
        Assert.Equal("2026-10-19T18:30:00.000+00:00", root.GetProperty("expiration_date_to").GetString());
        Assert.Equal("CAFE LUNA", root.GetProperty("statement_descriptor").GetString());
        Assert.False(root.TryGetProperty("marketplace_fee", out _));
        Assert.False(root.TryGetProperty("notification_url", out _));
    }

    [Fact]
    public async Task CreateCheckoutAsync_ShouldOpenTheTestPage_InSandbox()
    {
        var (gateway, _) = Create(
            _ => Json(HttpStatusCode.Created, """{"id":"123-pref","init_point":"https://live.test","sandbox_init_point":"https://sandbox.test"}"""),
            sandbox: true);

        var page = await gateway.CreateCheckoutAsync("TEST-business", new OnlinePaymentCheckoutRequest(
            CheckoutId, "Pedido", 10m, "https://tienditapp.test/back", Now.AddMinutes(30), null), CancellationToken.None);

        Assert.Equal("https://sandbox.test", page.CheckoutUrl);
    }

    [Fact]
    public async Task GetPaymentAsync_ShouldReadWhatDecidesThePayment()
    {
        var (gateway, http) = Create(_ => Json(HttpStatusCode.OK, $$"""
            {"id":1234567890,"status":"approved","status_detail":"accredited","external_reference":"{{CheckoutId}}",
             "transaction_amount":235.5,"currency_id":"MXN","date_approved":"2026-10-19T12:05:31.000-06:00",
             "collector_id":987654321,"live_mode":true,"payer":{"email":"cliente@correo.test"} }
            """));

        var payment = await gateway.GetPaymentAsync("APP_USR-business", "1234567890", CancellationToken.None);

        Assert.NotNull(payment);
        Assert.Equal("1234567890", payment.Id);
        Assert.True(payment.IsApproved);
        Assert.Equal(235.50m, payment.Amount);
        Assert.Equal("MXN", payment.Currency);
        Assert.Equal(CheckoutId.ToString(), payment.ExternalReference);
        Assert.Equal(987654321, payment.CollectorId);
        Assert.True(payment.LiveMode);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 18, 5, 31, TimeSpan.Zero), payment.ApprovedAt);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://api.mercadopago.com/v1/payments/1234567890", request.Url);
        Assert.Equal("Bearer APP_USR-business", request.Authorization);
    }

    [Fact]
    public async Task GetPaymentAsync_ShouldNotReadAsApproved_WhatIsNot()
    {
        var (gateway, _) = Create(_ => Json(HttpStatusCode.OK, """
            {"id":1,"status":"approved","transaction_amount":10,"currency_id":"MXN"}
            """));

        var payment = await gateway.GetPaymentAsync("APP_USR-business", "1", CancellationToken.None);

        Assert.NotNull(payment);
        Assert.False(payment.IsApproved);
        Assert.Null(payment.CollectorId);
        Assert.False(payment.LiveMode);
    }

    [Theory]
    [InlineData("12/../../users/me")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("123456789012345678901")]
    public async Task GetPaymentAsync_ShouldNotCallMercadoPago_WithWhatIsNotAPaymentId(string paymentId)
    {
        var (gateway, http) = Create(_ => Json(HttpStatusCode.OK, "{}"));

        Assert.Null(await gateway.GetPaymentAsync("APP_USR-business", paymentId, CancellationToken.None));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task GetPaymentAsync_ShouldBeNull_WhenTheAccountHasNoSuchPayment_AndUnavailableWhenMercadoPagoFails()
    {
        var (missing, _) = Create(_ => Json(HttpStatusCode.NotFound, """{"message":"Payment not found","error":"not_found","status":404}"""));
        var (failing, _) = Create(_ => Json(HttpStatusCode.InternalServerError, "oops"));

        Assert.Null(await missing.GetPaymentAsync("APP_USR-business", "123", CancellationToken.None));
        await Assert.ThrowsAsync<OnlinePaymentGatewayUnavailableException>(
            () => failing.GetPaymentAsync("APP_USR-business", "123", CancellationToken.None));
    }

    [Fact]
    public async Task FindPaymentsAsync_ShouldSearchByTheCheckout()
    {
        var (gateway, http) = Create(_ => Json(HttpStatusCode.OK, $$"""
            {"paging":{"total":2},"results":[
              {"id":2,"status":"rejected","external_reference":"{{CheckoutId}}","transaction_amount":10,"currency_id":"MXN"},
              {"id":1,"status":"approved","external_reference":"{{CheckoutId}}","transaction_amount":10,"currency_id":"MXN",
               "date_approved":"2026-10-19T12:05:31.000-06:00","collector_id":5,"live_mode":true},
              {"status":"approved"}]}
            """));

        var payments = await gateway.FindPaymentsAsync("APP_USR-business", CheckoutId, CancellationToken.None);

        Assert.Equal(["2", "1"], payments.Select(payment => payment.Id));
        Assert.True(payments[1].IsApproved);
        var request = Assert.Single(http.Requests);
        Assert.StartsWith($"https://api.mercadopago.com/v1/payments/search?external_reference={CheckoutId}&", request.Url);
        Assert.Equal("Bearer APP_USR-business", request.Authorization);
    }

    [Fact]
    public async Task RefundAsync_ShouldGiveTheMoneyBackOnce_WithTheIdempotencyKey()
    {
        var key = Guid.NewGuid();
        var (gateway, http) = Create(_ => Json(HttpStatusCode.Created, """{"id":555666777,"payment_id":1234567890,"amount":100.25,"status":"approved"}"""));

        var outcome = await gateway.RefundAsync("APP_USR-business", "1234567890", 100.25m, key, CancellationToken.None);

        Assert.Equal(new OnlinePaymentRefundOutcome.Done("555666777"), outcome);
        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.mercadopago.com/v1/payments/1234567890/refunds", request.Url);
        Assert.Equal(key.ToString(), request.IdempotencyKey);
        Assert.Equal("Bearer APP_USR-business", request.Authorization);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(100.25m, body.RootElement.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task RefundAsync_ShouldBeRefused_WhenMercadoPagoSaysNo_AndRetriedWhenItDoesNotAnswer()
    {
        var (refused, _) = Create(_ => Json(HttpStatusCode.BadRequest, """{"message":"Insufficient balance to refund","error":"bad_request","status":400,"cause":[{"code":2085}]}"""));
        var (limited, _) = Create(_ => Json(HttpStatusCode.TooManyRequests, "{}"));
        var (failing, _) = Create(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var outcome = await refused.RefundAsync("APP_USR-business", "1", 10m, Guid.NewGuid(), CancellationToken.None);

        var refusal = Assert.IsType<OnlinePaymentRefundOutcome.Refused>(outcome);
        Assert.Contains("Insufficient balance to refund", refusal.Message);
        await Assert.ThrowsAsync<OnlinePaymentGatewayUnavailableException>(
            () => limited.RefundAsync("APP_USR-business", "1", 10m, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<OnlinePaymentGatewayUnavailableException>(
            () => failing.RefundAsync("APP_USR-business", "1", 10m, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Calls_ShouldBeUnavailable_WhenMercadoPagoDoesNotAnswer()
    {
        var (gateway, _) = Create(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<OnlinePaymentGatewayUnavailableException>(
            () => gateway.GetPaymentAsync("APP_USR-business", "1", CancellationToken.None));
    }

    [Fact]
    public async Task Gateway_ShouldOfferNothing_UntilThePlatformConfiguresIt()
    {
        var (withoutApp, http) = Create(clientId: null);
        var (withoutAddress, _) = Create(appUrl: null);
        var (withoutKey, _) = Create(protectionKey: null);

        Assert.False(withoutApp.IsConfigured);
        Assert.False(withoutAddress.IsConfigured);
        Assert.False(withoutKey.IsConfigured);
        Assert.True(Create().Gateway.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => withoutApp.GetPaymentAsync("APP_USR-business", "1", CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => withoutAddress.AuthorizationUrl(Guid.NewGuid(), "challenge"));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("Café Luna", "CAFE LUNA")]
    [InlineData("  Pizzería «Don Ñoño» & Co.  ", "PIZZERIA DON NONO CO")]
    [InlineData("Una tienda con un nombre muy largo", "UNA TIENDA CON UN NOMB")]
    [InlineData("¡¿!?", null)]
    [InlineData(null, null)]
    public void StatementDescriptor_ShouldKeepWhatACardStatementTakes(string? name, string? expected)
    {
        Assert.Equal(expected, MercadoPagoGateway.StatementDescriptor(name));
    }

    private static (MercadoPagoGateway Gateway, RecordingHandler Http) Create(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null,
        bool sandbox = false,
        string? clientId = "app-123",
        string? appUrl = "https://tienditapp.test",
        string? protectionKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=")
    {
        var http = new RecordingHandler(respond ?? (_ => Json(HttpStatusCode.OK, "{}")));
        var protector = new AesGcmSecretProtector(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmSecretProtector.KeySetting] = protectionKey })
            .Build());
        var gateway = new MercadoPagoGateway(
            new SingleClientFactory(http),
            Options.Create(new MercadoPagoOptions
            {
                ClientId = clientId,
                ClientSecret = "secret-xyz",
                WebhookSecret = "webhook-xyz",
                Sandbox = sandbox
            }),
            new Links(appUrl),
            protector,
            new FixedClock(Now),
            NullLogger<MercadoPagoGateway>.Instance);
        return (gateway, http);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(HttpMethod Method, string Url, string? Authorization, string? IdempotencyKey, string? Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Idempotency-Key", out var keys) ? keys.Single() : null,
                body));
            return respond(request);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(MercadoPagoGateway.HttpClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class Links(string? baseUrl) : IAppLinks
    {
        public string? BaseUrl { get; } = baseUrl;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
