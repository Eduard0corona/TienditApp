using FoodParkDigital.Application.Purchasing.Commands;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Purchasing.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>A purchase order goes from draft to sent, arrives in parts at the supplier's real price, or is cancelled before anything arrives.</summary>
public class PurchaseOrderLifecycleTests
{
    [Fact]
    public async Task PartialReception_ShouldValueTheStockAtTheActualCost()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory);
        var order = await CreateOrderAsync(fixture, tenant, quantity: 10m, unitCost: 12m, markAsSent: true);
        var line = Assert.Single(order.Lines);

        var partial = await ReceiveAsync(fixture, order.Id, line.Id, 4m, unitCost: 13m);

        Assert.True(partial.IsSuccess, partial.Error);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, partial.Value!.Status);
        var received = Assert.Single(partial.Value.Lines);
        Assert.Equal(4m, received.ReceivedQuantity);
        Assert.Equal(6m, received.PendingQuantity);
        Assert.Equal(13m, received.UnitCost);
        Assert.Equal(130m, partial.Value.Total);
        Assert.Equal(4m, (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity);
        Assert.Equal(13m, (await fixture.Context.InventoryMovementLines.AsNoTracking().SingleAsync()).UnitCost);

        var rest = await ReceiveAsync(fixture, order.Id, line.Id, 6m);

        Assert.True(rest.IsSuccess, rest.Error);
        Assert.Equal(PurchaseOrderStatus.Received, rest.Value!.Status);
        Assert.Equal(10m, (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public async Task Draft_ShouldBeSentAndThenCancelled()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory);
        var draft = await CreateOrderAsync(fixture, tenant, quantity: 3m, unitCost: 20m, markAsSent: false);
        Assert.Equal(PurchaseOrderStatus.Draft, draft.Status);

        var sent = await new SendPurchaseOrderCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new SendPurchaseOrderCommand(draft.Id), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var sentAgain = await Assert.ThrowsAsync<DomainException>(() => new SendPurchaseOrderCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new SendPurchaseOrderCommand(draft.Id), CancellationToken.None));
        fixture.Context.ChangeTracker.Clear();
        var cancelled = await CancelAsync(fixture, draft.Id);

        Assert.Equal(PurchaseOrderStatus.Sent, sent.Value!.Status);
        Assert.Equal("Solo una compra en borrador puede enviarse.", sentAgain.Message);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Value!.Status);
        Assert.Empty(await fixture.Context.InventoryStocks.ToListAsync());
    }

    [Fact]
    public async Task OrderWithGoodsReceived_ShouldNotBeCancelled()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory);
        var order = await CreateOrderAsync(fixture, tenant, quantity: 10m, unitCost: 12m, markAsSent: true);
        Assert.True((await ReceiveAsync(fixture, order.Id, order.Lines.Single().Id, 2m)).IsSuccess);

        var error = await Assert.ThrowsAsync<DomainException>(() => CancelAsync(fixture, order.Id));

        Assert.Equal("La compra ya tiene mercancía recibida; no se puede cancelar.", error.Message);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await fixture.Context.PurchaseOrders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task OrderOfAnotherBusiness_ShouldNotBeFound()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory, slug: "otro");
        var foreign = await CreateOrderAsync(fixture, other, quantity: 1m, unitCost: 5m, markAsSent: false);
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory, slug: "propio");

        await Assert.ThrowsAsync<EntityNotFoundException>(() => new SendPurchaseOrderCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new SendPurchaseOrderCommand(foreign.Id), CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => CancelAsync(fixture, foreign.Id));
    }

    private static async Task<PurchaseOrderDto> CreateOrderAsync(SalesTestFixture fixture, Tenant tenant, decimal quantity, decimal unitCost, bool markAsSent)
    {
        var supplier = new Supplier(tenant.Id, "Proveedor central");
        var supply = new SupplyItem(tenant.Id, "Detergente", UnitType.Liter, defaultCost: 10m);
        fixture.Context.Suppliers.Add(supplier);
        fixture.Context.SupplyItems.Add(supply);
        await fixture.Context.SaveChangesAsync();
        var location = await fixture.Context.InventoryLocations.SingleAsync(l => l.TenantId == tenant.Id);

        var result = await new CreatePurchaseOrderCommandHandler(fixture.Context, fixture.TenantService).Handle(new CreatePurchaseOrderCommand
        {
            SupplierId = supplier.Id,
            MarkAsSent = markAsSent,
            Lines = [new CreatePurchaseOrderLineRequest { SupplyItemId = supply.Id, InventoryLocationId = location.Id, Quantity = quantity, UnitCost = unitCost }]
        }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        fixture.Context.ChangeTracker.Clear();
        return result.Value!;
    }

    private static async Task<Application.Common.Models.Result<PurchaseOrderDto>> ReceiveAsync(
        SalesTestFixture fixture, Guid orderId, Guid lineId, decimal quantity, decimal? unitCost = null)
    {
        var result = await new ReceivePurchaseOrderCommandHandler(fixture.Context, fixture.TenantService).Handle(new ReceivePurchaseOrderCommand
        {
            PurchaseOrderId = orderId,
            Lines = [new ReceivePurchaseOrderLineRequest { PurchaseOrderLineId = lineId, QuantityReceived = quantity, UnitCost = unitCost }]
        }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Application.Common.Models.Result<PurchaseOrderDto>> CancelAsync(SalesTestFixture fixture, Guid orderId)
    {
        var result = await new CancelPurchaseOrderCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new CancelPurchaseOrderCommand(orderId), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }
}
