using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Visits to quote requested online (Fase 29B, ADR 0053) through the API, without an account: a business's request page
/// answers only for its own zones and times, the visit lands only in its own agenda with how it is charged and where its
/// quote goes, its link says the postal code and never the address, and the page goes away once the visits are off.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantHomeVisitBookingSqlServerTests
{
    private const string Settings = "/api/v1/appointments/home-visit-requests";

    private readonly TwoBusinessesApi _api;

    public CrossTenantHomeVisitBookingSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task AVisitRequestedOnline_ShouldGoOnlyWhereItsBusinessGoes_AndOnlyToItsAgenda()
    {
        var mine = await _api.SignUpAsync("solicitud-a");
        var theirs = await _api.SignUpAsync("solicitud-b");
        var mySlug = await ReceiveVisitsAsync(mine.Client, "72000", 50m, chargePolicy: 1);
        var theirSlug = await ReceiveVisitsAsync(theirs.Client, "72001", 80m, chargePolicy: 0);
        using var stranger = _api.AnonymousClient();

        var page = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{mySlug}/home-visit"));
        Assert.Equal(("Visita a domicilio", 60, 1), (page.GetProperty("visitName").GetString(),
            page.GetProperty("durationMinutes").GetInt32(), page.GetProperty("chargePolicy").GetInt32()));
        // The other business goes to 72001, this one does not.
        var elsewhere = await TwoBusinessesApi.ReadAsync(
            await stranger.GetAsync($"/api/v1/public/menu/{mySlug}/home-visit/coverage?postalCode=72001"));
        Assert.False(elsewhere.GetProperty("covered").GetBoolean());
        var here = await TwoBusinessesApi.ReadAsync(
            await stranger.GetAsync($"/api/v1/public/menu/{mySlug}/home-visit/coverage?postalCode=72000"));
        Assert.Equal((true, 0m), (here.GetProperty("covered").GetBoolean(), here.GetProperty("price").GetDecimal()));

        var startAt = await FirstFreeTimeAsync(stranger, mySlug);
        var refused = await stranger.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/home-visits", Request(startAt, "72001"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Por ahora no hacemos visitas en el 72001.", await refused.Content.ReadAsStringAsync());

        var receipt = await TwoBusinessesApi.ReadAsync(
            await stranger.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/home-visits", Request(startAt, "72000")));
        Assert.Equal(("72000", JsonValueKind.Null), (receipt.GetProperty("visit").GetProperty("postalCode").GetString(),
            receipt.GetProperty("address").ValueKind));
        var tracked = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/appointments/tracking",
            new { token = receipt.GetProperty("trackingToken").GetString() }));
        Assert.Equal("72000", tracked.GetProperty("visit").GetProperty("postalCode").GetString());
        // The link never shows the address: it opens without an account.
        Assert.DoesNotContain("Juárez", tracked.GetRawText());

        // Only its own agenda has it, «Sin costo» as agreed, with where its quote goes.
        var myRequests = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync("/api/v1/appointments?status=5")))
            .GetProperty("items").EnumerateArray().ToList();
        var request = Assert.Single(myRequests);
        Assert.Equal(("valeria@correo.mx", 1, 0m), (request.GetProperty("customerEmail").GetString(),
            request.GetProperty("visit").GetProperty("chargePolicy").GetInt32(), request.GetProperty("visit").GetProperty("fee").GetDecimal()));
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync("/api/v1/appointments?status=5")))
            .GetProperty("items").EnumerateArray());

        // Visits off: the other business's page goes away; this one's stays.
        await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync(Settings, new { enabled = false, serviceId = (Guid?)null, chargePolicy = 0 }));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/public/menu/{theirSlug}/home-visit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync($"/api/v1/public/menu/{mySlug}/home-visit")).StatusCode);
    }

    private static object Request(string startAt, string postalCode) => new
    {
        startAt,
        postalCode,
        addressLine = "Av. Juárez 123, Col. Centro",
        problem = "Gotea el calentador",
        customerName = "Valeria Ruiz",
        customerPhone = "5522107765",
        customerEmail = "valeria@correo.mx",
        idempotencyKey = Guid.NewGuid(),
        // What the page said: «Sin costo».
        expectedChargePolicy = 1,
        expectedPrice = 0m
    };

    /// <summary>The first free time of the visit the request page offers this week or the next.</summary>
    private static async Task<string> FirstFreeTimeAsync(HttpClient client, string slug)
    {
        string? from = null;
        for (var week = 0; week < 2; week++)
        {
            var slots = await TwoBusinessesApi.ReadAsync(
                await client.GetAsync($"/api/v1/public/menu/{slug}/home-visit/slots{(from is null ? string.Empty : $"?from={from}")}"));
            var first = slots.GetProperty("days").EnumerateArray().SelectMany(day => day.GetProperty("slots").EnumerateArray()).FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
                return first.GetProperty("startAt").GetString()!;
            from = slots.GetProperty("nextFrom").GetString();
        }
        throw new InvalidOperationException("The visit offered no free time.");
    }

    /// <returns>The slug of the business's menu, once it receives visits to a postal code, every day from 8 to 20.</returns>
    private static async Task<string> ReceiveVisitsAsync(HttpClient owner, string postalCode, decimal fee, int chargePolicy)
    {
        var slug = (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync("/api/v1/auth/my-tenant/online-menu", new { mode = 1 })))
            .GetProperty("slug").GetString()!;
        await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync("/api/v1/auth/my-tenant/opening-hours",
            new { intervals = Enumerable.Range(0, 7).Select(day => new { day, opens = "08:00", closes = "20:00" }) }));
        await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync("/api/v1/delivery-zones",
            new { name = $"Zona {postalCode}", fee, minimumOrder = 0m, freeDeliveryFrom = (decimal?)null, postalCodes = new[] { postalCode } }));
        var settings = await TwoBusinessesApi.ReadAsync(
            await owner.PutAsJsonAsync(Settings, new { enabled = true, serviceId = (Guid?)null, chargePolicy }));
        Assert.True(settings.GetProperty("receivesVisits").GetBoolean(), settings.GetRawText());
        return slug;
    }
}
