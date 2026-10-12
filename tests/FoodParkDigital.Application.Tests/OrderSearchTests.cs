using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
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

    [Theory]
    [InlineData("#bc 953")]
    [InlineData("bc953")]
    [InlineData("BC 9")]
    [InlineData("953")]
    public async Task SearchOrders_ShouldFindTheFolioOfAPlatformsApp_TypedAnyWay(string search)
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        await AddPlatformOrderAsync(fixture, other, "ORD-000001", "BC953");
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "propio");
        await AddOrderAsync(fixture, tenant, "ORD-000001", "Público en general", null);
        var platform = await AddPlatformOrderAsync(fixture, tenant, "ORD-000002", "BC953");
        await AddPlatformOrderAsync(fixture, tenant, "ORD-000003", "XY120");

        var handler = new GetOrdersByBusinessQueryHandler(fixture.Context, fixture.TenantService);
        var result = await handler.Handle(new GetOrdersByBusinessQuery { Search = search }, CancellationToken.None);

        // Only this business's order, whatever the other one has.
        var order = Assert.Single(result.Items);
        Assert.Equal(platform.Id, order.Id);
        Assert.Equal("BC953", order.PlatformOrder!.ExternalOrderNumber);
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

    [Theory]
    [InlineData(OrderChannel.Counter, "ORD-000001")]
    [InlineData(OrderChannel.WhatsApp, "ORD-000002")]
    [InlineData(OrderChannel.Online, "ORD-000003")]
    public async Task ListOrders_ShouldFilterByChannel(OrderChannel channel, string expectedFolio)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        await AddOrderAsync(fixture, tenant, "ORD-000001", "Mostrador", null);
        await AddOrderAsync(fixture, tenant, "ORD-000002", "Chat", null, OrderChannel.WhatsApp);
        // A customer's own order always has its customer (here a temporary one, from the phone).
        await AddOrderAsync(fixture, tenant, "ORD-000003", "Menú en línea", "+525533334444", OrderChannel.Online, createdByBusiness: false);

        var handler = new GetOrdersByBusinessQueryHandler(fixture.Context, fixture.TenantService);
        var filtered = await handler.Handle(new GetOrdersByBusinessQuery { ChannelFilter = channel }, CancellationToken.None);
        var all = await handler.Handle(new GetOrdersByBusinessQuery(), CancellationToken.None);

        Assert.Equal(expectedFolio, Assert.Single(filtered.Items).OrderNumber);
        Assert.Equal(3, all.TotalCount);
    }

    [Fact]
    public void ListOrders_ShouldRejectAnUnknownChannel()
    {
        var validator = new GetOrdersByBusinessQueryValidator();

        Assert.False(validator.Validate(new GetOrdersByBusinessQuery { ChannelFilter = (OrderChannel)9 }).IsValid);
        Assert.True(validator.Validate(new GetOrdersByBusinessQuery { ChannelFilter = OrderChannel.Online }).IsValid);
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
        string? phone,
        OrderChannel channel = OrderChannel.Counter,
        bool createdByBusiness = true)
    {
        var order = new Order(
            tenantId: tenant.Id,
            customerUserId: null,
            temporaryCustomerId: phone is null ? null : Guid.NewGuid(),
            createdByBusiness: createdByBusiness,
            orderNumber: orderNumber,
            businessType: tenant.BusinessType,
            orderType: OrderType.Retail,
            customerName: customerName,
            customerPhone: phone,
            customerNotes: null,
            tableNumber: null,
            channel: channel);
        order.AddItem(Guid.NewGuid(), "Producto", 1, 10m);
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>An order of Uber Eats with the folio of its app, as Vender registers it.</summary>
    private static async Task<Order> AddPlatformOrderAsync(SalesTestFixture fixture, Tenant tenant, string orderNumber, string folio)
    {
        var order = new Order(
            tenantId: tenant.Id,
            customerUserId: null,
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: orderNumber,
            businessType: tenant.BusinessType,
            orderType: OrderType.Retail,
            customerName: "Público en general",
            customerPhone: null,
            customerNotes: null,
            tableNumber: null,
            channel: OrderChannel.UberEats);
        order.AddItem(Guid.NewGuid(), "Paquete", 1, 100m);
        var setting = new DeliveryPlatformSetting(tenant.Id, OrderChannel.UberEats, true, PlatformTerms.WithFiscalDefaults(25m));
        order.RegisterPlatformOrder(setting, PlatformOrderNumber.From(folio), PlatformCollectedBy.Platform, SalesTestFixture.Now, null);
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
        return order;
    }
}
