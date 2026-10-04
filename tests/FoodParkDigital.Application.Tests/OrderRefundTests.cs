using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>Refunds compensate payments and lines without editing them, through the drawer and inventory.</summary>
public class OrderRefundTests
{
    [Fact]
    public async Task PartialRefund_ShouldLeaveThePaymentsAndTakeCashOutOfTheDrawer()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var drawer = await fixture.OpenCashSessionAsync(tenant);
        var sale = await SellAsync(fixture, soda.Id, 3, (60m, OrderPaymentMethod.Cash));
        var line = Assert.Single(sale.Items);

        var refund = await RefundHandler(fixture).Handle(new RefundOrderCommand
        {
            OrderId = sale.Id,
            Amount = 20m,
            Method = OrderPaymentMethod.Cash,
            Reason = "Refresco caducado",
            Items = [new RefundItemDto { OrderItemId = line.Id, Quantity = 1 }]
        }, CancellationToken.None);

        Assert.True(refund.IsSuccess, refund.Error);
        Assert.Equal(60m, refund.Value.PaidAmount);
        Assert.Equal(20m, refund.Value.RefundedAmount);
        Assert.Equal(40m, refund.Value.NetPaidAmount);
        Assert.Equal(0m, refund.Value.Balance);
        Assert.Equal(1, Assert.Single(refund.Value.Items).ReturnedQuantity);
        var recorded = Assert.Single(refund.Value.Refunds);
        Assert.Equal("Refresco caducado", recorded.Reason);
        Assert.Equal("cajero", recorded.RefundedBy);
        var movements = await fixture.Context.CashTransactions.AsNoTracking().Where(t => t.CashSessionId == drawer.Id).ToListAsync();
        Assert.Contains(movements, t => t.TransactionType == CashTransactionType.Refund && t.Amount == -20m);
        Assert.Single(await fixture.Context.OrderPayments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void CanCancel_ShouldDependOnTheMoneyStillKept()
    {
        var order = new OrderDto
        {
            OrderNumber = "V-1",
            CustomerName = "Mostrador",
            BusinessType = BusinessType.Retail,
            OrderType = OrderType.Retail,
            Status = OrderStatus.Pending,
            PaidAmount = 50m,
            NetPaidAmount = 50m
        };

        order.ApplyStateMetadata();
        Assert.False(order.CanCancel);

        order.RefundedAmount = 50m;
        order.NetPaidAmount = 0m;
        order.ApplyStateMetadata();
        Assert.True(order.CanCancel);
    }

    [Fact]
    public async Task Refund_ShouldNotExceedWhatWasPaidAndKept()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var sale = await SellAsync(fixture, soda.Id, 1, (20m, OrderPaymentMethod.Cash));

        var first = await RefundHandler(fixture).Handle(Refund(sale.Id, 15m), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var second = await RefundHandler(fixture).Handle(Refund(sale.Id, 10m), CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.False(second.IsSuccess);
        Assert.Contains("no puede ser mayor", second.Error);
    }

    [Fact]
    public async Task Staff_ShouldNeedTheRefundPermission()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var sale = await SellAsync(fixture, soda.Id, 1, (20m, OrderPaymentMethod.Card));
        fixture.CurrentUser.Role = "STAFF";
        fixture.CurrentUser.TenantRole = "STAFF";

        var denied = await RefundHandler(fixture, grantsRefunds: false).Handle(Refund(sale.Id, 5m, OrderPaymentMethod.Card), CancellationToken.None);
        var allowed = await RefundHandler(fixture, grantsRefunds: true).Handle(Refund(sale.Id, 5m, OrderPaymentMethod.Card), CancellationToken.None);

        Assert.False(denied.IsSuccess);
        Assert.Equal(OrderRefundProcessor.NotAllowedMessage, denied.Error);
        Assert.True(allowed.IsSuccess, allowed.Error);
    }

    [Fact]
    public async Task Void_ShouldRefundEachPaymentWithItsMethodAndCancel()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen, pricesIncludeTax: true);
        var taco = await fixture.AddProductAsync(tenant, "Taco", 25m);
        var drawer = await fixture.OpenCashSessionAsync(tenant);
        // Paid while the kitchen prepares it: still cancellable.
        var sale = await SellAsync(fixture, taco.Id, 4, (60m, OrderPaymentMethod.Cash), (40m, OrderPaymentMethod.Card));
        Assert.Equal(OrderStatus.Confirmed, sale.Status);

        var voided = await VoidHandler(fixture).Handle(new VoidOrderCommand { OrderId = sale.Id, Reason = "El cliente se fue" }, CancellationToken.None);

        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal(OrderStatus.Cancelled, voided.Value.Status);
        Assert.Equal(0m, voided.Value.NetPaidAmount);
        Assert.Equal(2, voided.Value.Refunds.Count);
        Assert.Contains(voided.Value.Refunds, r => r.Method == OrderPaymentMethod.Cash && r.Amount == 60m);
        Assert.Contains(voided.Value.Refunds, r => r.Method == OrderPaymentMethod.Card && r.Amount == 40m);
        var session = await fixture.Context.CashSessions.Include(s => s.Transactions).AsNoTracking().SingleAsync(s => s.Id == drawer.Id);
        // Only the cash left the drawer: back to the opening amount.
        Assert.Equal(session.OpeningAmount, session.ExpectedBalance);
    }

    [Fact]
    public async Task Void_ShouldKeepADeliveredSaleDeliveredAndFullyRefunded()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var sale = await SellAsync(fixture, soda.Id, 2, (40m, OrderPaymentMethod.Cash));
        Assert.Equal(OrderStatus.Delivered, sale.Status);

        var voided = await VoidHandler(fixture).Handle(new VoidOrderCommand { OrderId = sale.Id, Reason = "Cobro duplicado" }, CancellationToken.None);

        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal(OrderStatus.Delivered, voided.Value.Status);
        Assert.Equal(0m, voided.Value.NetPaidAmount);
        Assert.Equal(2, Assert.Single(voided.Value.Items).ReturnedQuantity);
    }

    [Fact]
    public async Task ReturnedUnits_ShouldGoBackToInventoryOnce_EvenWhenTheRestIsVoidedLater()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen | TenantCapability.Inventory, pricesIncludeTax: true);
        var taco = await fixture.AddProductAsync(tenant, "Taco", 25m);
        var location = await fixture.Context.InventoryLocations.SingleAsync();
        var tortilla = new SupplyItem(tenant.Id, "Tortilla", UnitType.Unit);
        var stock = new InventoryStock(tenant.Id, tortilla.Id, location.Id);
        stock.ApplyMovement(100m, 1m, SalesTestFixture.Now);
        fixture.Context.SupplyItems.Add(tortilla);
        fixture.Context.InventoryStocks.Add(stock);
        fixture.Context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.Id, taco.Id, tortilla.Id, 2m));
        await fixture.Context.SaveChangesAsync();
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, NullLogger<OrderInventoryService>.Instance);

        var sale = await SellAsync(fixture, taco.Id, 5, inventory, (125m, OrderPaymentMethod.Cash));
        Assert.Equal(90m, await OnHandAsync(fixture, tortilla.Id));

        fixture.Context.ChangeTracker.Clear();
        var partial = await RefundHandler(fixture, inventory: inventory).Handle(new RefundOrderCommand
        {
            OrderId = sale.Id,
            Amount = 25m,
            Method = OrderPaymentMethod.Cash,
            Reason = "Un taco frío",
            RestockItems = true,
            Items = [new RefundItemDto { OrderItemId = sale.Items.Single().Id, Quantity = 1 }]
        }, CancellationToken.None);
        Assert.True(partial.IsSuccess, partial.Error);
        Assert.Equal(92m, await OnHandAsync(fixture, tortilla.Id));

        fixture.Context.ChangeTracker.Clear();
        var voided = await VoidHandler(fixture, inventory).Handle(new VoidOrderCommand { OrderId = sale.Id, Reason = "Cancelaron el pedido" }, CancellationToken.None);

        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal(OrderStatus.Cancelled, voided.Value.Status);
        // 10 tortillas consumed, 2 returned with the partial refund and 8 with the void: never more than consumed.
        Assert.Equal(100m, await OnHandAsync(fixture, tortilla.Id));
    }

    private static async Task<Orders.DTOs.OrderDto> SellAsync(
        SalesTestFixture fixture, Guid productId, int quantity, params (decimal Amount, OrderPaymentMethod Method)[] payments)
        => await SellAsync(fixture, productId, quantity, null, payments);

    private static async Task<Orders.DTOs.OrderDto> SellAsync(
        SalesTestFixture fixture,
        Guid productId,
        int quantity,
        IOrderInventoryService? inventory,
        params (decimal Amount, OrderPaymentMethod Method)[] payments)
    {
        var result = await fixture.CreateOrderHandler(inventory).Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = productId, Quantity = quantity }],
            Payments = payments.Select(p => new CreateOrderPaymentDto { Amount = p.Amount, Method = p.Method }).ToList()
        }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        fixture.Context.ChangeTracker.Clear();
        return result.Value;
    }

    private static RefundOrderCommand Refund(Guid orderId, decimal amount, OrderPaymentMethod method = OrderPaymentMethod.Cash) => new()
    {
        OrderId = orderId,
        Amount = amount,
        Method = method,
        Reason = "Ajuste de precio"
    };

    private static RefundOrderCommandHandler RefundHandler(
        SalesTestFixture fixture, bool grantsRefunds = false, IOrderInventoryService? inventory = null)
        => new(fixture.Context, fixture.CurrentUser, new RefundAccess(grantsRefunds), new OrderCashService(fixture.Context, fixture.CurrentUser),
            fixture.Clock, fixture.Mapper, inventory);

    private static VoidOrderCommandHandler VoidHandler(SalesTestFixture fixture, IOrderInventoryService? inventory = null)
        => new(fixture.Context, fixture.CurrentUser, new RefundAccess(false), new OrderCashService(fixture.Context, fixture.CurrentUser),
            fixture.Clock, fixture.Mapper, inventory);

    private static async Task<decimal> OnHandAsync(SalesTestFixture fixture, Guid supplyId)
        => (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync(s => s.SupplyItemId == supplyId)).OnHandQuantity;

    private sealed class RefundAccess(bool grantsRefunds) : ITenantAccessService
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(grantsRefunds && permission == TenantPermission.ProcessRefunds);

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
