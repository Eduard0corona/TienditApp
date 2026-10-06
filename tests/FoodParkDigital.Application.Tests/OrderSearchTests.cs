using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

/// <summary>Searching runs on the server, over every page, and only inside the current business.</summary>
public class OrderSearchTests
{
    [Theory]
    [InlineData("ORD-000002", "Mariana López")]
    [InlineData("mariana", "Mariana López")]
    [InlineData("55 2222 3333", "Mariana López")]
    [InlineData("3333", "Mariana López")]
    public async Task SearchOrders_ShouldMatchFolioNameOrPhoneDigits(string search, string expectedCustomer)
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        await AddOrderAsync(fixture, other, "ORD-000002", "Mariana López", "+525522223333");
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "propio");
        await AddOrderAsync(fixture, tenant, "ORD-000001", "Público en general", null);
        await AddOrderAsync(fixture, tenant, "ORD-000002", "Mariana López", "+525522223333");

        var handler = new GetOrdersByBusinessQueryHandler(fixture.Context, fixture.TenantService);
        var result = await handler.Handle(new GetOrdersByBusinessQuery { Search = search }, CancellationToken.None);

        var order = Assert.Single(result.Items);
        Assert.Equal(expectedCustomer, order.CustomerName);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task SearchOrders_ShouldNotTreatShortNumbersAsPhones()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        await AddOrderAsync(fixture, tenant, "ORD-000001", "Cliente", "+525522223333");

        var handler = new GetOrdersByBusinessQueryHandler(fixture.Context, fixture.TenantService);
        var result = await handler.Handle(new GetOrdersByBusinessQuery { Search = "52" }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public void SearchOrders_ShouldRejectLongTerms()
    {
        var validator = new GetOrdersByBusinessQueryValidator();

        Assert.False(validator.Validate(new GetOrdersByBusinessQuery { Search = new string('a', 101) }).IsValid);
        Assert.True(validator.Validate(new GetOrdersByBusinessQuery { Search = "ORD-000001" }).IsValid);
    }

    [Fact]
    public async Task SearchProducts_ShouldMatchNameSkuOrExactBarcode()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco de cola 600 ml", 20m);
        soda.Update(soda.CategoryId, soda.Name, null, soda.PriceAmount, "MXN", null, "REF-600", "7501055300075", 0, false,
            null, null, null, null, null, null, null, null, null, false, false, false);
        await fixture.AddProductAsync(tenant, "Jabón de tocador", 18m);
        await fixture.Context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock);
        var byName = await handler.Handle(new GetProductsQuery { Search = "cola" }, CancellationToken.None);
        var bySku = await handler.Handle(new GetProductsQuery { Search = "ref-600" }, CancellationToken.None);
        var byBarcode = await handler.Handle(new GetProductsQuery { Search = "7501055300075" }, CancellationToken.None);
        var partialBarcode = await handler.Handle(new GetProductsQuery { Search = "75010" }, CancellationToken.None);

        Assert.Equal(soda.Id, Assert.Single(byName.Items).Id);
        Assert.Equal(soda.Id, Assert.Single(bySku.Items).Id);
        Assert.Equal(soda.Id, Assert.Single(byBarcode.Items).Id);
        Assert.Empty(partialBarcode.Items);
    }

    private static async Task AddOrderAsync(
        SalesTestFixture fixture,
        Tenant tenant,
        string orderNumber,
        string customerName,
        string? phone)
    {
        var order = new Order(
            tenantId: tenant.Id,
            customerUserId: null,
            temporaryCustomerId: phone is null ? null : Guid.NewGuid(),
            createdByBusiness: true,
            orderNumber: orderNumber,
            businessType: tenant.BusinessType,
            orderType: OrderType.Retail,
            customerName: customerName,
            customerPhone: phone,
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Producto", 1, 10m);
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
    }
}
