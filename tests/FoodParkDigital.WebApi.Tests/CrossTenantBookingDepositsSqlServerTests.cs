using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The deposit to book an appointment online (Fase 31O, ADR 0095) through the API: the business asks it once its Mercado Pago
/// account is connected; the booking page says it, a request without paying it is refused, and a signed notification of the
/// account books the request with the deposit held, in that business only. The return page answers only on its business's
/// page. Declined, the deposit goes back through the queue; two payments for the same time at once book it once (the booking
/// lock, with the real transaction) and the other goes back.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantBookingDepositsSqlServerTests
{
    private const string OnlinePayments = "/api/v1/online-payments";
    private const string Settings = "/api/v1/appointments/online-booking";

    private readonly TwoBusinessesApi _api;

    public CrossTenantBookingDepositsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task ADepositPaid_ShouldBookTheRequest_OnlyInItsBusiness_AndGoBackWhenDeclined()
    {
        var mine = await _api.SignUpAsync("reserva-a");
        var theirs = await _api.SignUpAsync("reserva-b");
        var (mySlug, myService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(mine.Client, "Corte con anticipo");
        var (theirSlug, theirService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(theirs.Client, "Masaje sin anticipo");

        // Without Mercado Pago connected the share is refused; connected, it is asked.
        Assert.Equal(HttpStatusCode.BadRequest, (await mine.Client.PutAsJsonAsync(Settings, new { enabled = true, capacity = 1, depositPercent = 30 })).StatusCode);
        var account = await ConnectAsync(mine.Client);
        var settings = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(Settings, new { enabled = true, capacity = 1, depositPercent = 30 }));
        Assert.Equal((30, true), (settings.GetProperty("depositPercent").GetInt32(), settings.GetProperty("depositAvailable").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, (await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(Settings))).GetProperty("depositPercent").ValueKind);

        using var customer = _api.AnonymousClient();
        var page = await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{mySlug}/booking"));
        Assert.Equal(60m, Assert.Single(page.GetProperty("services").EnumerateArray()).GetProperty("depositAmount").GetDecimal());
        var theirPage = await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{theirSlug}/booking"));
        Assert.Equal(JsonValueKind.Null, Assert.Single(theirPage.GetProperty("services").EnumerateArray()).GetProperty("depositAmount").ValueKind);

        var startAt = await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(customer, mySlug, myService);
        // Without paying it, it is refused; the other business's service has nothing to pay here.
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/appointments", CrossTenantOnlineBookingSqlServerTests.Request(myService, startAt, "5512340001"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/appointments/deposit", CrossTenantOnlineBookingSqlServerTests.Request(theirService, startAt, "5512340001"))).StatusCode);

        var started = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/appointments/deposit", CrossTenantOnlineBookingSqlServerTests.Request(myService, startAt, "5512340001")));
        var checkoutId = started.GetProperty("checkoutId").GetGuid();
        var returnKey = started.GetProperty("returnKey").GetString();
        Assert.Equal(60m, started.GetProperty("amount").GetDecimal());

        // Paid in Mercado Pago: the other business's page does not find it, nor the return page of the menu.
        var payment = _api.OnlinePayments.Approve(checkoutId, 60m, account);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{theirSlug}/appointments/deposit/{checkoutId}/confirm", new { paymentId = payment.Id, returnKey })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/checkouts/{checkoutId}/confirm", new { paymentId = payment.Id, returnKey })).StatusCode);

        // A signed notification of the account books the request, in my business only.
        Assert.Equal(HttpStatusCode.OK, (await _api.PostMercadoPagoWebhookAsync(payment.Id, account)).StatusCode);
        await _api.ProcessWebhookInboxAsync();
        var appointment = await _api.QueryAsync(mine.TenantId, db => db.Appointments.SingleAsync());
        Assert.Equal((AppointmentStatus.Requested, (decimal?)60m, (AppointmentDepositStatus?)AppointmentDepositStatus.Held),
            (appointment.Status, appointment.DepositAmount, appointment.DepositStatus));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.Appointments.ToListAsync()));

        // The customer's return, with this browser's key, has the link of the request.
        var status = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/appointments/deposit/{checkoutId}/confirm", new { paymentId = payment.Id, returnKey }));
        Assert.Equal((int)OnlineCheckoutStatus.Placed, status.GetProperty("status").GetInt32());
        Assert.Equal(appointment.TrackingToken, status.GetProperty("receipt").GetProperty("trackingToken").GetString());
        var tracked = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(CrossTenantOnlineBookingSqlServerTests.Tracking, new { token = appointment.TrackingToken }));
        Assert.Equal((60m, true), (tracked.GetProperty("depositAmount").GetDecimal(), tracked.GetProperty("cancelReturnsDeposit").GetBoolean()));

        // The other team neither gives it back nor declines it; its own team declines it, and the deposit goes back.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/appointments/{appointment.Id}/deposit/return", null)).StatusCode);
        var declined = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/appointments/{appointment.Id}/decline", null));
        Assert.Equal((int)AppointmentDepositStatus.Returned, declined.GetProperty("depositStatus").GetInt32());
        await _api.DispatchOutboxAsync();
        var refund = await _api.QueryAsync(mine.TenantId, db => db.OnlinePaymentRefunds.SingleAsync());
        Assert.Equal((OnlinePaymentRefundReason.AppointmentDeposit, payment.Id, 60m), (refund.Reason, refund.PaymentId, refund.Amount));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OnlinePaymentRefunds.ToListAsync()));

        // Mercado Pago stays while the deposit is asked.
        Assert.Equal(HttpStatusCode.BadRequest, (await mine.Client.DeleteAsync($"{OnlinePayments}/account")).StatusCode);
    }

    [SqlServerFact]
    public async Task TwoDepositsForTheSameTimePaidAtOnce_ShouldBookItOnce_AndGiveTheOtherBack()
    {
        var mine = await _api.SignUpAsync("reserva-c");
        var (slug, service) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(mine.Client, "Corte a la vez");
        var account = await ConnectAsync(mine.Client);
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(Settings, new { enabled = true, capacity = 1, depositPercent = 50 }));
        using var ana = _api.AnonymousClient();
        using var beto = _api.AnonymousClient();
        var startAt = await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(ana, slug, service);

        // Two customers go to pay the same time (nothing holds it while they pay).
        var first = await TwoBusinessesApi.ReadAsync(await ana.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments/deposit", CrossTenantOnlineBookingSqlServerTests.Request(service, startAt, "5512340002")));
        var second = await TwoBusinessesApi.ReadAsync(await beto.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments/deposit", CrossTenantOnlineBookingSqlServerTests.Request(service, startAt, "5512340003")));
        var firstPayment = _api.OnlinePayments.Approve(first.GetProperty("checkoutId").GetGuid(), 100m, account);
        var secondPayment = _api.OnlinePayments.Approve(second.GetProperty("checkoutId").GetGuid(), 100m, account);

        // Both come back at once, each request with its own context applying its own payment.
        var answers = await Task.WhenAll(
            ana.PostAsJsonAsync($"/api/v1/public/menu/{slug}/appointments/deposit/{first.GetProperty("checkoutId").GetGuid()}/confirm",
                new { paymentId = firstPayment.Id, returnKey = first.GetProperty("returnKey").GetString() }),
            beto.PostAsJsonAsync($"/api/v1/public/menu/{slug}/appointments/deposit/{second.GetProperty("checkoutId").GetGuid()}/confirm",
                new { paymentId = secondPayment.Id, returnKey = second.GetProperty("returnKey").GetString() }));
        var statuses = new List<int>();
        foreach (var answer in answers)
            statuses.Add((await TwoBusinessesApi.ReadAsync(answer)).GetProperty("status").GetInt32());
        Assert.Equal(new[] { (int)OnlineCheckoutStatus.Placed, (int)OnlineCheckoutStatus.NotPlaced }.Order(), statuses.Order());

        // One request with its deposit held; the other payment goes back whole.
        var appointment = Assert.Single(await _api.QueryAsync(mine.TenantId, db => db.Appointments.ToListAsync()));
        Assert.Equal((startAt, AppointmentDepositStatus.Held), (appointment.StartAt, appointment.DepositStatus!.Value));
        var refund = Assert.Single(await _api.QueryAsync(mine.TenantId, db => db.OnlinePaymentRefunds.ToListAsync()));
        var booked = await _api.QueryAsync(mine.TenantId, db => db.OnlineCheckouts.SingleAsync(c => c.AppointmentId == appointment.Id));
        Assert.Equal(booked.PaymentId == firstPayment.Id ? secondPayment.Id : firstPayment.Id, refund.PaymentId);
        Assert.Equal((OnlinePaymentRefundReason.NotPlaced, 100m), (refund.Reason, refund.Amount));
    }

    [SqlServerFact]
    public async Task Cancelling_ShouldDoWhatThePageSaid_OrNothing_ByTheLinkAndFromTheAccount()
    {
        var mine = await _api.SignUpAsync("reserva-cancela");
        var (slug, service) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(mine.Client, "Corte que se cancela");
        var account = await ConnectAsync(mine.Client);
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(Settings, new { enabled = true, capacity = 1, depositPercent = 30 }));
        using var customer = _api.AnonymousClient();
        var ana = await _api.SignUpCustomerAsync("reserva-ana");
        // Two requests booked paying their deposit: one followed by its link, the other from Ana's account.
        var link = (await PaidRequestAsync(customer, slug, service, "5512340004", account)).GetProperty("trackingToken").GetString()!;
        await PaidRequestAsync(ana.Client, slug, service, "5512340005", account);
        var anas = Assert.Single((await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync("/api/v1/me/appointments"))).GetProperty("items").EnumerateArray());
        var anasId = anas.GetProperty("id").GetGuid();
        // Still requests, cancelling gives the deposit back whatever the hour.
        Assert.True(anas.GetProperty("cancelReturnsDeposit").GetBoolean());

        // A page that said the business keeps it (opened before, or another rule): nothing is cancelled, and it says why.
        var stale = await customer.PostAsJsonAsync($"{CrossTenantOnlineBookingSqlServerTests.Tracking}/cancel", new { token = link, expectDepositBack = false });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        Assert.Contains("te devolvemos tu anticipo", await stale.Content.ReadAsStringAsync());
        var staleFromAccount = await ana.Client.PostAsJsonAsync($"/api/v1/me/appointments/{anasId}/cancel", new { expectDepositBack = false });
        Assert.Equal(HttpStatusCode.BadRequest, staleFromAccount.StatusCode);
        Assert.All(await _api.QueryAsync(mine.TenantId, db => db.Appointments.ToListAsync()), appointment =>
            Assert.Equal((AppointmentStatus.Requested, (AppointmentDepositStatus?)AppointmentDepositStatus.Held), (appointment.Status, appointment.DepositStatus)));
        // A link nobody has is not found, whatever it says of the deposit.
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync($"{CrossTenantOnlineBookingSqlServerTests.Tracking}/cancel",
            new { token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE", expectDepositBack = true })).StatusCode);

        // With what the server holds, both are cancelled and their deposits go back.
        var byLink = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"{CrossTenantOnlineBookingSqlServerTests.Tracking}/cancel", new { token = link, expectDepositBack = true }));
        var fromAccount = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsJsonAsync(
            $"/api/v1/me/appointments/{anasId}/cancel", new { expectDepositBack = true }));
        Assert.All(new[] { byLink, fromAccount }, cancelled => Assert.Equal(
            ("La cancelaste", (int)AppointmentDepositStatus.Returned),
            (cancelled.GetProperty("statusLabel").GetString(), cancelled.GetProperty("depositStatus").GetInt32())));
    }

    /// <returns>The receipt of a request booked by paying its deposit, from the customer's return.</returns>
    private async Task<JsonElement> PaidRequestAsync(HttpClient customer, string slug, Guid service, string phone, long account)
    {
        var startAt = await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(customer, slug, service);
        var started = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments/deposit", CrossTenantOnlineBookingSqlServerTests.Request(service, startAt, phone)));
        var checkoutId = started.GetProperty("checkoutId").GetGuid();
        var payment = _api.OnlinePayments.Approve(checkoutId, started.GetProperty("amount").GetDecimal(), account);
        var status = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments/deposit/{checkoutId}/confirm",
            new { paymentId = payment.Id, returnKey = started.GetProperty("returnKey").GetString() }));
        Assert.Equal((int)OnlineCheckoutStatus.Placed, status.GetProperty("status").GetInt32());
        return status.GetProperty("receipt");
    }

    /// <returns>The account Mercado Pago connected.</returns>
    private static async Task<long> ConnectAsync(HttpClient owner)
    {
        var started = await TwoBusinessesApi.ReadAsync(await owner.PostAsync($"{OnlinePayments}/connect", null));
        var state = Guid.ParseExact(HttpUtility.ParseQueryString(new Uri(started.GetProperty("authorizationUrl").GetString()!).Query)["state"]!, "N");
        var connected = await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync($"{OnlinePayments}/connect/complete", new { state, code = "TG-code" }));
        return connected.GetProperty("accountId").GetInt64();
    }
}
