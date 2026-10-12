using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Errors;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The prices of the products in the apps of the delivery platforms (Fase 22F-2) through the API: each business lists and
/// changes only the prices of its own products, the business header of an owner changes nothing, Vender gets the catalog
/// with the prices of the platform and the order of the platform is charged with them, and the prices are for whoever
/// manages the catalog.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantPlatformPricesSqlServerTests
{
    private const string Password = "Secreto1";

    private readonly TwoBusinessesApi _api;

    public CrossTenantPlatformPricesSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Prices_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("precios-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("precios-b", TwoBusinessesApi.FoodBusiness);
        var myBurger = await ProductAsync(mine.Client, "Hamburguesa de A");
        var theirBurger = await ProductAsync(theirs.Client, "Hamburguesa de B");
        Assert.Equal(HttpStatusCode.NoContent, (await mine.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", Prices((myBurger, 22m)))).StatusCode);

        // The other owner, even with the header of the business (only the platform admin may use it), lists only their own
        // products, without my price.
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/v1/delivery-platforms/Rappi/prices");
        read.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
        var theirList = await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(read));
        var item = Assert.Single(theirList.GetProperty("items").EnumerateArray());
        Assert.Equal(theirBurger, item.GetProperty("productId").GetGuid());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("platformPrice").ValueKind);
        Assert.DoesNotContain(myBurger.ToString(), await theirs.Client.GetStringAsync("/api/v1/products?priceChannel=Rappi&pageSize=100"));

        // Their save of my product, with the header too, finds no such product and changes nothing.
        using var save = new HttpRequestMessage(HttpMethod.Put, "/api/v1/delivery-platforms/3/prices") { Content = JsonContent.Create(Prices((myBurger, 1m))) };
        save.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
        var refused = await theirs.Client.SendAsync(save);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(PlatformPriceErrors.ProductGone, JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await theirs.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", Prices((theirBurger, 19m)))).StatusCode);

        // Mine is as I left it, and each business's prices stay in it.
        var myList = await mine.Client.GetFromJsonAsync<JsonElement>("/api/v1/delivery-platforms/Rappi/prices");
        Assert.Equal(22m, Assert.Single(myList.GetProperty("items").EnumerateArray()).GetProperty("platformPrice").GetDecimal());
        Assert.Equal(
            new[] { mine.TenantId },
            await _api.QueryAsync(mine.TenantId, db => db.ProductPlatformPrices.Select(p => p.TenantId).ToListAsync()));
        Assert.Equal(
            new[] { (theirs.TenantId, 19m) },
            (await _api.QueryAsync(theirs.TenantId, db => db.ProductPlatformPrices.ToListAsync())).Select(p => (p.TenantId, p.Price)));
    }

    [SqlServerFact]
    public async Task OrderOfAPlatform_ShouldBeChargedWithTheAppsPrices_AsVenderShowsThem()
    {
        var owner = await _api.SignUpAsync("precios-vender", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(commission: 30m)));
        var burger = await ProductAsync(owner.Client, "Hamburguesa");
        var withOptions = await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync($"/api/v1/products/{burger}/option-groups", new
        {
            groups = new[]
            {
                new
                {
                    name = "Tamaño", minSelections = 1, maxSelections = 1,
                    options = new[] { new { name = "Sencilla", priceDelta = 0m }, new { name = "Doble", priceDelta = 10m } }
                }
            }
        }));
        var doble = withOptions.GetProperty("optionGroups")[0].GetProperty("options").EnumerateArray()
            .Single(o => o.GetProperty("name").GetString() == "Doble").GetProperty("id").GetGuid();
        var saved = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", new
        {
            products = new[] { new { productId = burger, price = (decimal?)22m, options = new[] { new { optionId = doble, priceDelta = (decimal?)14m } } } }
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        // Vender, for an order of Rappi, gets the catalog with Rappi's prices; at the counter, the menu's.
        var inRappi = Single(await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/products?isAvailable=true&priceChannel=Rappi&pageSize=500"), burger);
        var atTheCounter = Single(await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/products?isAvailable=true&pageSize=500"), burger);
        Assert.Equal((22m, 14m), (inRappi.GetProperty("priceAmount").GetDecimal(), DeltaOf(inRappi, doble)));
        Assert.Equal((15m, 10m), (atTheCounter.GetProperty("priceAmount").GetDecimal(), DeltaOf(atTheCounter, doble)));

        // The order of Rappi is charged what the app charged; a Vender with the menu's prices on screen is told a price changed.
        var line = new { productId = burger, quantity = 2, optionIds = new[] { doble } };
        var stale = await owner.Client.PostAsJsonAsync("/api/v1/orders", RappiOrder(line, "BC953", expectedTotal: 2 * (15m + 10m)));
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        var order = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/orders", RappiOrder(line, "BC953", expectedTotal: 2 * (22m + 14m))));
        Assert.Equal(72m, order.GetProperty("total").GetDecimal());
        Assert.Equal(21.60m, await _api.QueryAsync(owner.TenantId, db => db.PlatformOrderDetails.Select(d => d.Commission).SingleAsync()));
    }

    [SqlServerFact]
    public async Task Prices_ShouldBeForWhoeverManagesTheCatalog_AndVenderReadsThemToSell()
    {
        var owner = await _api.SignUpAsync("precios-equipo", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(commission: 30m)));
        var burger = await ProductAsync(owner.Client, "Hamburguesa");
        var catalog = await EmployeeAsync(owner, appRole: 2, "ManageCatalog");
        var cashier = await EmployeeAsync(owner, appRole: 3, "ManageOrders");
        var reports = await EmployeeAsync(owner, appRole: 2, "ViewReports");

        Assert.Equal(HttpStatusCode.NoContent, (await catalog.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", Prices((burger, 21m)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await catalog.GetAsync("/api/v1/delivery-platforms/Rappi/prices")).StatusCode);
        // Which platforms are on, to say so in Precios en apps; never the rates of the contracts.
        var channels = await catalog.GetAsync("/api/v1/delivery-platforms/channels");
        Assert.Equal(HttpStatusCode.OK, channels.StatusCode);
        Assert.DoesNotContain("commission", await channels.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await catalog.GetAsync("/api/v1/delivery-platforms")).StatusCode);

        foreach (var staff in new[] { cashier, reports })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/delivery-platforms/Rappi/prices")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", Prices((burger, 1m)))).StatusCode);
        }

        // Whoever sells reads the catalog with the platform's prices.
        var forSale = Single(await cashier.GetFromJsonAsync<JsonElement>("/api/v1/products?priceChannel=3&pageSize=100"), burger);
        Assert.Equal(21m, forSale.GetProperty("priceAmount").GetDecimal());
        Assert.Equal(21m, await _api.QueryAsync(owner.TenantId, db => db.ProductPlatformPrices.Select(p => p.Price).SingleAsync()));
    }

    [SqlServerFact]
    public async Task Prices_ShouldBeRefused_WhenTheyAreNotOfAPlatform_OrSaidWhole()
    {
        var owner = await _api.SignUpAsync("precios-validos", TwoBusinessesApi.FoodBusiness);
        var burger = await ProductAsync(owner.Client, "Hamburguesa");

        var counter = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Counter/prices", Prices((burger, 20m)));
        var counterList = await owner.Client.GetAsync("/api/v1/delivery-platforms/0/prices");
        var whatsAppCatalog = await owner.Client.GetAsync("/api/v1/products?priceChannel=WhatsApp");
        // Without the price, nothing is read as «back to the menu»: it has to be said with null.
        var withoutPrice = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", new { products = new[] { new { productId = burger } } });
        var thirdDecimal = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", Prices((burger, 20.005m)));

        foreach (var response in new[] { counter, counterList, whatsAppCatalog })
            await AssertValidationAsync(response, DeliveryPlatformSettingMessage);
        Assert.Equal(HttpStatusCode.BadRequest, withoutPrice.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, JsonDocument.Parse(await withoutPrice.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, thirdDecimal.StatusCode);
        Assert.Contains("'Hamburguesa' en Rappi", await thirdDecimal.Content.ReadAsStringAsync());
        Assert.Equal(0, await _api.QueryAsync(owner.TenantId, db => db.ProductPlatformPrices.CountAsync()));

        // Null goes back to the menu's price.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", Prices((burger, 20m)))).StatusCode);
        var back = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi/prices", new { products = new[] { new { productId = burger, price = (decimal?)null } } });
        Assert.Equal(HttpStatusCode.NoContent, back.StatusCode);
        Assert.Equal(0, await _api.QueryAsync(owner.TenantId, db => db.ProductPlatformPrices.CountAsync()));
    }

    private const string DeliveryPlatformSettingMessage = "Esa no es una plataforma de reparto.";

    private static object Prices(params (Guid Product, decimal Price)[] prices) => new
    {
        products = prices.Select(p => new { productId = p.Product, price = (decimal?)p.Price }).ToArray()
    };

    private static object Terms(decimal commission) => new
    {
        revision = 0,
        isActive = true,
        commissionPercent = commission,
        commissionBase = 0,
        commissionVatPercent = 16m,
        isrWithholdingPercent = 2.5m,
        vatWithholdingPercent = 50m
    };

    private static object RappiOrder(object line, string folio, decimal expectedTotal) => new
    {
        channel = (int)OrderChannel.Rappi,
        externalOrderNumber = folio,
        expectedTotal,
        items = new[] { line }
    };

    private static JsonElement Single(JsonElement page, Guid product)
        => page.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == product);

    private static decimal DeltaOf(JsonElement product, Guid option)
        => product.GetProperty("optionGroups").EnumerateArray().SelectMany(g => g.GetProperty("options").EnumerateArray())
            .Single(o => o.GetProperty("id").GetGuid() == option).GetProperty("priceDelta").GetDecimal();

    /// <summary>A member of the owner's team with the role and the permissions given, signed in with its own password.</summary>
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

    private static async Task AssertValidationAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
        Assert.Contains(message, problem.GetRawText());
    }
}
