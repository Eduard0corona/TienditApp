using System.Net.Http.Json;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Reminding a business to answer a request online in time (Fase 29U, ADR 0075), through the messaging worker's pass and the
/// outbox: each owner is reminded of the requests of their own business that still wait an hour before their limit, once, and
/// never of another business's; a business that cannot answer them (its agenda off, or suspended) is not, until it can again.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantAnswerReminderSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public CrossTenantAnswerReminderSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task EachOwner_ShouldBeRemindedOnce_OfTheRequestsOfTheirOwnBusinessDueNow()
    {
        var mine = await _api.SignUpAsync("recuerda-a");
        var theirs = await _api.SignUpAsync("recuerda-b");
        var (mySlug, myService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(mine.Client, "Corte de recordatorio");
        var (theirSlug, theirService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(theirs.Client, "Masaje de recordatorio");
        using var stranger = _api.AnonymousClient();

        // Each business gets a request due now (it arrived 23 h 10 min ago, for two days from now: its limit comes in 50 min).
        // Theirs gets another that is not due yet (it arrived 22 hours ago), and mine one already answered.
        var myDue = await RequestAsync(stranger, mySlug, myService, "5512350001");
        var myAnswered = await RequestAsync(stranger, mySlug, myService, "5512350002");
        var theirDue = await RequestAsync(stranger, theirSlug, theirService, "5512350003");
        var theirNotYet = await RequestAsync(stranger, theirSlug, theirService, "5512350004");
        await ArrivedAsync(mine.TenantId, myDue, hoursAgo: 23.2);
        await ArrivedAsync(mine.TenantId, myAnswered, hoursAgo: 23.2);
        await ArrivedAsync(theirs.TenantId, theirDue, hoursAgo: 23.2);
        await ArrivedAsync(theirs.TenantId, theirNotYet, hoursAgo: 22);
        Assert.True((await mine.Client.PostAsync($"/api/v1/appointments/{myAnswered}/accept", null)).IsSuccessStatusCode);
        // The emails of their arrival go out first: only the reminders are counted below.
        await _api.DispatchOutboxAsync();
        var mineBefore = _api.PlainEmailsTo(mine.Email).Count;
        var theirsBefore = _api.PlainEmailsTo(theirs.Email).Count;

        Assert.True(await _api.RemindAnswersAsync() >= 2);
        await _api.DispatchOutboxAsync();

        var mineGot = _api.PlainEmailsTo(mine.Email).Skip(mineBefore).ToList();
        var theirsGot = _api.PlainEmailsTo(theirs.Email).Skip(theirsBefore).ToList();
        var myReminder = Assert.Single(mineGot);
        Assert.StartsWith("Responde a tiempo la solicitud de cita en ", myReminder.Subject);
        Assert.Contains("Corte de recordatorio", myReminder.Subject);
        var theirReminder = Assert.Single(theirsGot);
        Assert.Contains("Masaje de recordatorio", theirReminder.Subject);
        Assert.DoesNotContain(mineGot.Concat(theirsGot), email => email.Body.Contains("551235000"));
        Assert.Equal(
            (true, false),
            (await RemindedAsync(theirs.TenantId, theirDue), await RemindedAsync(theirs.TenantId, theirNotYet)));
        Assert.False(await RemindedAsync(mine.TenantId, myAnswered));

        // Once: another pass reminds them of nothing more.
        await _api.RemindAnswersAsync();
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(mine.Email).Skip(mineBefore));
        Assert.Single(_api.PlainEmailsTo(theirs.Email).Skip(theirsBefore));
    }

    [SqlServerFact]
    public async Task ABusinessThatCannotAnswer_ShouldNotBeReminded_UntilItCanAgain_BeforeTheLimit()
    {
        var off = await _api.SignUpAsync("recuerda-apagada");
        var suspended = await _api.SignUpAsync("recuerda-suspendida");
        var (offSlug, offService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(off.Client, "Corte sin agenda");
        var (suspendedSlug, suspendedService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(suspended.Client, "Masaje suspendido");
        using var stranger = _api.AnonymousClient();
        var offDue = await RequestAsync(stranger, offSlug, offService, "5512350011");
        var suspendedDue = await RequestAsync(stranger, suspendedSlug, suspendedService, "5512350012");
        await ArrivedAsync(off.TenantId, offDue, hoursAgo: 23.2);
        await ArrivedAsync(suspended.TenantId, suspendedDue, hoursAgo: 23.2);
        await _api.DispatchOutboxAsync();
        var offBefore = _api.PlainEmailsTo(off.Email).Count;

        // One turned its agenda off and the other was suspended: nobody there may answer them, so they are not reminded yet.
        await AgendaAsync(off.TenantId, on: false);
        await _api.QueryAsync(suspended.TenantId, db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Tenants SET Status = {(int)TenantStatus.Suspended} WHERE Id = {suspended.TenantId}"));
        await _api.RemindAnswersAsync();
        Assert.Equal(
            (false, false),
            (await RemindedAsync(off.TenantId, offDue), await RemindedAsync(suspended.TenantId, suspendedDue)));

        // The agenda on again before its limit: the next pass reminds it.
        await AgendaAsync(off.TenantId, on: true);
        await _api.RemindAnswersAsync();
        await _api.DispatchOutboxAsync();
        Assert.True(await RemindedAsync(off.TenantId, offDue));
        Assert.Contains("Corte sin agenda", Assert.Single(_api.PlainEmailsTo(off.Email).Skip(offBefore)).Subject);
        Assert.False(await RemindedAsync(suspended.TenantId, suspendedDue));
    }

    /// <summary>A customer's request online at <paramref name="slug"/>, at its first free time.</summary>
    private static async Task<Guid> RequestAsync(HttpClient customer, string slug, Guid service, string phone)
    {
        var receipt = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments",
            CrossTenantOnlineBookingSqlServerTests.Request(service, await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(customer, slug, service), phone)));
        return (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
                CrossTenantOnlineBookingSqlServerTests.Tracking, new { token = receipt.GetProperty("trackingToken").GetString() })))
            .GetProperty("id").GetGuid();
    }

    /// <summary>The request arrived <paramref name="hoursAgo"/>, for two days from now.</summary>
    private Task ArrivedAsync(Guid tenantId, Guid request, double hoursAgo)
        => _api.QueryAsync(tenantId, db => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Appointments
            SET CreatedAt = DATEADD(minute, {(int)(-hoursAgo * 60)}, SYSDATETIMEOFFSET()),
                StartAt = DATEADD(day, 2, SYSDATETIMEOFFSET()), EndAt = DATEADD(hour, 1, DATEADD(day, 2, SYSDATETIMEOFFSET())),
                RequestedStartAt = DATEADD(day, 2, SYSDATETIMEOFFSET())
            WHERE Id = {request}
            """));

    /// <summary>Turns the business's agenda (its appointments) on or off.</summary>
    private Task AgendaAsync(Guid tenantId, bool on)
        => _api.QueryAsync(tenantId, db => db.Database.ExecuteSqlInterpolatedAsync(on
            ? (FormattableString)$"UPDATE TenantSettings SET Capabilities = Capabilities | {(int)TenantCapability.Appointments} WHERE TenantId = {tenantId}"
            : $"UPDATE TenantSettings SET Capabilities = Capabilities & ~{(int)TenantCapability.Appointments} WHERE TenantId = {tenantId}"));

    private Task<bool> RemindedAsync(Guid tenantId, Guid request)
        => _api.QueryAsync(tenantId, db => db.Appointments.AsNoTracking().AnyAsync(a => a.Id == request && a.AnswerReminderSentAt != null));
}
