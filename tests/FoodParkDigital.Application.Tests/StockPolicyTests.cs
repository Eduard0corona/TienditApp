using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>A sale whose recipe supplies are short is refused naming the supply, or goes through with a warning.</summary>
public class StockPolicyTests
{
    [Fact]
    public async Task BlockPolicy_ShouldRefuseTheSaleNamingTheSupply()
    {
        await using var fixture = SalesTestFixture.Create();
        var taco = await SeedTacoWithTortillasAsync(fixture, tortillasInStock: 4m);

        var error = await Assert.ThrowsAsync<DomainException>(() => SellAsync(fixture, taco, quantity: 3));

        Assert.Equal("No alcanza Tortilla: hay 4 y la venta necesita 6. Registra la entrada en Inventario antes de vender.", error.Message);
        Assert.Equal(4m, (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public async Task WarnPolicy_ShouldSellAndTellWhichSupplyRanOut()
    {
        await using var fixture = SalesTestFixture.Create();
        var taco = await SeedTacoWithTortillasAsync(fixture, tortillasInStock: 4m);
        var settings = await fixture.Context.TenantSettings.SingleAsync();
        settings.SetInsufficientStockPolicy(InsufficientStockPolicy.Warn);
        await fixture.Context.SaveChangesAsync();

        var result = await SellAsync(fixture, taco, quantity: 3);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["Tortilla quedó en -2"], result.Value!.StockWarnings);
        Assert.Equal(-2m, (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public async Task SaleCapturedOffline_ShouldNeverBeRefused()
    {
        await using var fixture = SalesTestFixture.Create();
        var taco = await SeedTacoWithTortillasAsync(fixture, tortillasInStock: 4m);

        var result = await SellAsync(fixture, taco, quantity: 3, capturedOfflineAt: SalesTestFixture.Now.AddMinutes(-20));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["Tortilla quedó en -2"], result.Value!.StockWarnings);
    }

    [Fact]
    public async Task EnoughStock_ShouldSellWithoutWarnings()
    {
        await using var fixture = SalesTestFixture.Create();
        var taco = await SeedTacoWithTortillasAsync(fixture, tortillasInStock: 10m);

        var result = await SellAsync(fixture, taco, quantity: 3);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!.StockWarnings);
        Assert.Equal(4m, (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public void SeveralShortages_ShouldBeListedWithTheirUnits()
    {
        var message = StockShortage.BlockMessage(
        [
            new StockShortage("Tortilla", UnitType.Unit, 6m, 4m),
            new StockShortage("Queso", UnitType.Kg, 0.4m, 0.25m)
        ]);

        Assert.Equal(
            "No alcanzan los insumos: Tortilla (hay 4, se necesitan 6); Queso (hay 0.25 kg, se necesitan 0.4 kg). Registra la entrada en Inventario antes de vender.",
            message);
    }

    [Fact]
    public async Task UpdateSalesPolicy_ShouldSwitchTheStockPolicyAndRecordIt()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory);

        var result = await new UpdateSalesPolicyCommandHandler(fixture.Context, fixture.CurrentUser, fixture.TenantService).Handle(
            new UpdateSalesPolicyCommand { DefaultTaxRate = 16m, AutoCloseCounterSales = true, InsufficientStockPolicy = InsufficientStockPolicy.Warn },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(InsufficientStockPolicy.Warn, result.Value!.InsufficientStockPolicy);
        Assert.Equal(InsufficientStockPolicy.Warn, (await fixture.Context.TenantSettings.SingleAsync()).InsufficientStockPolicy);
        Assert.Contains("\"InsufficientStockPolicy\":1", (await fixture.Context.AuditLogs.SingleAsync()).Metadata);
    }

    private static async Task<Product> SeedTacoWithTortillasAsync(SalesTestFixture fixture, decimal tortillasInStock)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory, pricesIncludeTax: true);
        var taco = await fixture.AddProductAsync(tenant, "Taco", 20m);
        var tortilla = new SupplyItem(tenant.Id, "Tortilla", UnitType.Unit, defaultCost: 1m);
        fixture.Context.SupplyItems.Add(tortilla);
        fixture.Context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.Id, taco.Id, tortilla.Id, 2m));
        var location = await fixture.Context.InventoryLocations.SingleAsync();
        var stock = new InventoryStock(tenant.Id, tortilla.Id, location.Id);
        stock.ApplyMovement(tortillasInStock, 1m, SalesTestFixture.Now);
        fixture.Context.InventoryStocks.Add(stock);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return taco;
    }

    private static Task<Application.Common.Models.Result<Application.Orders.DTOs.OrderDto>> SellAsync(
        SalesTestFixture fixture, Product product, int quantity, DateTimeOffset? capturedOfflineAt = null)
    {
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, NullLogger<OrderInventoryService>.Instance);
        return fixture.CreateOrderHandler(inventory).Handle(new CreateOrderCommand
        {
            IdempotencyKey = capturedOfflineAt is null ? null : Guid.NewGuid(),
            CapturedOfflineAt = capturedOfflineAt,
            Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = quantity }]
        }, CancellationToken.None);
    }
}
