using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;

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

        // Three customers ask each trade for a visit, and each team accepts them: one quotes the three right away (before the
        // visit, from what the customer told), the other only one.
        for (var n = 0; n < 3; n++)
        {
            var mine = await VisitAsync(stranger, quickSlug, "72010", $"55300000{n:00}");
            await AcceptAsync(quick.Client, mine);
            await QuoteAsync(quick.Client, mine);
            var theirs = await VisitAsync(stranger, fewSlug, "72011", $"55400000{n:00}");
            await AcceptAsync(few.Client, theirs);
            if (n == 0)
                await QuoteAsync(few.Client, theirs);
        }
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
    private static async Task QuoteAsync(HttpClient team, Guid visit)
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
    }
}
