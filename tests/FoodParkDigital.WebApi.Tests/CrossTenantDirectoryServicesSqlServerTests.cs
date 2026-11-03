using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Explorar finds services (Fase 29I, ADR 0063) through the API, as a customer without an account reads it: the services of
/// a business that books online and of one that receives visits, each with its own business and where it opens; never
/// those of a business whose pages do not show them, even when its menu is in Explorar.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDirectoryServicesSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public CrossTenantDirectoryServicesSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Search_ShouldFindTheServicesOfWhoBooksOnlineOrReceivesVisits_EachWithItsOwnBusiness()
    {
        var salon = await _api.SignUpAsync("servicios-a");
        var (salonSlug, haircut) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(salon.Client, "Corte servicios29i");
        var plumber = await _api.SignUpAsync("servicios-b");
        var plumberSlug = await CrossTenantHomeVisitBookingSqlServerTests.ReceiveVisitsAsync(plumber.Client, "72000", 50m, chargePolicy: 1);
        var leaks = await ServiceAsync(plumber.Client, "Fugas servicios29i", 350m);
        // Its menu is in Explorar, but it books nobody online: its service is on no page.
        var shop = await _api.SignUpAsync("servicios-c");
        await TwoBusinessesApi.ReadAsync(await shop.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/online-menu", new { mode = 1 }));
        var shelf = await TwoBusinessesApi.IdAsync(shop.Client, "/api/v1/categories", new { name = "Productos", sortOrder = 1 });
        await TwoBusinessesApi.IdAsync(shop.Client, "/api/v1/products",
            new { categoryId = shelf, name = "Shampoo servicios29i", priceAmount = 90m, priceCurrency = "MXN", sortOrder = 1, taxObject = 2 });
        await ServiceAsync(shop.Client, "Peinado servicios29i", 250m);
        _api.ForgetDirectorySnapshot();
        using var stranger = _api.AnonymousClient();

        var found = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync("/api/v1/public/directory?q=servicios29i"));

        var services = found.GetProperty("services").EnumerateArray()
            .Select(s => (
                Name: s.GetProperty("name").GetString(),
                Kind: s.GetProperty("kind").GetInt32(),
                Slug: s.GetProperty("businessSlug").GetString(),
                Id: s.GetProperty("id").GetGuid(),
                Price: s.GetProperty("price").GetDecimal()))
            .OrderBy(s => s.Name)
            .ToList();
        Assert.Equal(
            [
                ("Corte servicios29i", 0, salonSlug, haircut, 200m),
                ("Fugas servicios29i", 1, plumberSlug, leaks, 350m)
            ],
            services);
        // The shop's product is found as a product; no service is.
        Assert.Equal("Shampoo servicios29i", Assert.Single(found.GetProperty("products").EnumerateArray()).GetProperty("name").GetString());
        // Its card shows nothing but what its pages already show.
        Assert.DoesNotContain("Peinado", found.GetRawText());
    }

    private static async Task<Guid> ServiceAsync(HttpClient owner, string name, decimal price)
    {
        var category = await TwoBusinessesApi.IdAsync(owner, "/api/v1/categories", new { name = $"Servicios de {name}", sortOrder = 1 });
        return await TwoBusinessesApi.IdAsync(owner, "/api/v1/products", new
        {
            categoryId = category,
            name,
            priceAmount = price,
            priceCurrency = "MXN",
            sortOrder = 1,
            unit = 52,
            preparationTimeMinutes = 60,
            taxObject = 2
        });
    }
}
