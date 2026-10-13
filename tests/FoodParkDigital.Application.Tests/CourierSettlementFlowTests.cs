using System.Text.Json;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Couriers;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Orders to the customer's address that go out with Paquetenvia and the cash it collects (Fase 22D): «paga con» taken with
/// the order, the order sent out with the folio and what the courier collects, the customer told it is on its way, what the
/// courier owes once delivered, and the settlement that charges those orders in the drawer of whoever receives the cash,
/// with the courier's trip and fee as movements of that drawer.
/// </summary>
public class CourierSettlementFlowTests
{
    private const string Address = "Av. Reforma 123, Centro";
    private const string Link = "https://paquetenvia.mx/rastreo/PQ1234";

    [Fact]
    public async Task Dispatch_ShouldSendTheOrderOut_WithWhatTheCourierCollects_AndRecordIt_WithoutTheLink()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await KitchenAsync(fixture);
        var sale = await SellAsync(fixture, Delivery(tacos, paysWith: 500m));
        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal((275m, 500m), (sale.Value.Total, sale.Value.Delivery!.PaysWith));

        var dispatched = await DispatchAsync(fixture, sale.Value.Id, " #pq 1234 ", Link, collectsCash: true, paysWith: 500m);
        var again = await DispatchAsync(fixture, sale.Value.Id, "PQ1234", Link, collectsCash: true, paysWith: 500m);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(10);
        var corrected = await DispatchAsync(fixture, sale.Value.Id, "PQ1250", null, collectsCash: true, paysWith: null);

        Assert.True(dispatched.IsSuccess, dispatched.Error);
        Assert.True(again.IsSuccess, again.Error);
        Assert.True(corrected.IsSuccess, corrected.Error);
        var delivery = (await fixture.Context.Orders.SingleAsync()).Delivery!;
        // The correction keeps when it went out and who sent it.
        Assert.Equal((true, "PQ1250", null, 275m, null, SalesTestFixture.Now, "cajero"),
            (delivery.IsDispatched, delivery.CourierFolio, delivery.TrackingUrl, delivery.CashToCollect, delivery.PaysWith,
                delivery.DispatchedAt, delivery.DispatchedBy));
        var logs = (await fixture.Context.AuditLogs.Where(a => a.Action == DispatchOrderCommandHandler.OrderDispatched).ToListAsync())
            .Select(log => JsonDocument.Parse(log.Metadata!).RootElement).ToList();
        Assert.Equal(2, logs.Count);
        var first = Assert.Single(logs, log => !log.GetProperty("Correction").GetBoolean());
        Assert.Equal(("Paquetenvia", "PQ1234", true, 275m, 500m),
            (first.GetProperty("Courier").GetString(), first.GetProperty("Folio").GetString(), first.GetProperty("HasTrackingUrl").GetBoolean(),
                first.GetProperty("CashToCollect").GetDecimal(), first.GetProperty("PaysWith").GetDecimal()));
        var correction = Assert.Single(logs, log => log.GetProperty("Correction").GetBoolean());
        Assert.Equal(("PQ1250", JsonValueKind.Null), (correction.GetProperty("Folio").GetString(), correction.GetProperty("PaysWith").ValueKind));
        Assert.All(logs, log =>
        {
            Assert.DoesNotContain("paquetenvia.mx", log.GetRawText());
            Assert.DoesNotContain("Reforma", log.GetRawText());
        });

        // Read again, the order tells what the courier collects; it owes nothing until it is delivered.
        var detail = await new GetOrderByIdQueryHandler(fixture.Context).Handle(new GetOrderByIdQuery(sale.Value.Id), CancellationToken.None);
        Assert.Equal((275m, SalesTestFixture.Now, "cajero", false),
            (detail.Delivery!.CashToCollect, detail.Delivery.DispatchedAt, detail.Delivery.DispatchedBy, detail.Delivery.AwaitsCourierCash));
        await DeliverAsync(fixture, sale.Value.Id);
        detail = await new GetOrderByIdQueryHandler(fixture.Context).Handle(new GetOrderByIdQuery(sale.Value.Id), CancellationToken.None);
        var listed = Assert.Single((await new GetOrdersByBusinessQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetOrdersByBusinessQuery(), CancellationToken.None)).Items);
        Assert.True(detail.Delivery!.AwaitsCourierCash);
        Assert.Equal((true, 275m), (listed.Delivery!.AwaitsCourierCash, listed.Delivery.CashToCollect));
    }

    [Fact]
    public async Task Dispatch_ShouldBeRefused_ForWhatTheCourierCannotCollect_AnOrderThatStays_OrOneOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        await OpenOwnDrawerAsync(fixture, tenant);
        var unpaid = await SellAsync(fixture, Delivery(tacos));
        var paid = await SellAsync(fixture, Delivery(tacos) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 275m, Method = OrderPaymentMethod.Cash }]
        });
        var delivered = await SellAsync(fixture, Delivery(tacos));
        await DeliverAsync(fixture, delivered.Value.Id);
        var counter = await SellAsync(fixture, new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 1 }] });

        Assert.Equal(HomeDeliveryRules.PaysWithNotEnoughMessage(200m, 275m),
            (await DispatchAsync(fixture, unpaid.Value.Id, "PQ1", null, collectsCash: true, paysWith: 200m)).Error);
        Assert.Equal(HomeDeliveryRules.PaysWithNeedsCashMessage,
            (await DispatchAsync(fixture, unpaid.Value.Id, "PQ1", null, collectsCash: false, paysWith: 500m)).Error);
        Assert.Equal(HomeDeliveryRules.NothingToCollectMessage,
            (await DispatchAsync(fixture, paid.Value.Id, "PQ2", null, collectsCash: true, paysWith: null)).Error);
        Assert.Equal(HomeDeliveryRules.AlreadyDeliveredMessage,
            (await DispatchAsync(fixture, delivered.Value.Id, "PQ3", null, collectsCash: true, paysWith: null)).Error);
        Assert.Equal(HomeDeliveryRules.NotADeliveryMessage,
            (await DispatchAsync(fixture, counter.Value.Id, "PQ4", null, collectsCash: false, paysWith: null)).Error);
        Assert.All(await fixture.Context.Orders.Where(o => o.Delivery != null).ToListAsync(), order => Assert.False(order.Delivery!.IsDispatched));
        Assert.Empty(await fixture.Context.AuditLogs.Where(a => a.Action == DispatchOrderCommandHandler.OrderDispatched).ToListAsync());

        // An order already paid goes out without collecting anything.
        var free = await DispatchAsync(fixture, paid.Value.Id, "PQ2", null, collectsCash: false, paysWith: null);
        Assert.True(free.IsSuccess, free.Error);
        Assert.Equal((true, null), ((await fixture.Context.Orders.SingleAsync(o => o.Id == paid.Value.Id)).Delivery!.IsDispatched,
            (await fixture.Context.Orders.SingleAsync(o => o.Id == paid.Value.Id)).Delivery!.CashToCollect));

        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "vecina");
        await Assert.ThrowsAsync<EntityNotFoundException>(() => DispatchAsync(fixture, unpaid.Value.Id, "PQ1", null, collectsCash: true, paysWith: null));
        fixture.TenantService.SetTenant(tenant.Id);

        var validator = new DispatchOrderCommandValidator();
        Assert.Equal(CourierShipment.FolioRequiredMessage,
            validator.Validate(new DispatchOrderCommand(unpaid.Value.Id, DeliveryCourier.Paquetenvia, " ", null, true, null)).Errors.First().ErrorMessage);
        Assert.Equal(CourierShipment.CourierMessage,
            validator.Validate(new DispatchOrderCommand(unpaid.Value.Id, (DeliveryCourier)9, "PQ1", null, true, null)).Errors.First().ErrorMessage);
        Assert.Equal(HomeDeliveryRules.PaysWithMessage,
            validator.Validate(new DispatchOrderCommand(unpaid.Value.Id, DeliveryCourier.Paquetenvia, "PQ1", null, true, 500.505m)).Errors.First().ErrorMessage);
        Assert.Equal(HomeDeliveryRules.PaysWithMessage,
            validator.Validate(new DispatchOrderCommand(unpaid.Value.Id, DeliveryCourier.Paquetenvia, "PQ1", null, true, 100_000.01m)).Errors.First().ErrorMessage);
        Assert.True(validator.Validate(new DispatchOrderCommand(unpaid.Value.Id, DeliveryCourier.Paquetenvia, "PQ1", Link, true, 500m)).IsValid);
    }

    [Fact]
    public async Task PaysWith_ShouldBeTakenWithTheOrder_InVenderAndTheMenu_AndRefusedBelowTheTotal()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        await OpenOnlineMenuAsync(fixture, tenant);

        var notEnough = await SellAsync(fixture, Delivery(tacos, paysWith: 200m));
        var placed = await PlaceAsync(fixture, PublicOrder(tacos, paysWith: 300m) with { ExpectedTotal = 275m });

        Assert.Equal(HomeDeliveryRules.PaysWithNotEnoughMessage(200m, 275m), notEnough.Error);
        Assert.True(placed.IsSuccess, placed.Error);
        var order = await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((OrderStatus.Pending, 300m), (order.Status, order.Delivery!.PaysWith));
        Assert.Equal(HomeDeliveryRules.PaysWithNotEnoughMessage(250m, 275m),
            (await PlaceAsync(fixture, PublicOrder(tacos, paysWith: 250m))).Error);

        // An order of the menu goes out once the business confirms it.
        Assert.Equal(HomeDeliveryRules.NotConfirmedMessage,
            (await DispatchAsync(fixture, order.Id, "PQ1", null, collectsCash: true, paysWith: 300m)).Error);

        var validator = new OrderDeliveryInputValidator();
        Assert.Equal(HomeDeliveryRules.PaysWithMessage,
            validator.Validate(new OrderDeliveryInput { PostalCode = "72000", AddressLine = Address, PaysWith = 0m }).Errors.Single().ErrorMessage);
        Assert.Equal(HomeDeliveryRules.PaysWithMessage,
            validator.Validate(new OrderDeliveryInput { PostalCode = "72000", AddressLine = Address, PaysWith = 99.999m }).Errors.Single().ErrorMessage);
        Assert.True(validator.Validate(new OrderDeliveryInput { PostalCode = "72000", AddressLine = Address, PaysWith = 100_000m }).IsValid);
    }

    [Fact]
    public async Task GoingOut_ShouldTellTheCustomerItIsOnTheWay_OnceAndWithTheLinkWhenThereIsOne()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, tacos) = await KitchenAsync(fixture);
        var whatsApp = new RecordingWhatsApp();
        var notifications = new OrderCustomerNotifications(fixture.Context, whatsApp, NullLogger<OrderCustomerNotifications>.Instance);
        var context = OutboxContext(tenant.Id);
        var withLink = await SellAsync(fixture, Delivery(tacos));
        var withoutLink = await SellAsync(fixture, Delivery(tacos));
        var deliveredFirst = await SellAsync(fixture, Delivery(tacos));
        mediator.Published.Clear();

        // The trip written down before it leaves, and a correction once on its way, tell the customer nothing new.
        await new RegisterCourierShipmentCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new RegisterCourierShipmentCommand(withLink.Value.Id, DeliveryCourier.Paquetenvia, "PQ1234", Link), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        await DispatchAsync(fixture, withLink.Value.Id, "PQ1234", Link, collectsCash: true, paysWith: null);
        await DispatchAsync(fixture, withLink.Value.Id, "PQ1250", Link, collectsCash: true, paysWith: null);
        await DispatchAsync(fixture, withoutLink.Value.Id, "PQ1300", null, collectsCash: true, paysWith: null);
        await DispatchAsync(fixture, deliveredFirst.Value.Id, "PQ1400", null, collectsCash: true, paysWith: null);
        await DeliverAsync(fixture, deliveredFirst.Value.Id);

        var events = mediator.Published.OfType<OrderDeliveryUpdatedEvent>().ToList();
        Assert.Equal(5, events.Count);
        Assert.Equal(3, events.Count(e => e.WentOut));
        foreach (var updated in events)
            await notifications.HandleAsync(updated, context, CancellationToken.None);

        Assert.Equal(
            [
                ("order_status_update", withLink.Value.OrderNumber, $"En camino, síguelo aquí: {Link}"),
                ("order_status_update", withoutLink.Value.OrderNumber, OrderStatusLabels.OnTheWay)
            ],
            whatsApp.Sent.Select(sent => (sent.Template, sent.Parameters[0], sent.Parameters[1])).ToList());
        Assert.Equal("En camino", OrderStatusLabels.OnTheWayNotice(null));
    }

    [Fact]
    public async Task GoingOut_ShouldRefreshTheOrdersOnScreen_InRealTime()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, tacos) = await KitchenAsync(fixture);
        var sale = await SellAsync(fixture, Delivery(tacos));
        await DispatchAsync(fixture, sale.Value.Id, "PQ1234", null, collectsCash: true, paysWith: null);
        var realtime = new RecordingOrderNotifications();
        var handler = new OrderRealtimeNotifications(fixture.Context, realtime, NullLogger<OrderRealtimeNotifications>.Instance);

        await handler.Handle(Assert.Single(mediator.Published.OfType<OrderDeliveryUpdatedEvent>()), CancellationToken.None);

        Assert.Equal([(tenant.Id, sale.Value.Id)], realtime.Updated);
    }

    [Fact]
    public async Task Receivables_ShouldTellWhatTheCourierOwes_ItsBalance_AndWhatItCarries_OfThisBusinessOnly()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        await OpenOwnDrawerAsync(fixture, tenant);
        var owed = await OwedOrderAsync(fixture, tacos, "PQ1");
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(30);
        var partlyPaid = await OwedOrderAsync(fixture, tacos, "PQ2", quantity: 4);
        var onTheWay = await SellAsync(fixture, Delivery(tacos));
        await DispatchAsync(fixture, onTheWay.Value.Id, "PQ3", null, collectsCash: true, paysWith: 300m);
        var paidAhead = await SellAsync(fixture, Delivery(tacos) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 275m, Method = OrderPaymentMethod.Card }]
        });
        await DispatchAsync(fixture, paidAhead.Value.Id, "PQ4", null, collectsCash: false, paysWith: null);
        await DeliverAsync(fixture, paidAhead.Value.Id);
        var neverWentOut = await SellAsync(fixture, Delivery(tacos));
        await DeliverAsync(fixture, neverWentOut.Value.Id);
        // The customer paid part of the second one by transfer: the courier owes the rest.
        var transfer = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = partlyPaid.Id, Amount = 80m, Method = OrderPaymentMethod.Transfer }, CancellationToken.None);
        Assert.True(transfer.IsSuccess, transfer.Error);
        fixture.Context.ChangeTracker.Clear();
        // What a courier owes another business is not this one's.
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "vecina");
        var neighbourTacos = await fixture.AddProductAsync(await fixture.Context.Tenants.SingleAsync(t => t.Slug == "vecina"), "Tacos", 200m);
        await AddZoneAsync(fixture, "Sur", 0m, 0m, null, "72000");
        await OwedOrderAsync(fixture, neighbourTacos, "PQ9");
        fixture.TenantService.SetTenant(tenant.Id);

        var receivable = Assert.Single(await ReceivablesAsync(fixture));
        var pending = await PendingAsync(fixture);

        Assert.Equal((DeliveryCourier.Paquetenvia, 2, 275m + 400m, SalesTestFixture.Now, 1, 275m, null),
            (receivable.Courier, receivable.OwedOrders, receivable.OwedAmount, receivable.OldestDispatchedAt,
                receivable.OnTheWayOrders, receivable.OnTheWayAmount, receivable.LastSettledAt));
        Assert.Equal([(owed.Id, "PQ1", 275m, 275m), (partlyPaid.Id, "PQ2", 480m, 400m)],
            pending.Owed.Select(o => (o.OrderId, o.CourierFolio, o.CashToCollect, o.Balance)).ToList());
        var carried = Assert.Single(pending.OnTheWay);
        Assert.Equal((onTheWay.Value.Id, 275m, 300m, OrderStatus.Confirmed), (carried.OrderId, carried.CashToCollect, carried.PaysWith, carried.Status));
        Assert.False(pending.Truncated);
        Assert.False(new GetPendingCourierOrdersQueryValidator().Validate(new GetPendingCourierOrdersQuery((DeliveryCourier)9)).IsValid);
    }

    [Fact]
    public async Task Settlement_ShouldChargeTheOrders_InTheOwnDrawer_AndEnterTheTripAndTheFee_AsItsMovements()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        var colleague = await fixture.OpenCashSessionAsync(tenant, openedByUserId: Guid.NewGuid());
        var first = await OwedOrderAsync(fixture, tacos, "PQ1");
        var second = await OwedOrderAsync(fixture, tacos, "PQ2");
        var own = await OpenOwnDrawerAsync(fixture, tenant);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(3);

        var settled = await SettleAsync(fixture, Settlement(
            500m,
            [first.Id, second.Id],
            [
                new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.TripFee, null, -45m),
                new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.CollectionFee, " 2 % ", -10m),
                new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.Other, "Redondeo", 5m)
            ]) with { Reference = " Corte del martes " });

        Assert.True(settled.IsSuccess, settled.Error);
        var detail = settled.Value;
        Assert.Equal((DeliveryCourier.Paquetenvia, OrderPaymentMethod.Cash, 500m, 550m, -50m, 2, "Corte del martes", "cajero", fixture.Clock.UtcNow),
            (detail.Settlement.Courier, detail.Settlement.Method, detail.Settlement.HandedAmount, detail.Settlement.OrdersTotal,
                detail.Settlement.AdjustmentsTotal, detail.Settlement.OrderCount, detail.Settlement.Reference, detail.Settlement.SettledBy,
                detail.Settlement.SettledAt));
        Assert.Equal([("PQ1", 275m), ("PQ2", 275m)], detail.Orders.Select(o => (o.CourierFolio, o.Amount)).ToList());
        Assert.Equal(
            [
                (1, CourierSettlementAdjustmentKind.TripFee, (string?)null, -45m),
                (2, CourierSettlementAdjustmentKind.CollectionFee, "2 %", -10m),
                (3, CourierSettlementAdjustmentKind.Other, "Redondeo", 5m)
            ],
            detail.Adjustments.Select(a => (a.Position, a.Kind, a.Description, a.Amount)).ToList());

        // Each order is paid in full, in cash, telling the courier's folio.
        var orders = await fixture.Context.Orders.Include(o => o.Payments).Where(o => o.Id == first.Id || o.Id == second.Id).ToListAsync();
        Assert.All(orders, order =>
        {
            Assert.Equal(0m, order.Balance);
            Assert.False(order.AwaitsCourierCash);
            var payment = Assert.Single(order.Payments);
            Assert.Equal((275m, OrderPaymentMethod.Cash, $"Paquetenvia {order.Delivery!.CourierFolio}", "Liquidación de Paquetenvia"),
                (payment.Amount, payment.Method, payment.Reference, payment.Notes));
        });
        // All of it enters the drawer of whoever received it: the payments, the trip and the fee out, the rounding in.
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == own.Id);
        Assert.Equal(
            [
                (CashTransactionType.SalePayment, 275m, "Paquetenvia PQ1", "Liquidación de Paquetenvia"),
                (CashTransactionType.SalePayment, 275m, "Paquetenvia PQ2", "Liquidación de Paquetenvia"),
                (CashTransactionType.Expense, -45m, "Paquetenvia", "Viaje de Paquetenvia"),
                (CashTransactionType.Expense, -10m, "Paquetenvia", "Comisión por cobrar de Paquetenvia: 2 %"),
                (CashTransactionType.Deposit, 5m, "Paquetenvia", "Paquetenvia: Redondeo")
            ],
            drawer.Transactions.OrderBy(t => t.TransactionType).ThenBy(t => t.Reference).ThenBy(t => t.Amount)
                .Select(t => (t.TransactionType, t.Amount, t.Reference, t.Notes)).ToList());
        Assert.All(drawer.Transactions, t => Assert.Equal(OrderPaymentMethod.Cash, t.PaymentMethod));
        Assert.Equal(500m + 500m, drawer.ExpectedBalance);
        Assert.Empty(await fixture.Context.CashTransactions.Where(t => t.CashSessionId == colleague.Id).ToListAsync());
        var stored = await fixture.Context.CourierSettlements.SingleAsync();
        Assert.Equal(own.Id, stored.CashSessionId);

        var log = JsonDocument.Parse((await fixture.Context.AuditLogs
            .SingleAsync(a => a.Action == CreateCourierSettlementCommandHandler.SettlementRegistered)).Metadata!).RootElement;
        Assert.Equal((500m, 550m, -50m), (log.GetProperty("HandedAmount").GetDecimal(), log.GetProperty("OrdersTotal").GetDecimal(),
            log.GetProperty("AdjustmentsTotal").GetDecimal()));
        Assert.Equal(2, log.GetProperty("Orders").GetArrayLength());
        Assert.DoesNotContain("Reforma", log.GetRawText());

        // Nothing is owed any more; the same orders are not settled twice.
        var receivable = Assert.Single(await ReceivablesAsync(fixture));
        Assert.Equal((0, 0m, null, fixture.Clock.UtcNow), (receivable.OwedOrders, receivable.OwedAmount, receivable.OldestDispatchedAt, receivable.LastSettledAt));
        Assert.Equal(CourierSettlementErrors.NotOwed, (await SettleAsync(fixture, Settlement(550m, [first.Id, second.Id]))).Error);
        Assert.Single(await fixture.Context.CourierSettlements.ToListAsync());
    }

    [Fact]
    public async Task Settlement_ShouldBeRefused_WithoutOwnDrawer_ForOrdersNotOwed_OrWhenItDoesNotAddUp_SavingNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        var colleague = await fixture.OpenCashSessionAsync(tenant, openedByUserId: Guid.NewGuid());
        var owed = await OwedOrderAsync(fixture, tacos, "PQ1");
        var onTheWay = await SellAsync(fixture, Delivery(tacos));
        await DispatchAsync(fixture, onTheWay.Value.Id, "PQ2", null, collectsCash: true, paysWith: null);
        var chargedAtTheCounter = await OwedOrderAsync(fixture, tacos, "PQ3");
        var notADelivery = await SellAsync(fixture, new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 2 }] });

        // Whoever has no drawer of their own cannot receive the cash, even with a colleague's open.
        Assert.Equal(CourierSettlementErrors.NoOwnDrawer, (await SettleAsync(fixture, Settlement(275m, [owed.Id]))).Error);

        var own = await OpenOwnDrawerAsync(fixture, tenant);
        var charge = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = chargedAtTheCounter.Id, Amount = 275m, Method = OrderPaymentMethod.Cash }, CancellationToken.None);
        Assert.True(charge.IsSuccess, charge.Error);
        fixture.Context.ChangeTracker.Clear();
        var paymentsBefore = await fixture.Context.CashTransactions.CountAsync();

        Assert.Equal(CourierSettlementErrors.NotOwed, (await SettleAsync(fixture, Settlement(550m, [owed.Id, onTheWay.Value.Id]))).Error);
        Assert.Equal(CourierSettlementErrors.NotOwed, (await SettleAsync(fixture, Settlement(550m, [owed.Id, chargedAtTheCounter.Id]))).Error);
        Assert.Equal(CourierSettlementErrors.NotOwed, (await SettleAsync(fixture, Settlement(515m, [owed.Id, notADelivery.Value.Id]))).Error);
        Assert.Equal(CourierSettlementErrors.NotOwed, (await SettleAsync(fixture, Settlement(275m, [owed.Id, Guid.NewGuid()]))).Error);
        Assert.Equal(CourierSettlement.DoesNotBalanceMessage(240m, 230m), (await SettleAsync(fixture, Settlement(
            240m, [owed.Id], [new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.TripFee, null, -45m)]))).Error);
        Assert.Equal(CourierSettlement.DeductionMessage, (await SettleAsync(fixture, Settlement(
            320m, [owed.Id], [new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.TripFee, null, 45m)]))).Error);
        Assert.Equal(CourierSettlement.AdjustmentDescriptionRequiredMessage, (await SettleAsync(fixture, Settlement(
            280m, [owed.Id], [new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.Other, " ", 5m)]))).Error);

        Assert.Empty(await fixture.Context.CourierSettlements.ToListAsync());
        Assert.Equal(paymentsBefore, await fixture.Context.CashTransactions.CountAsync());
        Assert.Equal(275m, (await fixture.Context.Orders.SingleAsync(o => o.Id == owed.Id)).Balance);
        Assert.Empty(await fixture.Context.CashTransactions.Where(t => t.CashSessionId == colleague.Id).ToListAsync());
        Assert.NotEqual(Guid.Empty, own.Id);
    }

    [Fact]
    public async Task Settlement_ByTransfer_ShouldEnterTheDrawer_ApartFromTheCash()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        var own = await OpenOwnDrawerAsync(fixture, tenant);
        var owed = await OwedOrderAsync(fixture, tacos, "PQ1");

        var settled = await SettleAsync(fixture, Settlement(
            230m, [owed.Id], [new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.TripFee, null, -45m)]) with
        {
            Method = OrderPaymentMethod.Transfer
        });

        Assert.True(settled.IsSuccess, settled.Error);
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == own.Id);
        Assert.All(drawer.Transactions, t => Assert.Equal(OrderPaymentMethod.Transfer, t.PaymentMethod));
        // A transfer is not cash in the drawer: the expected cash stays the opening.
        Assert.Equal((500m, 230m), (drawer.ExpectedBalance, drawer.NonCashInflows));
        Assert.Equal(OrderPaymentMethod.Transfer,
            (await fixture.Context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == owed.Id)).Payments.Single().Method);
    }

    [Fact]
    public async Task Settlements_ShouldBeListedNewestFirst_ByCourier_AndReadInDetail_OnlyByTheirBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        await OpenOwnDrawerAsync(fixture, tenant);
        var first = await OwedOrderAsync(fixture, tacos, "PQ1");
        var second = await OwedOrderAsync(fixture, tacos, "PQ2");
        var older = await SettleAsync(fixture, Settlement(275m, [first.Id]));
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        var newer = await SettleAsync(fixture, Settlement(275m, [second.Id]) with { Notes = "Viernes" });

        var page = await new GetCourierSettlementsQueryHandler(fixture.Context)
            .Handle(new GetCourierSettlementsQuery { Courier = DeliveryCourier.Paquetenvia }, CancellationToken.None);
        var detail = await new GetCourierSettlementQueryHandler(fixture.Context)
            .Handle(new GetCourierSettlementQuery(older.Value.Settlement.Id), CancellationToken.None);

        Assert.Equal([newer.Value.Settlement.Id, older.Value.Settlement.Id], page.Items.Select(s => s.Id).ToList());
        Assert.Equal((1, "Viernes"), (page.Items[0].OrderCount, page.Items[0].Notes));
        Assert.Equal((first.Id, "Ana", 275m), (detail.Orders.Single().OrderId, detail.Orders.Single().CustomerName, detail.Orders.Single().Amount));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => new GetCourierSettlementQueryHandler(fixture.Context)
            .Handle(new GetCourierSettlementQuery(Guid.NewGuid()), CancellationToken.None));

        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "vecina");
        Assert.Empty((await new GetCourierSettlementsQueryHandler(fixture.Context)
            .Handle(new GetCourierSettlementsQuery(), CancellationToken.None)).Items);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => new GetCourierSettlementQueryHandler(fixture.Context)
            .Handle(new GetCourierSettlementQuery(older.Value.Settlement.Id), CancellationToken.None));
        Assert.Equal(0, Assert.Single(await ReceivablesAsync(fixture)).OwedOrders);

        var validator = new GetCourierSettlementsQueryValidator();
        Assert.False(validator.Validate(new GetCourierSettlementsQuery { PageSize = 101 }).IsValid);
        Assert.False(validator.Validate(new GetCourierSettlementsQuery { Courier = (DeliveryCourier)9 }).IsValid);
        Assert.True(validator.Validate(new GetCourierSettlementsQuery()).IsValid);
    }

    [Fact]
    public void SettlementValidator_ShouldRefuse_NoOrders_RepeatedOnes_TooMany_ACardOrAMissingAdjustment()
    {
        var validator = new CreateCourierSettlementCommandValidator();
        var order = Guid.NewGuid();

        Assert.Equal(CourierSettlement.NoOrdersMessage, FirstError(validator.Validate(Settlement(0m, []))));
        Assert.Equal(CourierSettlement.RepeatedOrderMessage, FirstError(validator.Validate(Settlement(10m, [order, order]))));
        Assert.Equal(CourierSettlement.RepeatedOrderMessage, FirstError(validator.Validate(Settlement(10m, [Guid.Empty]))));
        Assert.Equal(CourierSettlement.TooManyOrdersMessage,
            FirstError(validator.Validate(Settlement(10m, Enumerable.Range(0, CourierSettlement.MaxOrders + 1).Select(_ => Guid.NewGuid()).ToList()))));
        Assert.Equal(CourierSettlement.MethodMessage, FirstError(validator.Validate(Settlement(10m, [order]) with { Method = OrderPaymentMethod.Card })));
        Assert.Equal(CourierSettlement.CourierMessage, FirstError(validator.Validate(Settlement(10m, [order]) with { Courier = (DeliveryCourier)9 })));
        Assert.Equal(CourierSettlement.AdjustmentKindMessage, FirstError(validator.Validate(Settlement(10m, [order], [null!]))));
        Assert.Equal(CourierSettlement.TooManyAdjustmentsMessage, FirstError(validator.Validate(Settlement(10m, [order],
            Enumerable.Range(0, CourierSettlement.MaxAdjustments + 1)
                .Select(_ => new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.TripFee, null, -1m)).ToList()))));
        Assert.True(validator.Validate(Settlement(10m, [order])).IsValid);
    }

    /// <summary>A kitchen selling with IVA included: $120 tacos, and its zone Centro reaching 72000 for $35, from $150, free from $400.</summary>
    private static async Task<(Tenant Tenant, Product Tacos)> KitchenAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: "demo");
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var zone = await AddZoneAsync(fixture, "Centro", 35m, 150m, 400m, "72000");
        Assert.True(zone.IsSuccess, zone.Error);
        return (tenant, tacos);
    }

    private static async Task<Result<DeliveryZoneDto>> AddZoneAsync(
        SalesTestFixture fixture, string name, decimal fee, decimal minimum, decimal? freeFrom, params string[] postalCodes)
    {
        var result = await new CreateDeliveryZoneCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new CreateDeliveryZoneCommand(name, fee, minimum, freeFrom, postalCodes), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    /// <summary>The cashier's own drawer, with $500 to start.</summary>
    private static async Task<CashSession> OpenOwnDrawerAsync(SalesTestFixture fixture, Tenant tenant)
    {
        var drawer = await fixture.OpenCashSessionAsync(tenant, openedByUserId: Guid.Parse(fixture.CurrentUser.UserId!));
        fixture.Context.ChangeTracker.Clear();
        return drawer;
    }

    private static CreateOrderCommand Delivery(Product product, decimal quantity = 2, decimal? paysWith = null) => new()
    {
        CustomerName = "Ana",
        CustomerPhone = "55 1234 5678",
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = quantity }],
        Delivery = new OrderDeliveryInput { PostalCode = "72000", AddressLine = Address, PaysWith = paysWith }
    };

    private static PlacePublicOrderCommand PublicOrder(Product product, decimal? paysWith) => new()
    {
        Slug = "demo",
        CustomerName = "Ana",
        CustomerPhone = "55 1234 5678",
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 2 }],
        Delivery = new OrderDeliveryInput { PostalCode = "72000", AddressLine = Address, PaysWith = paysWith }
    };

    private static CreateCourierSettlementCommand Settlement(
        decimal handedAmount, IReadOnlyList<Guid> orderIds, IReadOnlyList<CourierSettlementAdjustmentInput>? adjustments = null)
        => new(DeliveryCourier.Paquetenvia, OrderPaymentMethod.Cash, handedAmount, orderIds, adjustments ?? [], null, null);

    /// <summary>Sold in Vender, sent out with the courier collecting it all and delivered: what the courier owes.</summary>
    private static async Task<OrderDto> OwedOrderAsync(SalesTestFixture fixture, Product product, string folio, decimal quantity = 2)
    {
        var sale = await SellAsync(fixture, Delivery(product, quantity));
        Assert.True(sale.IsSuccess, sale.Error);
        var dispatched = await DispatchAsync(fixture, sale.Value.Id, folio, null, collectsCash: true, paysWith: null);
        Assert.True(dispatched.IsSuccess, dispatched.Error);
        await DeliverAsync(fixture, sale.Value.Id);
        return sale.Value;
    }

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result<OrderDto>> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var result = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result> DispatchAsync(
        SalesTestFixture fixture, Guid orderId, string folio, string? trackingUrl, bool collectsCash, decimal? paysWith)
    {
        try
        {
            return await new DispatchOrderCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
                .Handle(new DispatchOrderCommand(orderId, DeliveryCourier.Paquetenvia, folio, trackingUrl, collectsCash, paysWith), CancellationToken.None);
        }
        finally
        {
            fixture.Context.ChangeTracker.Clear();
        }
    }

    private static async Task DeliverAsync(SalesTestFixture fixture, Guid orderId)
    {
        var result = await new UpdateOrderStatusCommandHandler(fixture.Context, fixture.CurrentUser, NoOpOrderInventoryService.Instance)
            .Handle(new UpdateOrderStatusCommand { OrderId = orderId, NewStatus = OrderStatus.Delivered }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<Result<CourierSettlementDetailDto>> SettleAsync(SalesTestFixture fixture, CreateCourierSettlementCommand command)
    {
        try
        {
            return await new CreateCourierSettlementCommandHandler(
                    fixture.Context, fixture.TenantService, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser), fixture.Clock)
                .Handle(command, CancellationToken.None);
        }
        finally
        {
            fixture.Context.ChangeTracker.Clear();
        }
    }

    private static Task<IReadOnlyList<CourierReceivableDto>> ReceivablesAsync(SalesTestFixture fixture)
        => new GetCourierReceivablesQueryHandler(fixture.Context).Handle(new GetCourierReceivablesQuery(), CancellationToken.None);

    private static Task<PendingCourierOrdersDto> PendingAsync(SalesTestFixture fixture)
        => new GetPendingCourierOrdersQueryHandler(fixture.Context)
            .Handle(new GetPendingCourierOrdersQuery(DeliveryCourier.Paquetenvia), CancellationToken.None);

    private static async Task OpenOnlineMenuAsync(SalesTestFixture fixture, Tenant tenant)
    {
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        await fixture.Context.SaveChangesAsync();
        await fixture.OpenCashSessionAsync(tenant, (await fixture.DefaultBranchAsync()).Id);
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<Result<PublicOrderReceiptDto>> PlaceAsync(SalesTestFixture fixture, PlacePublicOrderCommand command)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        var result = await new PlacePublicOrderCommandHandler(
                fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance,
                new FolioGenerator(fixture.Context))
            .Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static string? FirstError(FluentValidation.Results.ValidationResult result) => result.Errors.FirstOrDefault()?.ErrorMessage;

    private static OutboxEventContext OutboxContext(Guid tenantId)
        => new(Guid.NewGuid(), tenantId, "user-1", "cajero", SalesTestFixture.Now, 1);

    private sealed class RecordingMediator : IMediator
    {
        public List<DomainEvent> Published { get; } = [];

        public Task Publish(object notification, CancellationToken ct = default)
        {
            if (notification is DomainEvent domainEvent)
                Published.Add(domainEvent);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
            => Publish((object)notification!, ct);

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class RecordingWhatsApp : IWhatsAppService
    {
        public List<(string Template, string[] Parameters)> Sent { get; } = [];

        public Task<MessageSendResult> SendTemplateMessageAsync(
            Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
        {
            Sent.Add((templateName, parameters));
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Sent.Count}"));
        }

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Order notices are templates.");
    }

    private sealed class RecordingOrderNotifications : IOrderNotificationService
    {
        public List<(Guid TenantId, Guid OrderId)> Updated { get; } = [];

        public Task NotifyOrderUpdated(Guid tenantId, Guid orderId)
        {
            Updated.Add((tenantId, orderId));
            return Task.CompletedTask;
        }

        public Task NotifyOrderCreated(Guid tenantId, Guid orderId, string orderNumber, BusinessType businessType, decimal total,
            OrderStatus status, OrderChannel channel, string? externalOrderNumber) => throw new NotSupportedException();
        public Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus) => throw new NotSupportedException();
        public Task NotifyOrderCancelled(Guid tenantId, Guid orderId, OrderStatus previousStatus, string? reason) => throw new NotSupportedException();
        public Task NotifyKitchenTicketsChanged(Guid tenantId, Guid orderId, int round) => throw new NotSupportedException();
        public Task NotifyServiceBoardPieceUpdated(Guid tenantId, Guid orderId, Guid pieceId, ServiceOrderPieceStatus status, Guid? assignedToUserId)
            => throw new NotSupportedException();
    }
}
