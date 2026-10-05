using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Folios come from the counters of each business: they continue after the folios a business already had, are
/// independent between businesses, and a sale refused by its checks does not use one.
/// </summary>
public class FolioTests
{
    [Fact]
    public async Task Folios_ShouldContinueAfterTheExistingOnes_AndBePerBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var cafe = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe");
        // An order stored before the counter existed (its folio came from counting the orders).
        fixture.Context.Orders.Add(new Order(
            cafe.Id, null, null, true, "ORD-000041", BusinessType.Food, OrderType.Retail, "Público en general", null, null, null));
        await fixture.Context.SaveChangesAsync();
        var coffee = await fixture.AddProductAsync(cafe, "Café", 30m);

        var first = await SellAsync(fixture, coffee.Id);
        var second = await SellAsync(fixture, coffee.Id);

        var shop = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "tienda");
        var soap = await fixture.AddProductAsync(shop, "Jabón", 20m);
        var otherBusiness = await SellAsync(fixture, soap.Id);

        Assert.Equal("ORD-000042", first);
        Assert.Equal("ORD-000043", second);
        Assert.Equal("ORD-000001", otherBusiness);
    }

    [Fact]
    public async Task RefusedSale_ShouldNotUseAFolio()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 20m);

        var refused = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = soap.Id, Quantity = 1 }],
            ExpectedTotal = 19m
        }, CancellationToken.None);
        var sold = await SellAsync(fixture, soap.Id);

        Assert.False(refused.IsSuccess);
        Assert.Equal("ORD-000001", sold);
    }

    [Fact]
    public void StoredOrder_ShouldKeepItsFolio_AndNoOrderIsStoredWithoutOne()
    {
        var stored = new Order(Guid.NewGuid(), null, null, true, "ORD-000007", BusinessType.Retail, OrderType.Retail, "Ana", null, null, null);
        var unnumbered = new Order(Guid.NewGuid(), null, null, true, Order.NumberNotAssigned, BusinessType.Retail, OrderType.Retail, "Ana", null, null, null);

        Assert.Throws<DomainException>(() => stored.AssignNumber("ORD-000008"));
        Assert.Throws<DomainException>(unnumbered.MarkAsCreated);
        unnumbered.AssignNumber("ORD-000001");
        unnumbered.MarkAsCreated();
        Assert.Equal("ORD-000001", unnumbered.OrderNumber);
    }

    [Fact]
    public async Task DailyFolios_ShouldContinueAfterTheDay_AndStartAgainTheNextOne()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        fixture.Context.InventoryMovements.Add(new InventoryMovement(
            tenant.Id, "INV-20261004-0005", InventoryMovementType.Entry, InventoryMovementReason.Purchase, SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();
        var folios = new FolioGenerator(fixture.Context);

        var sameDay = await folios.NextInventoryMovementNumberAsync(tenant.Id, SalesTestFixture.Now, CancellationToken.None);
        // Two movements saved together (a sale consuming and returning) used to get the same folio by counting.
        var sameSave = await folios.NextInventoryMovementNumberAsync(tenant.Id, SalesTestFixture.Now, CancellationToken.None);
        var nextDay = await folios.NextInventoryMovementNumberAsync(tenant.Id, SalesTestFixture.Now.AddDays(1), CancellationToken.None);
        var purchase = await folios.NextPurchaseOrderNumberAsync(tenant.Id, SalesTestFixture.Now, CancellationToken.None);

        Assert.Equal("INV-20261004-0006", sameDay);
        Assert.Equal("INV-20261004-0007", sameSave);
        Assert.Equal("INV-20261005-0001", nextDay);
        Assert.Equal("PO-20261004-0001", purchase);
    }

    private static async Task<string> SellAsync(SalesTestFixture fixture, Guid productId)
    {
        var result = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = productId, Quantity = 1 }]
        }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        fixture.Context.ChangeTracker.Clear();
        return result.Value!.OrderNumber;
    }
}
