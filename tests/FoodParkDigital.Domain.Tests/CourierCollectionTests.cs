using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// An order that goes out with the courier and the cash the courier collects (Fase 22D): the bill the customer pays with,
/// the order sent out with its trip and what the courier collects, the order the courier owes once delivered, and the
/// settlement of what it hands over, which must add up to the cent.
/// </summary>
public class CourierCollectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 19, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.From("72000", "Av. Juárez 123, Col. Centro", null);
    private static readonly CourierShipment Trip = CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ48213", "https://paquetenvia.mx/r/PQ48213");

    [Fact]
    public void PaysWith_ShouldBeKept_WhenItCoversTheTotalWithItsDelivery()
    {
        var order = OrderWith(240m);
        order.ArrangeDelivery(Address, Zone(fee: 35m), VatRate.General, paysWith: 500m);

        Assert.Equal((275m, 500m), (order.Total, order.Delivery!.PaysWith));
        Assert.Null(OrderArranged(240m).Delivery!.PaysWith);
    }

    [Theory]
    [InlineData(274.99, "Con $274.99 no alcanza: hay que cobrar $275.00.")]
    [InlineData(0, HomeDeliveryRules.PaysWithMessage)]
    [InlineData(500.001, HomeDeliveryRules.PaysWithMessage)]
    [InlineData(100_000.01, HomeDeliveryRules.PaysWithMessage)]
    public void PaysWith_ShouldBeRefused_BelowTheTotalOrNotInCents_LeavingTheOrderAsItWas(decimal paysWith, string message)
    {
        var order = OrderWith(240m);

        Assert.Equal(message, Assert.Throws<DomainException>(() =>
            order.ArrangeDelivery(Address, Zone(fee: 35m), VatRate.General, paysWith)).Message);
        Assert.Null(order.Delivery);
        Assert.Equal(240m, order.Total);
        // Refused, the order can still go to the address.
        order.ArrangeDelivery(Address, Zone(fee: 35m), VatRate.General);
        Assert.Equal(275m, order.Total);
    }

    [Fact]
    public void Dispatch_ShouldSendTheOrderOut_WithItsTrip_AndTheBalanceTheCourierCollects()
    {
        var order = Confirmed(OrderArranged(240m, paysWith: 500m));

        Assert.True(order.Dispatch(Trip, collectsCash: true, paysWith: 500m, Now, "caja"));

        var delivery = order.Delivery!;
        Assert.Equal((Now, "caja", 275m, 500m), (delivery.DispatchedAt, delivery.DispatchedBy, delivery.CashToCollect, delivery.PaysWith));
        Assert.Equal((DeliveryCourier.Paquetenvia, "PQ48213", "https://paquetenvia.mx/r/PQ48213"),
            (delivery.Courier, delivery.CourierFolio, delivery.TrackingUrl));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(Now, order.UpdatedAt);
        var updated = Assert.Single(order.DomainEvents.OfType<OrderDeliveryUpdatedEvent>());
        Assert.Equal((order.Id, Tenant, true), (updated.OrderId, updated.TenantId, updated.WentOut));
    }

    [Fact]
    public void Dispatch_ShouldBeCorrectedOnItsWay_KeepingWhenItWentOut_AndNoOneHearsOfItAgain()
    {
        var order = Confirmed(OrderArranged(240m));
        order.Dispatch(Trip, collectsCash: true, paysWith: null, Now, "caja");
        order.ClearDomainEvents();

        // The same again changes nothing.
        Assert.False(order.Dispatch(Trip, collectsCash: true, paysWith: null, Now.AddMinutes(1), "otro"));
        Assert.Empty(order.DomainEvents);

        Assert.True(order.Dispatch(Trip, collectsCash: true, paysWith: 300m, Now.AddMinutes(5), "gerente"));
        Assert.Equal((Now, "caja", 300m), (order.Delivery!.DispatchedAt, order.Delivery.DispatchedBy, order.Delivery.PaysWith));
        Assert.False(Assert.Single(order.DomainEvents.OfType<OrderDeliveryUpdatedEvent>()).WentOut);

        // Paid some other way, the courier collects nothing and the bill goes away.
        order.ClearDomainEvents();
        Assert.True(order.Dispatch(Trip, collectsCash: false, paysWith: null, Now.AddMinutes(6), "gerente"));
        Assert.Equal(((decimal?)null, (decimal?)null), (order.Delivery.CashToCollect, order.Delivery.PaysWith));
    }

    [Fact]
    public void Dispatch_ShouldCollectOnlyWhatIsStillOwed_AndNeedAnOrderToCollect()
    {
        var partly = Confirmed(OrderArranged(240m));
        partly.RegisterPayment(100m, OrderPaymentMethod.Transfer, Now, null, null, null, null);
        partly.Dispatch(Trip, collectsCash: true, paysWith: 200m, Now, "caja");
        Assert.Equal(175m, partly.Delivery!.CashToCollect);

        var paid = Confirmed(OrderArranged(240m));
        paid.RegisterPayment(paid.Total, OrderPaymentMethod.Transfer, Now, null, null, null, null);
        Assert.Equal(HomeDeliveryRules.NothingToCollectMessage,
            Assert.Throws<DomainException>(() => paid.Dispatch(Trip, collectsCash: true, paysWith: null, Now, "caja")).Message);
        Assert.True(paid.Dispatch(Trip, collectsCash: false, paysWith: null, Now, "caja"));
        Assert.Null(paid.Delivery!.CashToCollect);
    }

    [Theory]
    [InlineData(true, 274.99, "Con $274.99 no alcanza: hay que cobrar $275.00.")]
    [InlineData(true, 500.5051, HomeDeliveryRules.PaysWithMessage)]
    [InlineData(false, 500, HomeDeliveryRules.PaysWithNeedsCashMessage)]
    public void Dispatch_ShouldRefuseABillThatDoesNotCover_OrWithoutCash(bool collectsCash, decimal paysWith, string message)
    {
        var order = Confirmed(OrderArranged(240m));

        Assert.Equal(message, Assert.Throws<DomainException>(() => order.Dispatch(Trip, collectsCash, paysWith, Now, "caja")).Message);
        Assert.False(order.Delivery!.IsDispatched);
        Assert.Null(order.Delivery.CourierFolio);
    }

    [Fact]
    public void Dispatch_ShouldNeedAConfirmedOrderToTheAddress_ThatIsNotDeliveredNorCancelled()
    {
        var stateMachine = new FoodOrderStateMachine();
        Assert.Equal(HomeDeliveryRules.NotADeliveryMessage, Refused(Confirmed(OrderWith(240m))));
        Assert.Equal(HomeDeliveryRules.NotConfirmedMessage, Refused(OrderArranged(240m)));

        var delivered = Confirmed(OrderArranged(240m));
        delivered.TransitionTo(OrderStatus.Delivered, stateMachine, "caja");
        Assert.Equal(HomeDeliveryRules.AlreadyDeliveredMessage, Refused(delivered));

        var cancelled = Confirmed(OrderArranged(240m));
        cancelled.Cancel(stateMachine, "caja", "El cliente canceló");
        Assert.Equal(HomeDeliveryRules.CancelledMessage, Refused(cancelled));

        // Ready in the kitchen it goes out too.
        var ready = Confirmed(OrderArranged(240m));
        ready.TransitionTo(OrderStatus.Ready, stateMachine, "cocina");
        Assert.True(ready.Dispatch(Trip, collectsCash: true, paysWith: null, Now, "caja"));

        static string Refused(Order order)
            => Assert.Throws<DomainException>(() => order.Dispatch(Trip, collectsCash: true, paysWith: null, Now, "caja")).Message;
    }

    [Fact]
    public void ShipmentWrittenDown_ShouldTellTheScreens_ButNotTheCustomer()
    {
        var order = OrderArranged(240m);

        order.RegisterCourierShipment(Trip, Now, "caja");

        Assert.False(Assert.Single(order.DomainEvents.OfType<OrderDeliveryUpdatedEvent>()).WentOut);
    }

    [Fact]
    public void CourierOwesTheOrder_OnlyOnceDelivered_WhileTheBusinessIsNotPaid()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = Confirmed(OrderArranged(240m));
        Assert.False(order.AwaitsCourierCash);

        order.Dispatch(Trip, collectsCash: true, paysWith: null, Now, "caja");
        Assert.False(order.AwaitsCourierCash);

        order.TransitionTo(OrderStatus.Delivered, stateMachine, "caja");
        Assert.True(order.AwaitsCourierCash);

        order.RegisterPayment(order.Balance, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.False(order.AwaitsCourierCash);

        // An order that went out paid is not owed.
        var paid = Confirmed(OrderArranged(240m));
        paid.RegisterPayment(paid.Total, OrderPaymentMethod.Card, Now, null, null, null, null);
        paid.Dispatch(Trip, collectsCash: false, paysWith: null, Now, "caja");
        paid.TransitionTo(OrderStatus.Delivered, stateMachine, "caja");
        Assert.False(paid.AwaitsCourierCash);
    }

    [Fact]
    public void Settlement_ShouldPayEachOrder_AndExplainTheRestWithItsAdjustments()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var drawer = Guid.NewGuid();

        var settlement = new CourierSettlement(
            Tenant, DeliveryCourier.Paquetenvia, drawer, OrderPaymentMethod.Cash, handedAmount: 420m,
            [new(first, 275m), new(second, 225m)],
            [
                new(CourierSettlementAdjustmentKind.TripFee, "  ", -70m),
                new(CourierSettlementAdjustmentKind.Other, " Redondeo a favor ", -10m),
            ],
            reference: " PQ-SEM-42 ", notes: "   ", Now, "caja");

        Assert.Equal((500m, -80m, 420m), (settlement.OrdersTotal, settlement.AdjustmentsTotal, settlement.HandedAmount));
        Assert.Equal((Tenant, DeliveryCourier.Paquetenvia, drawer, OrderPaymentMethod.Cash, Now, "caja"),
            (settlement.TenantId, settlement.Courier, settlement.CashSessionId, settlement.Method, settlement.SettledAt, settlement.SettledBy));
        Assert.Equal(("PQ-SEM-42", (string?)null), (settlement.Reference, settlement.Notes));
        Assert.Equal([(first, 275m), (second, 225m)], settlement.Orders.Select(o => (o.OrderId, o.Amount)));
        Assert.All(settlement.Orders, o => Assert.Equal((Tenant, settlement.Id), (o.TenantId, o.SettlementId)));
        Assert.Equal(
            [(1, CourierSettlementAdjustmentKind.TripFee, (string?)null, -70m), (2, CourierSettlementAdjustmentKind.Other, "Redondeo a favor", -10m)],
            settlement.Adjustments.Select(a => (a.Position, a.Kind, a.Description, a.Amount)));
    }

    [Fact]
    public void Settlement_ShouldAddUpToTheCent_OrSayTheDifference()
    {
        var message = Assert.Throws<DomainException>(() => Settle(handedAmount: 480m, orders: [275m, 225m])).Message;

        Assert.Equal("Entregó $480.00 y los pedidos con sus ajustes dan $500.00: la diferencia debe ser $0.00. Registra lo que falta como un ajuste.", message);
        // A shortage explains it; a transfer of everything needs no adjustment.
        Assert.Equal(-20m, Settle(480m, [275m, 225m], [new(CourierSettlementAdjustmentKind.Shortage, null, -20m)]).AdjustmentsTotal);
        Assert.Equal(OrderPaymentMethod.Transfer, Settle(500m, [275m, 225m], method: OrderPaymentMethod.Transfer).Method);
        // Everything deducted, it hands over nothing.
        Assert.Equal(0m, Settle(0m, [50m], [new(CourierSettlementAdjustmentKind.TripFee, null, -50m)]).HandedAmount);
    }

    public static TheoryData<Func<CourierSettlement>, string> RefusedSettlements => new()
    {
        { () => Settle(0m, []), CourierSettlement.NoOrdersMessage },
        { () => Settle(550m, [275m, 275m], sameOrder: true), CourierSettlement.RepeatedOrderMessage },
        { () => Settle(0m, [0m]), CourierSettlement.OrderAmountMessage },
        { () => Settle(10.005m, [10.005m]), CourierSettlement.HandedAmountMessage },
        { () => Settle(-1m, [275m], [new(CourierSettlementAdjustmentKind.Shortage, null, -276m)]), CourierSettlement.HandedAmountMessage },
        { () => Settle(1_000_001m, [1_000_001m]), CourierSettlement.HandedAmountMessage },
        { () => Settle(275m, [275m], method: OrderPaymentMethod.Card), CourierSettlement.MethodMessage },
        { () => Settle(275m, [275m], method: OrderPaymentMethod.Platform), CourierSettlement.MethodMessage },
        { () => Settle(285m, [275m], [new(CourierSettlementAdjustmentKind.TripFee, null, 10m)]), CourierSettlement.DeductionMessage },
        { () => Settle(275m, [275m], [new(CourierSettlementAdjustmentKind.Other, null, 0m)]), CourierSettlement.AdjustmentAmountMessage },
        { () => Settle(285m, [275m], [new(CourierSettlementAdjustmentKind.Other, "  ", 10m)]), CourierSettlement.AdjustmentDescriptionRequiredMessage },
        { () => Settle(285m, [275m], [new(CourierSettlementAdjustmentKind.Other, new string('a', 201), 10m)]), CourierSettlement.AdjustmentDescriptionTooLongMessage },
        { () => Settle(265m, [275m], [new((CourierSettlementAdjustmentKind)7, null, -10m)]), CourierSettlement.AdjustmentKindMessage },
        { () => Settle(275m, [275m], reference: new string('a', 61)), CourierSettlement.ReferenceTooLongMessage },
        { () => Settle(275m, [275m], notes: new string('a', 501)), CourierSettlement.NotesTooLongMessage },
        { () => Settle(275m, [275m], courier: (DeliveryCourier)9), CourierSettlement.CourierMessage },
        { () => Settle(275m, [275m], drawer: Guid.Empty), CourierSettlement.DrawerRequiredMessage },
        { () => Settle(201m, Enumerable.Repeat(1m, 201).ToArray()), CourierSettlement.TooManyOrdersMessage },
        { () => Settle(224m, [275m], Enumerable.Repeat(new CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind.TripFee, null, -1m), 51).ToArray()),
            CourierSettlement.TooManyAdjustmentsMessage },
    };

    [Theory]
    [MemberData(nameof(RefusedSettlements))]
    public void Settlement_ShouldSayWhatIsWrong(Func<CourierSettlement> settle, string message)
        => Assert.Equal(message, Assert.Throws<DomainException>(() => settle()).Message);

    [Fact]
    public void EnumValues_ShouldStayAsStored()
    {
        Assert.Equal([1, 2, 3, 99], Enum.GetValues<CourierSettlementAdjustmentKind>().Select(k => (int)k));
        Assert.True(CourierSettlement.IsDeduction(CourierSettlementAdjustmentKind.TripFee));
        Assert.False(CourierSettlement.IsDeduction(CourierSettlementAdjustmentKind.Other));
    }

    private static CourierSettlement Settle(
        decimal handedAmount,
        decimal[] orders,
        CourierSettlementAdjustmentInput[]? adjustments = null,
        OrderPaymentMethod method = OrderPaymentMethod.Cash,
        string? reference = null,
        string? notes = null,
        DeliveryCourier courier = DeliveryCourier.Paquetenvia,
        Guid? drawer = null,
        bool sameOrder = false)
    {
        var id = Guid.NewGuid();
        return new CourierSettlement(
            Tenant, courier, drawer ?? Guid.NewGuid(), method, handedAmount,
            orders.Select(amount => new CourierSettlementOrderInput(sameOrder ? id : Guid.NewGuid(), amount)),
            adjustments ?? [], reference, notes, Now, "caja");
    }

    private static Order Confirmed(Order order)
    {
        order.TransitionTo(OrderStatus.Confirmed, new FoodOrderStateMachine(), "caja");
        return order;
    }

    private static Order OrderArranged(decimal amount, decimal? paysWith = null)
    {
        var order = OrderWith(amount);
        order.ArrangeDelivery(Address, Zone(fee: 35m), VatRate.General, paysWith);
        return order;
    }

    private static Order OrderWith(decimal amount)
    {
        var order = new Order(
            Tenant, null, Guid.NewGuid(), createdByBusiness: true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail,
            "Ana", customerPhone: "5512345678", customerNotes: null, tableNumber: null, pricesIncludeTax: true,
            channel: OrderChannel.WhatsApp);
        order.AddItem(Guid.NewGuid(), "Comida corrida", 1, amount, vatRate: VatRate.General);
        return order;
    }

    private static DeliveryZone Zone(decimal fee = 35m)
        => new(Tenant, new DeliveryZoneFields("Centro", fee, 0m, null, ["72000"], true));
}
