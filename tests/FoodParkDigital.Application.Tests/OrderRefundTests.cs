using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Refunds compensate payments and lines without editing them, through the drawer and inventory. What a delivery
/// platform charged goes back as «Plataforma», outside every drawer.
/// </summary>
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
        await fixture.OpenCashSessionAsync(tenant);
        var sale = await SellAsync(fixture, soda.Id, 1, (20m, OrderPaymentMethod.Cash));

        var first = await RefundHandler(fixture).Handle(Refund(sale.Id, 15m), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var second = await RefundHandler(fixture).Handle(Refund(sale.Id, 10m), CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.False(second.IsSuccess);
        Assert.Contains("no puede ser mayor", second.Error);
    }

    [Fact]
    public async Task Refund_WithoutAnOpenDrawer_ShouldBeRefused_WhateverTheMethod()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var drawer = await fixture.OpenCashSessionAsync(tenant);
        var sale = await SellAsync(fixture, soda.Id, 2, (20m, OrderPaymentMethod.Cash), (20m, OrderPaymentMethod.Card));
        var session = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == drawer.Id);
        session.Close(SalesTestFixture.Now, "cajero", session.ExpectedBalance);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var cash = await RefundHandler(fixture).Handle(Refund(sale.Id, 10m), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var card = await RefundHandler(fixture).Handle(Refund(sale.Id, 10m, OrderPaymentMethod.Card), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var voided = await VoidHandler(fixture).Handle(new VoidOrderCommand { OrderId = sale.Id, Reason = "Cobro duplicado" }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();

        Assert.Equal(OrderRefundProcessor.NoOpenDrawerMessage, cash.Error);
        Assert.Equal(OrderRefundProcessor.NoOpenDrawerMessage, card.Error);
        Assert.Equal(OrderRefundProcessor.NoOpenDrawerMessage, voided.Error);
        Assert.Empty(await fixture.Context.OrderRefunds.ToListAsync());
        Assert.Equal(40m, (await fixture.Context.Orders.SingleAsync()).NetPaidAmount);

        // With a drawer open again the refund goes through, out of that drawer.
        var reopened = await fixture.OpenCashSessionAsync(tenant);
        var refunded = await RefundHandler(fixture).Handle(Refund(sale.Id, 10m, OrderPaymentMethod.Card), CancellationToken.None);

        Assert.True(refunded.IsSuccess, refunded.Error);
        var movement = Assert.Single(await fixture.Context.CashTransactions.Where(t => t.CashSessionId == reopened.Id).ToListAsync());
        Assert.Equal(CashTransactionType.Refund, movement.TransactionType);
        Assert.Equal(-10m, movement.Amount);
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
        await fixture.OpenCashSessionAsync(tenant);
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
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context), NullLogger<OrderInventoryService>.Instance);
        await fixture.OpenCashSessionAsync(tenant);

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

    [Fact]
    public async Task VoidOfAnOrderThePlatformCharged_ShouldGiveItBackOutsideEveryDrawer_AndCancelIt()
    {
        await using var fixture = SalesTestFixture.Create();
        // With a kitchen screen, the paid order waits in Confirmed until it is ready: still cancellable.
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen, pricesIncludeTax: true);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 350m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var sale = await SellPlatformOrderAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id));
        Assert.Equal(OrderStatus.Confirmed, sale.Status);

        // No drawer is open: what Rappi charged never entered one, and its refund takes nothing out of one.
        var voided = await VoidHandler(fixture).Handle(
            new VoidOrderCommand { OrderId = sale.Id, Reason = "Rappi lo canceló", RestockItems = false }, CancellationToken.None);

        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal(OrderStatus.Cancelled, voided.Value.Status);
        Assert.Equal(0m, voided.Value.NetPaidAmount);
        var refund = Assert.Single(voided.Value.Refunds);
        Assert.Equal((OrderPaymentMethod.Platform, 350m, "Rappi lo canceló"), (refund.Method, refund.Amount, refund.Reason));
        Assert.Empty(await fixture.Context.CashTransactions.ToListAsync());
        // The answer of the void keeps the folio of the app.
        Assert.Equal("152390", voided.Value.PlatformOrder!.ExternalOrderNumber);
    }

    [Fact]
    public async Task VoidOfADeliveredOrderOfAPlatform_ShouldLeaveItDelivered_AndFullyRefunded()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 350m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        var sale = await SellPlatformOrderAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id));
        Assert.Equal(OrderStatus.Delivered, sale.Status);

        var voided = await VoidHandler(fixture).Handle(
            new VoidOrderCommand { OrderId = sale.Id, Reason = "Lo registré dos veces" }, CancellationToken.None);

        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal(OrderStatus.Delivered, voided.Value.Status);
        Assert.Equal((350m, 350m, 0m), (voided.Value.PaidAmount, voided.Value.RefundedAmount, voided.Value.NetPaidAmount));
        Assert.Equal(OrderPaymentMethod.Platform, Assert.Single(voided.Value.Refunds).Method);
        Assert.Empty(await fixture.Context.CashTransactions.ToListAsync());
    }

    [Fact]
    public async Task VoidOfAnOrderOfAPlatform_ShouldGiveBackItsSupplies_OnlyWhenAsked()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen | TenantCapability.Inventory, pricesIncludeTax: true);
        var taco = await fixture.AddProductAsync(tenant, "Taco", 25m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        var location = await fixture.Context.InventoryLocations.SingleAsync();
        var tortilla = new SupplyItem(tenant.Id, "Tortilla", UnitType.Unit);
        var stock = new InventoryStock(tenant.Id, tortilla.Id, location.Id);
        stock.ApplyMovement(100m, 1m, SalesTestFixture.Now);
        fixture.Context.SupplyItems.Add(tortilla);
        fixture.Context.InventoryStocks.Add(stock);
        fixture.Context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.Id, taco.Id, tortilla.Id, 2m));
        await fixture.Context.SaveChangesAsync();
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context), NullLogger<OrderInventoryService>.Instance);

        var prepared = await SellPlatformOrderAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", taco.Id, quantity: 5), inventory);
        var notPrepared = await SellPlatformOrderAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "13", taco.Id, quantity: 5), inventory);
        Assert.Equal(80m, await OnHandAsync(fixture, tortilla.Id));

        // DiDi cancelled once the food was made: the tortillas were used.
        var lost = await VoidHandler(fixture, inventory).Handle(
            new VoidOrderCommand { OrderId = prepared.Id, Reason = "DiDi lo canceló", RestockItems = false }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(80m, await OnHandAsync(fixture, tortilla.Id));
        // Cancelled before the kitchen started: they go back.
        var returned = await VoidHandler(fixture, inventory).Handle(
            new VoidOrderCommand { OrderId = notPrepared.Id, Reason = "DiDi lo canceló", RestockItems = true }, CancellationToken.None);

        Assert.True(lost.IsSuccess, lost.Error);
        Assert.True(returned.IsSuccess, returned.Error);
        Assert.Equal(OrderStatus.Cancelled, lost.Value.Status);
        Assert.Equal(90m, await OnHandAsync(fixture, tortilla.Id));
    }

    [Fact]
    public async Task RefundMethod_ShouldFollowWhoCharged()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 350m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var drawer = await fixture.OpenCashSessionAsync(tenant);
        var rappi = await SellPlatformOrderAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id));
        var counter = await SellAsync(fixture, pack.Id, 1, (350m, OrderPaymentMethod.Cash));

        // Cash out of the drawer for what Rappi charged, or «Plataforma» for what the drawer took: both refused.
        var cashForRappi = await RefundHandler(fixture).Handle(Refund(rappi.Id, 100m), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var platformForCounter = await RefundHandler(fixture).Handle(Refund(counter.Id, 100m, OrderPaymentMethod.Platform), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        // Part of what Rappi charged, as «Plataforma»: recorded apart from the drawer.
        var platformForRappi = await RefundHandler(fixture).Handle(Refund(rappi.Id, 100m, OrderPaymentMethod.Platform), CancellationToken.None);

        Assert.Equal(PlatformOrderRules.PlatformChargedRefundMessage, cashForRappi.Error);
        Assert.Equal(PlatformOrderRules.PlatformRefundOnlyOfPlatformMessage, platformForCounter.Error);
        Assert.True(platformForRappi.IsSuccess, platformForRappi.Error);
        Assert.Equal((100m, 250m), (platformForRappi.Value.RefundedAmount, platformForRappi.Value.NetPaidAmount));
        Assert.Equal("152390", platformForRappi.Value.PlatformOrder!.ExternalOrderNumber);
        Assert.Single(await fixture.Context.OrderRefunds.ToListAsync());
        // Only the counter sale's cash is in the drawer.
        var movement = Assert.Single(await fixture.Context.CashTransactions.Where(t => t.CashSessionId == drawer.Id).ToListAsync());
        Assert.Equal((CashTransactionType.SalePayment, counter.Id), (movement.TransactionType, movement.OrderId));
    }

    private static async Task<Orders.DTOs.OrderDto> SellPlatformOrderAsync(
        SalesTestFixture fixture, CreateOrderCommand command, IOrderInventoryService? inventory = null)
    {
        var result = await fixture.CreateOrderHandler(inventory).Handle(command, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        fixture.Context.ChangeTracker.Clear();
        return result.Value;
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

    private static RefundOrderCommandHandler RefundHandler(SalesTestFixture fixture, IOrderInventoryService? inventory = null)
        => new(fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser),
            fixture.Clock, inventory ?? NoOpOrderInventoryService.Instance);

    private static VoidOrderCommandHandler VoidHandler(SalesTestFixture fixture, IOrderInventoryService? inventory = null)
        => new(fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser),
            fixture.Clock, inventory ?? NoOpOrderInventoryService.Instance);

    private static async Task<decimal> OnHandAsync(SalesTestFixture fixture, Guid supplyId)
        => (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync(s => s.SupplyItemId == supplyId)).OnHandQuantity;
}
