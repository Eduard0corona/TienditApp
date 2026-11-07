using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// How soon a trade sends the quote after the visit, in Explorar and on its visit page (Fase 29S, ADR 0073), through the
/// API: the visits its customers request online and the quotes its team sends from them are what its card says, never those
/// of another business.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDirectoryQuoteTimeSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public CrossTenantDirectoryQuoteTimeSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Card_ShouldSayHowSoonATradeSentItsQuotes_AfterItsOwnVisitsOnly_AndItsVisitPageToo()
    {
        var quick = await _api.SignUpAsync("cotiza-a");
        var few = await _api.SignUpAsync("cotiza-b");
        var quickSlug = await CrossTenantHomeVisitBookingSqlServerTests.ReceiveVisitsAsync(quick.Client, "72010", 50m, chargePolicy: 1);
        var fewSlug = await CrossTenantHomeVisitBookingSqlServerTests.ReceiveVisitsAsync(few.Client, "72011", 50m, chargePolicy: 1);
        using var stranger = _api.AnonymousClient();

        // The same three customers ask each trade for a visit, and each team accepts them: one quotes the three right away
        // (before the visit, from what the customer told), the other only the first one, later. Each business counts its own.
        var visits = new List<(Guid Visit, Guid Quote)>();
        var fewsQuoted = Guid.Empty;
        for (var n = 0; n < 3; n++)
        {
            var mine = await VisitAsync(stranger, quickSlug, "72010", $"55300000{n:00}");
            await AcceptAsync(quick.Client, mine);
            visits.Add((mine, await QuoteAsync(quick.Client, mine)));
            var theirs = await VisitAsync(stranger, fewSlug, "72011", $"55300000{n:00}");
            await AcceptAsync(few.Client, theirs);
            if (n == 0)
            {
                await QuoteAsync(few.Client, theirs);
                fewsQuoted = theirs;
            }
        }
        // A visit counts once it took place: here they all ended an hour ago, so their quotes went out within the hour.
        await _api.QueryAsync(quick.TenantId, async db =>
        {
            foreach (var (visit, _) in visits)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Appointments SET StartAt = DATEADD(hour, -2, SYSDATETIMEOFFSET()), EndAt = DATEADD(hour, -1, SYSDATETIMEOFFSET()) WHERE Id = {visit}");
            return 0;
        });
        // So did the other trade's: its quote of the first customer is the last one that customer got, at either business.
        await _api.QueryAsync(few.TenantId, db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Appointments SET StartAt = DATEADD(hour, -2, SYSDATETIMEOFFSET()), EndAt = DATEADD(hour, -1, SYSDATETIMEOFFSET()) WHERE Id = {fewsQuoted}"));
        _api.ForgetDirectorySnapshot();

        var listed = (await TwoBusinessesApi.ReadAsync(await stranger.GetAsync("/api/v1/public/directory?q=cotiza&pageSize=48")))
            .GetProperty("businesses").GetProperty("items").EnumerateArray()
            .ToDictionary(b => b.GetProperty("slug").GetString()!, b => b.GetProperty("quoteTime"));
        var quickPage = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{quickSlug}/home-visit"));
        var fewPage = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{fewSlug}/home-visit"));

        // Within a day; fewer than three visits quoted say nothing, on the card and on the visit page.
        Assert.Equal(0, listed[quickSlug].GetInt32());
        Assert.Equal(JsonValueKind.Null, listed[fewSlug].ValueKind);
        Assert.Equal(0, quickPage.GetProperty("quoteTime").GetInt32());
        Assert.Equal(JsonValueKind.Null, fewPage.GetProperty("quoteTime").ValueKind);
    }

    [SqlServerFact]
    public async Task TheWait_ShouldRunFromTheEndOfEachVisit_WithLimitsThatLeaveOutExactlyADay()
    {
        var trade = await _api.SignUpAsync("cotiza-c");
        var slug = await CrossTenantHomeVisitBookingSqlServerTests.ReceiveVisitsAsync(trade.Client, "72012", 50m, chargePolicy: 1);
        using var stranger = _api.AnonymousClient();
        var visits = new List<Guid>();
        for (var n = 0; n < 4; n++)
        {
            var visit = await VisitAsync(stranger, slug, "72012", $"55600000{n:00}");
            await AcceptAsync(trade.Client, visit);
            await QuoteAsync(trade.Client, visit);
            visits.Add(visit);
        }
        // Their visits ended 23 hours, exactly a day, exactly three days and more than a week before their quotes went out:
        // half is within three days, not within a day.
        var waits = new[] { 23, 24, 72, 200 };
        await _api.QueryAsync(trade.TenantId, async db =>
        {
            for (var n = 0; n < visits.Count; n++)
            {
                var sentAt = await db.Quotes.Where(q => q.AppointmentId == visits[n]).Select(q => q.SentAt!.Value).SingleAsync();
                var end = sentAt.AddHours(-waits[n]);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Appointments SET StartAt = {end.AddHours(-1)}, EndAt = {end} WHERE Id = {visits[n]}");
            }
            return 0;
        });
        _api.ForgetDirectorySnapshot();

        var card = (await TwoBusinessesApi.ReadAsync(await stranger.GetAsync("/api/v1/public/directory?q=cotiza&pageSize=48")))
            .GetProperty("businesses").GetProperty("items").EnumerateArray()
            .Single(b => b.GetProperty("slug").GetString() == slug);

        Assert.Equal(1, card.GetProperty("quoteTime").GetInt32());
    }

    /// <returns>The visit a customer of <paramref name="phone"/> requested online at the first free time.</returns>
    private static async Task<Guid> VisitAsync(HttpClient stranger, string slug, string postalCode, string phone)
    {
        var startAt = await CrossTenantHomeVisitBookingSqlServerTests.FirstFreeTimeAsync(stranger, slug);
        var receipt = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync($"/api/v1/public/menu/{slug}/home-visits", new
        {
            startAt,
            postalCode,
            addressLine = "Av. Juárez 123, Col. Centro",
            problem = "Gotea el calentador",
            customerName = "Valeria Ruiz",
            customerPhone = phone,
            idempotencyKey = Guid.NewGuid(),
            expectedChargePolicy = 1,
            expectedPrice = 0m
        }));
        var tracked = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(
            "/api/v1/public/appointments/tracking", new { token = receipt.GetProperty("trackingToken").GetString() }));
        return tracked.GetProperty("id").GetGuid();
    }

    private static async Task AcceptAsync(HttpClient team, Guid visit)
        => Assert.Equal(HttpStatusCode.OK, (await team.PostAsync($"/api/v1/appointments/{visit}/accept", null)).StatusCode);

    /// <summary>The quote of the visit, with one line, sent.</summary>
    private static async Task<Guid> QuoteAsync(HttpClient team, Guid visit)
    {
        var draft = await TwoBusinessesApi.ReadAsync(await team.PostAsJsonAsync("/api/v1/quotes", new { appointmentId = visit }));
        var id = draft.GetProperty("id").GetGuid();
        await TwoBusinessesApi.ReadAsync(await team.PutAsJsonAsync($"/api/v1/quotes/{id}", new
        {
            customerName = draft.GetProperty("customerName").GetString(),
            customerPhone = draft.GetProperty("customerPhone").GetString(),
            validityDays = 15,
            lines = new[] { new { productId = (Guid?)null, description = "Cambio de calentador", quantity = 1m, unitPrice = 1160m } }
        }));
        await TwoBusinessesApi.ReadAsync(await team.PostAsync($"/api/v1/quotes/{id}/send", null));
        return id;
    }
}
