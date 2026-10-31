using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Visits to quote (Fase 29A, ADR 0053) through the API: a business picks only one of its own services as its visit, its
/// setting stays its own, its store and its card in Explorar show only its own visit and postal codes, and dismissing the
/// notice of its published address changes only its own.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantHomeVisitRequestsSqlServerTests
{
    private const string Settings = "/api/v1/appointments/home-visit-requests";
    private const string DismissNotice = "/api/v1/auth/my-tenant/directory/home-visit-address-notice/dismiss";

    private readonly TwoBusinessesApi _api;

    public CrossTenantHomeVisitRequestsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Visits_ShouldUseItsOwnServices_AndShowOnlyInItsOwnStoreAndCard()
    {
        var mine = await _api.SignUpAsync("visitas-a");
        var theirs = await _api.SignUpAsync("visitas-b");
        var (mySlug, myVisit) = await VisitsAsync(mine.Client, "Visita y diagnóstico de A", "72000", 50m);
        var (theirSlug, theirVisit) = await VisitsAsync(theirs.Client, "Visita de B", "72001", 80m);

        // The service of the other business is not a visit here.
        var foreign = await mine.Client.PutAsJsonAsync(Settings, new { enabled = true, serviceId = theirVisit, chargePolicy = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        var saved = await TwoBusinessesApi.ReadAsync(
            await mine.Client.PutAsJsonAsync(Settings, new { enabled = true, serviceId = myVisit, chargePolicy = 2 }));
        Assert.Equal((true, 2, 250m), (saved.GetProperty("receivesVisits").GetBoolean(), saved.GetProperty("chargePolicy").GetInt32(),
            saved.GetProperty("price").GetProperty("amount").GetDecimal()));
        Assert.DoesNotContain(theirVisit.ToString(), saved.GetRawText());
        var theirSettings = await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(Settings));
        Assert.Equal((JsonValueKind.Null, 0, theirSlug), (theirSettings.GetProperty("serviceId").ValueKind,
            theirSettings.GetProperty("chargePolicy").GetInt32(), theirSettings.GetProperty("slug").GetString()));

        // Each store shows its own visit: never the service or the postal codes of the other business.
        using var stranger = _api.AnonymousClient();
        var myStore = (await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{mySlug}"))).GetProperty("homeVisits");
        Assert.Equal("Visita y diagnóstico de A", myStore.GetProperty("visitServiceName").GetString());
        Assert.Equal(["72000"], myStore.GetProperty("postalCodes").EnumerateArray().Select(code => code.GetString()));
        Assert.DoesNotContain("Visita de B", myStore.GetRawText());
        var theirStore = (await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{theirSlug}"))).GetProperty("homeVisits");
        Assert.Equal(["72001"], theirStore.GetProperty("postalCodes").EnumerateArray().Select(code => code.GetString()));

        // In Explorar, with a postal code, each card answers for its own zones.
        _api.ForgetDirectorySnapshot();
        var listed = (await TwoBusinessesApi.ReadAsync(await stranger.GetAsync("/api/v1/public/directory?q=visitas&postalCode=72000")))
            .GetProperty("businesses").GetProperty("items").EnumerateArray().ToList();
        var cards = listed.ToDictionary(card => card.GetProperty("slug").GetString()!);
        Assert.Equal((true, 250m), (cards[mySlug].GetProperty("visitsPostalCode").GetBoolean(),
            cards[mySlug].GetProperty("visitPrice").GetProperty("amount").GetDecimal()));
        Assert.False(cards[theirSlug].GetProperty("visitsPostalCode").GetBoolean());
        // The one that goes there first.
        Assert.Equal([mySlug, theirSlug], listed.Select(card => card.GetProperty("slug").GetString()).Where(slug => slug == mySlug || slug == theirSlug));

        // Both publish the address of a main branch with premises; dismissing the notice changes only one's own.
        Assert.True(theirSettings.GetProperty("addressNotice").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await mine.Client.PostAsync(DismissNotice, null)).StatusCode);
        Assert.False((await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(Settings))).GetProperty("addressNotice").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(Settings))).GetProperty("addressNotice").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.PostAsync(DismissNotice, null)).StatusCode);
    }

    /// <returns>The slug of the business's menu and a service it can pick as its visit.</returns>
    private static async Task<(string Slug, Guid Service)> VisitsAsync(HttpClient owner, string serviceName, string postalCode, decimal fee)
    {
        var slug = (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync("/api/v1/auth/my-tenant/online-menu", new { mode = 1 })))
            .GetProperty("slug").GetString()!;
        await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync("/api/v1/auth/my-tenant/opening-hours",
            new { intervals = Enumerable.Range(0, 7).Select(day => new { day, opens = "08:00", closes = "20:00" }) }));
        await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync("/api/v1/delivery-zones",
            new { name = $"Zona {postalCode}", fee, minimumOrder = 0m, freeDeliveryFrom = (decimal?)null, postalCodes = new[] { postalCode } }));
        var category = await TwoBusinessesApi.IdAsync(owner, "/api/v1/categories", new { name = $"Servicios de {serviceName}", sortOrder = 1 });
        var service = await TwoBusinessesApi.IdAsync(owner, "/api/v1/products", new
        {
            categoryId = category,
            name = serviceName,
            priceAmount = 200m,
            priceCurrency = "MXN",
            sortOrder = 1,
            unit = 52,
            preparationTimeMinutes = 60,
            taxObject = 2
        });
        var settings = await TwoBusinessesApi.ReadAsync(
            await owner.PutAsJsonAsync(Settings, new { enabled = true, serviceId = (Guid?)null, chargePolicy = 0 }));
        Assert.True(settings.GetProperty("receivesVisits").GetBoolean(), settings.GetRawText());
        return (slug, service);
    }
}
