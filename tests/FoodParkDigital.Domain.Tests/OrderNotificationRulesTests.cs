using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>Which orders tell the customer by WhatsApp, and how often (each message is a paid template).</summary>
public class OrderNotificationRulesTests
{
    [Theory]
    [InlineData(true, OrderChannel.Counter, OrderType.Retail, false)] // At the counter: the customer has the ticket.
    [InlineData(true, OrderChannel.Counter, OrderType.Service, true)] // A service: the customer comes back for it.
    [InlineData(true, OrderChannel.WhatsApp, OrderType.Retail, true)] // Taken by the business from a chat.
    [InlineData(false, OrderChannel.Online, OrderType.Retail, true)] // Online menu.
    [InlineData(false, OrderChannel.Counter, OrderType.Retail, true)] // Placed from the customer's account.
    public void DefaultPolicy_ShouldMessageOnlyCustomersWhoAreNotAtTheCounter(
        bool createdByBusiness, OrderChannel channel, OrderType orderType, bool expected)
    {
        Assert.Equal(expected, OrderNotificationRules.ShouldNotifyCustomer(
            OrderNotificationPolicy.RemoteAndServices, createdByBusiness, channel, orderType));
    }

    [Fact]
    public void AllOrdersAndOff_ShouldNotDependOnWhereTheOrderCameFrom()
    {
        Assert.True(OrderNotificationRules.ShouldNotifyCustomer(
            OrderNotificationPolicy.AllOrders, true, OrderChannel.Counter, OrderType.Retail));
        Assert.False(OrderNotificationRules.ShouldNotifyCustomer(
            OrderNotificationPolicy.Off, false, OrderChannel.Online, OrderType.Retail));
        Assert.False(OrderNotificationRules.ShouldNotifyCustomer(
            OrderNotificationPolicy.Off, true, OrderChannel.Counter, OrderType.Service));
    }

    [Fact]
    public void Settings_ShouldStartWithTheDefaultPolicyAndRejectUnknownOnes()
    {
        var settings = new TenantSettings(Guid.NewGuid());
        Assert.Equal(OrderNotificationPolicy.RemoteAndServices, settings.OrderNotificationPolicy);

        settings.SetOrderNotificationPolicy(OrderNotificationPolicy.Off);
        Assert.Throws<DomainException>(() => settings.SetOrderNotificationPolicy((OrderNotificationPolicy)7));

        Assert.Equal(OrderNotificationPolicy.Off, settings.OrderNotificationPolicy);
    }

    [Fact]
    public void StatusANewOrderStartsIn_ShouldNotMessageTheCustomerAgain()
    {
        var order = NewOrder();

        order.AdvanceOnCreation(OrderStatus.Confirmed, new RetailOrderStateMachine(), "cajero");
        order.MarkAsCreated();

        // The customer hears about the order once, from its creation, which carries the status it starts in.
        Assert.False(Assert.Single(order.DomainEvents.OfType<OrderStatusChangedEvent>()).NotifyCustomer);
        Assert.Equal(OrderStatus.Confirmed, Assert.Single(order.DomainEvents.OfType<OrderCreatedEvent>()).Status);
        Assert.Equal(OrderStatus.Confirmed, order.StatusHistory.Last().NewStatus);
    }

    [Fact]
    public void StatusChangesAfterTheOrderWasCreated_ShouldMessageTheCustomer()
    {
        var order = NewOrder();

        order.MarkAsCreated();
        order.TransitionTo(OrderStatus.Confirmed, new RetailOrderStateMachine(), "cajero");

        Assert.Equal(OrderStatus.Pending, Assert.Single(order.DomainEvents.OfType<OrderCreatedEvent>()).Status);
        Assert.True(Assert.Single(order.DomainEvents.OfType<OrderStatusChangedEvent>()).NotifyCustomer);
    }

    [Fact]
    public void StatusANewOrderStartsIn_ShouldFollowTheStateMachine()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOrderStateTransitionException>(
            () => order.AdvanceOnCreation(OrderStatus.Ready, new RetailOrderStateMachine(), "cajero"));

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Empty(order.DomainEvents);
    }

    private static Order NewOrder()
    {
        var order = new Order(
            tenantId: Guid.NewGuid(),
            customerUserId: null,
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Retail,
            orderType: OrderType.Retail,
            customerName: "Karla",
            customerPhone: "+525511112222",
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Producto", 1, 50m);
        return order;
    }
}
