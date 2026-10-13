using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Errors;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Orders of Rappi, Uber Eats and DiDi Food through the API and SQL Server: the same folio of the same platform again
/// the same day answers 409 <c>possible_duplicate_order</c> (short folios repeat, so the client asks and sends the same
/// order again confirmed), and a platform the business does not take orders from is a business rule.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class PlatformOrdersSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public PlatformOrdersSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task SameFolioAgainToday_ShouldAnswer409PossibleDuplicateOrder_AndCreateTheOrderOnceConfirmed()
    {
        var (kitchen, product) = await KitchenWithRappiAsync("folio");
        var client = kitchen.Client;
        var key = Guid.NewGuid();

        var first = await client.PostAsJsonAsync("/api/v1/orders", RappiOrder(product, "BC953", Guid.NewGuid()));
        var repeated = await client.PostAsJsonAsync("/api/v1/orders", RappiOrder(product, "#bc 953", key));
        var confirmed = await client.PostAsJsonAsync("/api/v1/orders", RappiOrder(product, "#bc 953", key, confirm: true));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal(ApiProblem.ContentType, repeated.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ApiErrorCodes.PossibleDuplicateOrder, problem.GetProperty("code").GetString());
        Assert.Equal(PlatformOrderErrors.PossibleDuplicate, problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        var created = JsonDocument.Parse(await confirmed.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("BC953", created.GetProperty("platformOrder").GetProperty("externalOrderNumber").GetString());
        Assert.Equal((int)PlatformCollectedBy.Platform, created.GetProperty("platformOrder").GetProperty("collectedBy").GetInt32());
        var payment = created.GetProperty("payments")[0];
        Assert.Equal((int)OrderPaymentMethod.Platform, payment.GetProperty("method").GetInt32());
        Assert.Equal("BC953", payment.GetProperty("reference").GetString());
        // The warning saved nothing: two orders with the folio, the first and the confirmed one.
        Assert.Equal(2, await _api.QueryAsync(kitchen.TenantId, db => db.PlatformOrderDetails.CountAsync(d => d.ExternalOrderNumber == "BC953")));
    }

    [SqlServerFact]
    public async Task PlatformTheBusinessDoesNotTakeOrdersFrom_ShouldBeABusinessRule()
    {
        var (kitchen, product) = await KitchenWithRappiAsync("uber");

        var response = await kitchen.Client.PostAsJsonAsync("/api/v1/orders", new
        {
            channel = (int)OrderChannel.UberEats,
            externalOrderNumber = "BC953",
            items = new[] { new { productId = product, quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ApiErrorCodes.BusinessRule, problem.GetProperty("code").GetString());
        Assert.Equal(PlatformOrderErrors.NotActive(OrderChannel.UberEats), problem.GetProperty("detail").GetString());
    }

    /// <summary>
    /// A kitchen that signed up through the API, with a product and Rappi active. The screen that turns platforms on
    /// comes later: the setting is stored as it will store it.
    /// </summary>
    private async Task<(SignedUp Kitchen, Guid Product)> KitchenWithRappiAsync(string name)
    {
        var kitchen = await _api.SignUpAsync(name, TwoBusinessesApi.FoodBusiness);
        var category = await TwoBusinessesApi.IdAsync(kitchen.Client, "/api/v1/categories", new { name = "Comida", sortOrder = 1 });
        var product = await TwoBusinessesApi.IdAsync(
            kitchen.Client, "/api/v1/products", CrossTenantHttpSqlServerTests.NewProduct(category, "Paquete familiar"));
        await _api.QueryAsync(kitchen.TenantId, async db =>
        {
            db.DeliveryPlatformSettings.Add(new DeliveryPlatformSetting(
                kitchen.TenantId, OrderChannel.Rappi, isActive: true, PlatformTerms.WithFiscalDefaults(30m)));
            return await db.SaveChangesAsync();
        });
        return (kitchen, product);
    }

    private static object RappiOrder(Guid product, string folio, Guid key, bool confirm = false) => new
    {
        channel = (int)OrderChannel.Rappi,
        externalOrderNumber = folio,
        customerName = "Juan",
        idempotencyKey = key,
        confirmPossibleDuplicate = confirm,
        items = new[] { new { productId = product, quantity = 1 } }
    };
}
