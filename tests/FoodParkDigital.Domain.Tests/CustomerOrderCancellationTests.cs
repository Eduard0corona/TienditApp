using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The customer cancels their order of the online menu (Fase 29R, ADR 0072): while the business has not accepted it, nothing
/// was paid and it is not of a table. The history and the event say it was them.
/// </summary>
public class CustomerOrderCancellationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly FoodOrderStateMachine Food = new();

    [Fact]
    public void AnOnlineOrderNotAcceptedYet_ShouldBeCancelledByItsCustomer_AndSaySo()
    {
        var order = OnlineOrder();
        Assert.True(order.CustomerMayCancel);

        order.CancelByCustomer(Food);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Order.CustomerCancellationReason, order.CancellationReason);
        Assert.False(order.CustomerMayCancel);
        var history = order.StatusHistory.Last();
        Assert.Equal((OrderStatus.Pending, OrderStatus.Cancelled, Order.CustomerPerformedBy), (history.PreviousStatus, history.NewStatus, history.ChangedBy));
        var cancelled = Assert.IsType<OrderCancelledEvent>(order.DomainEvents.Last());
        Assert.Equal((OrderStatus.Pending, Order.CustomerCancellationReason, true), (cancelled.PreviousStatus, cancelled.Reason, cancelled.ByCustomer));
    }

    [Fact]
    public void TheBusinessCancelling_ShouldNotSayItWasTheCustomer()
    {
        var order = OnlineOrder();

        order.Cancel(Food, "cajero", "Sin existencias");

        Assert.False(Assert.IsType<OrderCancelledEvent>(order.DomainEvents.Last()).ByCustomer);
    }

    [Fact]
    public void AnOrderAcceptedPaidOfATableOrTakenByTheBusiness_ShouldOnlyBeCancelledByTheBusiness()
    {
        var accepted = OnlineOrder();
        accepted.TransitionTo(OrderStatus.Confirmed, Food, "cajero");
        var paid = OnlineOrder();
        paid.RegisterOnlinePayment("mp-123", Now, null);
        var atATable = OnlineOrder();
        atATable.OpenAsTab(Guid.NewGuid(), "Mesa 4", Now, "mesero");
        var byWhatsApp = OnlineOrder(OrderChannel.WhatsApp);

        Assert.Equal(Order.CustomerCancelAcceptedMessage, Refusal(accepted));
        Assert.Equal(Order.CustomerCancelPaidMessage, Refusal(paid));
        Assert.Equal(Order.CustomerCancelTableMessage, Refusal(atATable));
        Assert.Equal(Order.CustomerCancelNotOnlineMessage, Refusal(byWhatsApp));
        Assert.All([accepted, paid, atATable, byWhatsApp], order =>
        {
            Assert.False(order.CustomerMayCancel);
            Assert.NotEqual(OrderStatus.Cancelled, order.Status);
            Assert.DoesNotContain(order.DomainEvents, e => e is OrderCancelledEvent);
        });
    }

    [Fact]
    public void ACancelledOrder_ShouldNotBeCancelledAgain()
    {
        var order = OnlineOrder();
        order.CancelByCustomer(Food);

        Assert.Equal(Order.CustomerCancelAcceptedMessage, Refusal(order));
        Assert.Single(order.DomainEvents, e => e is OrderCancelledEvent);
    }

    private static string Refusal(Order order) => Assert.Throws<DomainException>(() => order.CancelByCustomer(Food)).Message;

    private static Order OnlineOrder(OrderChannel channel = OrderChannel.Online)
    {
        var order = new Order(
            tenantId: TenantId,
            customerUserId: null,
            temporaryCustomerId: Guid.NewGuid(),
            createdByBusiness: false,
            orderNumber: "ORD-000412",
            businessType: BusinessType.Food,
            orderType: OrderType.Retail,
            customerName: "Valeria Morales",
            customerPhone: "+525522107765",
            customerNotes: null,
            tableNumber: null,
            pricesIncludeTax: true,
            channel: channel);
        order.AddItem(Guid.NewGuid(), "Torta de jamón", 2, 117.75m);
        return order;
    }
}
