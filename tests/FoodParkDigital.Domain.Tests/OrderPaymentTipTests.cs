using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The tip travels with a payment but is never part of the sale; the cash handed over keeps the change.</summary>
public class OrderPaymentTipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tip_ShouldStayOutOfTheTotalAndTheBalance()
    {
        var order = NewConfirmedOrder();
        var total = order.Total;

        var payment = order.RegisterPayment(total, OrderPaymentMethod.Cash, Now, null, null, null, null, tip: 15m, cashReceived: total + 50m);

        Assert.Equal(total, order.Total);
        Assert.Equal(total, order.PaidAmount);
        Assert.Equal(0m, order.Balance);
        Assert.Equal(15m, order.TipTotal);
        Assert.Equal(15m, payment.Tip);
        Assert.Equal(total + 50m, payment.CashReceived);
        Assert.Equal(35m, payment.Change);
    }

    [Fact]
    public void Tip_MayBeLargerThanASmallLastPayment_ButNotThanTheBill()
    {
        var order = NewConfirmedOrder();
        order.RegisterPayment(order.Total - 5m, OrderPaymentMethod.Card, Now, null, null, null, null);

        // A mixed charge: the last 5 pesos in cash with a 20 peso tip is a real case.
        var last = order.RegisterPayment(5m, OrderPaymentMethod.Cash, Now, null, null, null, null, tip: 20m, cashReceived: 25m);
        var tooMuch = Assert.Throws<DomainException>(() => NewConfirmedOrder()
            .RegisterPayment(10m, OrderPaymentMethod.Card, Now, null, null, null, null, tip: 1_000m));

        Assert.Equal(20m, last.Tip);
        Assert.Equal(0m, last.Change);
        Assert.Equal(Order.TipAboveTotalMessage, tooMuch.Message);
    }

    [Fact]
    public void CashReceived_ShouldBeCashOnly_AndCoverThePaymentAndTheTip()
    {
        var order = NewConfirmedOrder();

        var negativeTip = Assert.Throws<DomainException>(() =>
            order.RegisterPayment(10m, OrderPaymentMethod.Cash, Now, null, null, null, null, tip: -1m));
        var onCard = Assert.Throws<DomainException>(() =>
            order.RegisterPayment(10m, OrderPaymentMethod.Card, Now, null, null, null, null, cashReceived: 20m));
        var short_ = Assert.Throws<DomainException>(() =>
            order.RegisterPayment(10m, OrderPaymentMethod.Cash, Now, null, null, null, null, tip: 2m, cashReceived: 11m));

        Assert.Equal("La propina no puede ser negativa.", negativeTip.Message);
        Assert.Equal(OrderPayment.CashReceivedOnlyForCashMessage, onCard.Message);
        Assert.Equal(OrderPayment.CashReceivedShortMessage, short_.Message);
        Assert.Empty(order.Payments);
        Assert.Equal(0m, order.PaidAmount);
    }

    [Fact]
    public void PaymentWithoutCashReceived_ShouldHaveNoChange()
    {
        var order = NewConfirmedOrder();

        var payment = order.RegisterPayment(10m, OrderPaymentMethod.Cash, Now, null, null, null, null, tip: 1m);

        Assert.Null(payment.CashReceived);
        Assert.Equal(0m, payment.Change);
    }

    private static Order NewConfirmedOrder()
    {
        var order = new Order(
            tenantId: Guid.NewGuid(),
            customerUserId: null,
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Food,
            orderType: OrderType.Retail,
            customerName: "Mesa 4",
            customerPhone: null,
            customerNotes: null,
            tableNumber: "4");
        order.AddItem(Guid.NewGuid(), "Comida corrida", 2, 50m);
        order.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail), "mesero");
        return order;
    }
}
