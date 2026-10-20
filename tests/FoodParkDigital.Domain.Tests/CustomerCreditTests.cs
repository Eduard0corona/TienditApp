using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Fiado (Fase 26A, ADR 0033): the business says how much a customer may owe, and a counter sale sold on credit closes as
/// if paid while its balance is what the customer owes.
/// </summary>
public class CustomerCreditTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreditLimit_ShouldBeSetToCentavos_AndRemoved()
    {
        var customer = new Customer(Guid.NewGuid(), "Ana López", "+525512345678", null);

        customer.SetCreditLimit(1500.50m);
        Assert.Equal(1500.50m, customer.CreditLimit);
        Assert.True(customer.SellsOnCredit);

        customer.SetCreditLimit(null);
        Assert.Null(customer.CreditLimit);
        Assert.False(customer.SellsOnCredit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.99)]
    [InlineData(-5)]
    [InlineData(1000000.01)]
    [InlineData(100.005)]
    public void CreditLimit_ShouldRefuse_AnAmountOutOfRange_OrBelowACentavo(double limit)
    {
        var customer = new Customer(Guid.NewGuid(), "Ana López", "+525512345678", null);

        var refused = Assert.Throws<DomainException>(() => customer.SetCreditLimit((decimal)limit));

        Assert.Equal(Customer.CreditLimitRangeMessage, refused.Message);
        Assert.Null(customer.CreditLimit);
    }

    [Fact]
    public void CreditLimit_ShouldNeed_ARealCustomerWithAPhone()
    {
        var walkIn = new Customer(Guid.NewGuid(), "Público en general", null, null, isPublicGeneral: true);
        var noPhone = new Customer(Guid.NewGuid(), "Ana López", null, null);

        Assert.Equal(Customer.PublicGeneralCreditMessage, Assert.Throws<DomainException>(() => walkIn.SetCreditLimit(100m)).Message);
        Assert.Equal(Customer.NoPhoneCreditMessage, Assert.Throws<DomainException>(() => noPhone.SetCreditLimit(100m)).Message);
    }

    [Fact]
    public void InactiveCustomer_ShouldKeepTheLimit_ButNotBeSoldOnCredit()
    {
        var customer = new Customer(Guid.NewGuid(), "Ana López", "+525512345678", null);
        customer.SetCreditLimit(500m);

        customer.SetActive(false);

        Assert.Equal(500m, customer.CreditLimit);
        Assert.False(customer.SellsOnCredit);
    }

    [Fact]
    public void CounterSaleOnCredit_ShouldCloseWithItsBalance_AndStaySoldOnCreditOncePaid()
    {
        var stateMachine = new RetailOrderStateMachine();
        var order = NewCounterSale(BusinessType.Retail, phone: "+525512345678");

        order.SellOnCredit();
        var closed = order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: false, "cajero");

        Assert.True(order.SoldOnCredit);
        Assert.True(closed);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(order.Total, order.Balance);

        // The customer pays later: the payment lands on the delivered order, which stays a sale on credit.
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(0m, order.Balance);
        Assert.True(order.SoldOnCredit);
    }

    [Fact]
    public void CounterSaleOnCredit_ShouldTakePartOfItsTotalNow()
    {
        var order = NewCounterSale(BusinessType.Retail, phone: "+525512345678");
        order.RegisterPayment(30m, OrderPaymentMethod.Cash, Now, null, null, null, null);

        order.SellOnCredit();

        Assert.True(order.CloseIfPaidCounterSale(new RetailOrderStateMachine(), waitsForKitchen: false, "cajero"));
        Assert.Equal(order.Total - 30m, order.Balance);
    }

    [Fact]
    public void FoodOnCredit_ShouldWaitForTheKitchen_LikeAPaidOne()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = NewCounterSale(BusinessType.Food, phone: "+525512345678");
        order.SellOnCredit();

        Assert.False(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: true, "cajero"));
        order.TransitionTo(OrderStatus.Ready, stateMachine, "cocina");

        Assert.True(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: true, "cajero"));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void UnpaidSaleNotOnCredit_ShouldStayOpen()
    {
        var order = NewCounterSale(BusinessType.Retail, phone: "+525512345678");

        Assert.False(order.CloseIfPaidCounterSale(new RetailOrderStateMachine(), waitsForKitchen: false, "cajero"));
        Assert.False(order.SoldOnCredit);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void SaleOnCredit_ShouldNeed_TheCustomersPhone_AndSomethingLeftToPay()
    {
        var walkIn = NewCounterSale(BusinessType.Retail, phone: null);
        var paid = NewCounterSale(BusinessType.Retail, phone: "+525512345678");
        paid.RegisterPayment(paid.Total, OrderPaymentMethod.Card, Now, null, null, null, null);

        Assert.Equal(Order.CreditNeedsCustomerMessage, Assert.Throws<DomainException>(walkIn.SellOnCredit).Message);
        Assert.Equal(Order.CreditNeedsBalanceMessage, Assert.Throws<DomainException>(paid.SellOnCredit).Message);
        Assert.False(walkIn.SoldOnCredit);
        Assert.False(paid.SoldOnCredit);
    }

    [Fact]
    public void SaleOnCredit_ShouldBe_ACounterSaleOfTheBusiness()
    {
        // Placed by the customer (WhatsApp, online), or a tab charged when the table leaves: neither is sold on credit.
        var customerOrder = new Order(
            Guid.NewGuid(), null, Guid.NewGuid(), false, "ORD-000003", BusinessType.Retail, OrderType.Retail,
            "Cliente", "+525512345678", null, null);
        customerOrder.AddItem(Guid.NewGuid(), "Producto", 1, 100m);
        var tab = NewCounterSale(BusinessType.Food, phone: "+525512345678", confirm: false);
        tab.OpenAsTab(Guid.NewGuid(), "Mesa 1", Now, "mesero");

        Assert.Equal(Order.CreditOnlyCounterSaleMessage, Assert.Throws<DomainException>(customerOrder.SellOnCredit).Message);
        Assert.Equal(Order.CreditOnlyCounterSaleMessage, Assert.Throws<DomainException>(tab.SellOnCredit).Message);
    }

    private static Order NewCounterSale(BusinessType businessType, string? phone, bool confirm = true)
    {
        var order = new Order(
            tenantId: Guid.NewGuid(),
            customerUserId: null,
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: businessType,
            orderType: OrderType.Retail,
            customerName: phone is null ? "Público en general" : "Ana López",
            customerPhone: phone,
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Producto", 2, 50m);
        if (confirm)
            order.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(businessType, OrderType.Retail), "cajero");
        return order;
    }
}
