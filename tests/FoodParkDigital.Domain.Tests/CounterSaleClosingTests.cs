using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

public class CounterSaleClosingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FullyPaidCounterSale_ShouldBeDeliveredWithoutMessagingTheCustomer()
    {
        var order = NewConfirmedOrder(BusinessType.Retail, createdByBusiness: true);
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
        order.ClearDomainEvents();

        var closed = order.CloseIfPaidCounterSale(new RetailOrderStateMachine(), waitsForKitchen: false, "cajero");

        Assert.True(closed);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(OrderStatus.Delivered, order.StatusHistory.Last().NewStatus);
        var statusChanged = Assert.IsType<OrderStatusChangedEvent>(Assert.Single(order.DomainEvents));
        Assert.False(statusChanged.NotifyCustomer);
    }

    [Fact]
    public void PartiallyPaidCounterSale_ShouldStayOpen()
    {
        var order = NewConfirmedOrder(BusinessType.Retail, createdByBusiness: true);
        order.RegisterPayment(order.Total - 1m, OrderPaymentMethod.Cash, Now, null, null, null, null);

        Assert.False(order.CloseIfPaidCounterSale(new RetailOrderStateMachine(), waitsForKitchen: false, "cajero"));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void PaidCustomerOrder_ShouldStayOpen()
    {
        // Placed by the customer (WhatsApp, online): it still has to be prepared and handed over.
        var order = NewConfirmedOrder(BusinessType.Retail, createdByBusiness: false);
        order.RegisterPayment(order.Total, OrderPaymentMethod.Transfer, Now, null, null, null, null);

        Assert.False(order.CloseIfPaidCounterSale(new RetailOrderStateMachine(), waitsForKitchen: false, "cajero"));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void PaidFoodWaitingForTheKitchen_ShouldCloseOnlyOnceReady()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = NewConfirmedOrder(BusinessType.Food, createdByBusiness: true);
        order.RegisterPayment(order.Total, OrderPaymentMethod.Card, Now, null, null, null, null);

        Assert.False(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: true, "cajero"));
        Assert.Equal(OrderStatus.Confirmed, order.Status);

        order.TransitionTo(OrderStatus.Ready, stateMachine, "cocina");
        Assert.True(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: true, "cajero"));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void PaidFoodWithoutKitchen_ShouldCloseRightAway()
    {
        var order = NewConfirmedOrder(BusinessType.Food, createdByBusiness: true);
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);

        Assert.True(order.CloseIfPaidCounterSale(new FoodOrderStateMachine(), waitsForKitchen: false, "cajero"));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void PaidServiceWithoutTrackedPieces_ShouldBeCompleted()
    {
        // A haircut charged at the counter: the work is done when it is paid.
        var order = NewServiceOrder();
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);

        Assert.True(order.CloseIfPaidCounterSale(new ServiceOrderStateMachine(), waitsForKitchen: false, "cajero"));
        Assert.Equal(OrderStatus.Completed, order.Status);
    }

    [Fact]
    public void PaidServiceWithPiecesInTheWorkshop_ShouldStayOpen()
    {
        // Laundry paid in advance: it closes when its pieces are completed, not when it is paid.
        var order = NewServiceOrder();
        order.AddServicePiece("Camisa", null, null, null, null, null, null, "Lavado", null, isTrackable: true);
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);

        Assert.False(order.CloseIfPaidCounterSale(new ServiceOrderStateMachine(), waitsForKitchen: false, "cajero"));
        Assert.Equal(OrderStatus.Accepted, order.Status);
    }

    private static Order NewServiceOrder()
    {
        var order = new Order(
            Guid.NewGuid(), null, null, true, "ORD-000002", BusinessType.Service, OrderType.Service,
            "Cliente", null, null, null);
        order.AddItem(Guid.NewGuid(), "Servicio", 1, 100m);
        order.TransitionTo(OrderStatus.Accepted, new ServiceOrderStateMachine(), "cajero");
        return order;
    }

    private static Order NewConfirmedOrder(BusinessType businessType, bool createdByBusiness)
    {
        var order = new Order(
            tenantId: Guid.NewGuid(),
            customerUserId: null,
            temporaryCustomerId: createdByBusiness ? null : Guid.NewGuid(),
            createdByBusiness: createdByBusiness,
            orderNumber: "ORD-000001",
            businessType: businessType,
            orderType: OrderType.Retail,
            customerName: "Cliente",
            customerPhone: null,
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Producto", 2, 50m);
        order.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(businessType, OrderType.Retail), "cajero");
        return order;
    }
}
