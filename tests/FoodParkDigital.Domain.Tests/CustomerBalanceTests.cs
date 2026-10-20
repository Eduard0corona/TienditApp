using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Saldo a favor del cliente (Fase 26D, ADR 0036): what a customer leaves, what a refund gives them, what they pay with it and
/// what is given back, as movements of their account that are never edited; the money enters or leaves a drawer only when
/// they leave it or get it back, and paying with it records the sale outside every drawer.
/// </summary>
public class CustomerBalanceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private const string Phone = "+525512345678";

    [Fact]
    public void Deposit_ShouldAddToTheBalance_ThroughTheDrawerOfWhoeverCharges()
    {
        var customer = Guid.NewGuid();
        var drawer = Guid.NewGuid();

        var entry = CustomerBalanceEntry.Deposit(
            Tenant, $"  {Phone} ", customer, 250m, OrderPaymentMethod.Transfer, drawer, "  SPEI 123  ", "  Para el pastel  ",
            Now, "cajero@cafe.mx");

        Assert.Equal(
            (Tenant, Phone, (Guid?)customer, CustomerBalanceEntryKind.Deposit, 250m, (OrderPaymentMethod?)OrderPaymentMethod.Transfer),
            (entry.TenantId, entry.Phone, entry.CustomerId, entry.Kind, entry.Amount, entry.Method));
        Assert.Equal(((Guid?)drawer, "SPEI 123", "Para el pastel", Now, "cajero@cafe.mx"),
            (entry.CashSessionId, entry.Reference, entry.Notes, entry.OccurredAt, entry.RecordedBy));
        Assert.Equal(((Guid?)null, (Guid?)null, (Guid?)null), (entry.OrderId, entry.OrderPaymentId, entry.OrderRefundId));
    }

    [Fact]
    public void Returned_ShouldTakeFromTheBalance_OutOfTheDrawerOfWhoeverGivesItBack()
    {
        var drawer = Guid.NewGuid();

        var entry = CustomerBalanceEntry.Returned(
            Tenant, Phone, null, 80.5m, OrderPaymentMethod.Cash, drawer, null, "   ", Now, "dueno");

        Assert.Equal((CustomerBalanceEntryKind.Returned, -80.5m, (OrderPaymentMethod?)OrderPaymentMethod.Cash, (Guid?)drawer),
            (entry.Kind, entry.Amount, entry.Method, entry.CashSessionId));
        Assert.Equal(((Guid?)null, (string?)null, (string?)null), (entry.CustomerId, entry.Reference, entry.Notes));
    }

    [Fact]
    public void Applied_ShouldTakeFromTheBalance_TiedToThePayment_WithoutADrawer()
    {
        var order = Guid.NewGuid();
        var payment = Guid.NewGuid();

        var entry = CustomerBalanceEntry.Applied(Tenant, Phone, null, 120m, order, payment, Now, "cajero");

        Assert.Equal((CustomerBalanceEntryKind.Applied, -120m, (Guid?)order, (Guid?)payment),
            (entry.Kind, entry.Amount, entry.OrderId, entry.OrderPaymentId));
        Assert.Equal(((OrderPaymentMethod?)null, (Guid?)null, (Guid?)null), (entry.Method, entry.CashSessionId, entry.OrderRefundId));
    }

    [Fact]
    public void Refunded_ShouldAddToTheBalance_TiedToTheRefund_WithoutADrawer()
    {
        var order = Guid.NewGuid();
        var refund = Guid.NewGuid();

        var entry = CustomerBalanceEntry.Refunded(Tenant, Phone, null, 45m, order, refund, Now, "dueno");

        Assert.Equal((CustomerBalanceEntryKind.Refund, 45m, (Guid?)order, (Guid?)refund),
            (entry.Kind, entry.Amount, entry.OrderId, entry.OrderRefundId));
        Assert.Equal(((OrderPaymentMethod?)null, (Guid?)null, (Guid?)null), (entry.Method, entry.CashSessionId, entry.OrderPaymentId));
    }

    [Fact]
    public void Notes_AndReference_ShouldBeClipped_ToTheirLength()
    {
        var entry = CustomerBalanceEntry.Deposit(
            Tenant, Phone, null, 10m, OrderPaymentMethod.Card, Guid.NewGuid(), new string('r', 150), new string('n', 600),
            Now, "cajero");

        Assert.Equal((CustomerBalanceEntry.MaxReferenceLength, CustomerBalanceEntry.MaxNotesLength),
            (entry.Reference!.Length, entry.Notes!.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    [InlineData(1000000.01)]
    public void EveryMovement_ShouldBeAboveZero_InCentavos_UpToAMillion(double amount)
    {
        var value = (decimal)amount;

        Assert.All(
            new Func<CustomerBalanceEntry>[]
            {
                () => CustomerBalanceEntry.Deposit(Tenant, Phone, null, value, OrderPaymentMethod.Cash, Guid.NewGuid(), null, null, Now, "c"),
                () => CustomerBalanceEntry.Returned(Tenant, Phone, null, value, OrderPaymentMethod.Cash, Guid.NewGuid(), null, null, Now, "c"),
                () => CustomerBalanceEntry.Applied(Tenant, Phone, null, value, Guid.NewGuid(), Guid.NewGuid(), Now, "c"),
                () => CustomerBalanceEntry.Refunded(Tenant, Phone, null, value, Guid.NewGuid(), Guid.NewGuid(), Now, "c"),
            },
            record => Assert.Equal(CustomerBalanceEntry.AmountMessage, Assert.Throws<DomainException>(record).Message));
        Assert.Equal(1_000_000m, CustomerBalanceEntry.Deposit(
            Tenant, Phone, null, 1_000_000m, OrderPaymentMethod.Cash, Guid.NewGuid(), null, null, Now, "c").Amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+52551234567890123456")]
    public void EveryMovement_ShouldBeOfAnAccount_ItsPhone(string phone)
    {
        var refused = Assert.Throws<DomainException>(() =>
            CustomerBalanceEntry.Applied(Tenant, phone, null, 10m, Guid.NewGuid(), Guid.NewGuid(), Now, "c"));

        Assert.Equal(CustomerBalanceEntry.PhoneMessage, refused.Message);
    }

    [Theory]
    [InlineData(OrderPaymentMethod.CustomerBalance)]
    [InlineData(OrderPaymentMethod.Platform)]
    [InlineData(OrderPaymentMethod.Online)]
    [InlineData((OrderPaymentMethod)42)]
    public void WhatIsLeftOrGivenBack_ShouldMoveThroughADrawer_InPerson(OrderPaymentMethod method)
    {
        var deposit = Assert.Throws<DomainException>(() =>
            CustomerBalanceEntry.Deposit(Tenant, Phone, null, 10m, method, Guid.NewGuid(), null, null, Now, "c"));
        var returned = Assert.Throws<DomainException>(() =>
            CustomerBalanceEntry.Returned(Tenant, Phone, null, 10m, method, Guid.NewGuid(), null, null, Now, "c"));

        Assert.Equal(CustomerBalanceEntry.InPersonMethodMessage, deposit.Message);
        Assert.Equal(CustomerBalanceEntry.InPersonMethodMessage, returned.Message);
    }

    [Theory]
    [InlineData(OrderPaymentMethod.Cash)]
    [InlineData(OrderPaymentMethod.Card)]
    [InlineData(OrderPaymentMethod.Transfer)]
    [InlineData(OrderPaymentMethod.DigitalWallet)]
    [InlineData(OrderPaymentMethod.Other)]
    public void WhatIsLeft_ShouldAcceptEveryMethodChargedInPerson(OrderPaymentMethod method)
    {
        var entry = CustomerBalanceEntry.Deposit(Tenant, Phone, null, 10m, method, Guid.NewGuid(), null, null, Now, "c");

        Assert.Equal(method, entry.Method);
    }

    [Fact]
    public void WhatLeavesTheBalance_ShouldNeverTakeItBelowZero()
    {
        Assert.Null(CustomerBalanceEntry.CoverProblem(100m, 100m, Money));

        Assert.Equal("No le alcanza el saldo a favor: tiene $100.00.", CustomerBalanceEntry.CoverProblem(100m, 100.01m, Money));
        Assert.Equal("No tiene saldo a favor.", CustomerBalanceEntry.CoverProblem(0m, 1m, Money));
        Assert.Equal("No tiene saldo a favor.", CustomerBalanceEntry.CoverProblem(-3m, 1m, Money));
    }

    [Fact]
    public void CustomerBalance_ShouldNeverEnterADrawer_AndBeDeclaredAsAnAdvanceApplied()
    {
        Assert.False(OrderPaymentMethodRules.EntersADrawer(OrderPaymentMethod.CustomerBalance));
        Assert.False(OrderPaymentMethodRules.RecordedOnlyOnCreation(OrderPaymentMethod.CustomerBalance));
        Assert.Equal(OrderPaymentMethodRules.CustomerBalanceNeverEntersADrawerMessage,
            OrderPaymentMethodRules.NeverEntersADrawer(OrderPaymentMethod.CustomerBalance));
        Assert.Equal(OrderPaymentMethodRules.OnlineNeverEntersADrawerMessage,
            OrderPaymentMethodRules.NeverEntersADrawer(OrderPaymentMethod.Online));
        Assert.Equal(OrderPaymentMethodRules.NeverEntersADrawerMessage,
            OrderPaymentMethodRules.NeverEntersADrawer(OrderPaymentMethod.Platform));
        Assert.Equal("30", SatCatalog.PaymentForm(OrderPaymentMethod.CustomerBalance));
    }

    [Fact]
    public void Drawer_ShouldTakeWhatIsLeftIn_AndGiveWhatIsReturnedOut_ButNeverAPaymentWithTheBalance()
    {
        var drawer = new CashSession(Tenant, Now, "cajero", 500m);

        var left = drawer.RegisterTransaction(CashTransactionType.CustomerDeposit, 200m, Now, OrderPaymentMethod.Cash);
        var leftByCard = drawer.RegisterTransaction(CashTransactionType.CustomerDeposit, 150m, Now, OrderPaymentMethod.Card);
        var returned = drawer.RegisterTransaction(CashTransactionType.CustomerDepositReturn, 50m, Now, OrderPaymentMethod.Cash);

        Assert.Equal((200m, 150m, -50m), (left.Amount, leftByCard.Amount, returned.Amount));
        Assert.Equal((650m, 150m), (drawer.ExpectedBalance, drawer.NonCashInflows));
        Assert.Equal("El monto de un ingreso debe ser mayor a cero.", Assert.Throws<DomainException>(() =>
            drawer.RegisterTransaction(CashTransactionType.CustomerDeposit, -1m, Now, OrderPaymentMethod.Cash)).Message);
        Assert.Equal(OrderPaymentMethodRules.CustomerBalanceNeverEntersADrawerMessage, Assert.Throws<DomainException>(() =>
            drawer.RegisterTransaction(CashTransactionType.SalePayment, 10m, Now, OrderPaymentMethod.CustomerBalance)).Message);
        Assert.False(CashSession.IsManualTransactionType(CashTransactionType.CustomerDeposit));
        Assert.False(CashSession.IsManualTransactionType(CashTransactionType.CustomerDepositReturn));
        Assert.Equal(3, drawer.Transactions.Count);
    }

    [Fact]
    public void Payment_WithTheBalance_ShouldRecordTheSale_OfAnOrderWithAPhone()
    {
        var order = CounterSale();

        var payment = order.RegisterPayment(60m, OrderPaymentMethod.CustomerBalance, Now, null, null, null, null);
        order.RegisterPayment(40m, OrderPaymentMethod.Cash, Now, null, null, null, null);

        Assert.Equal((OrderPaymentMethod.CustomerBalance, 60m), (payment.Method, payment.Amount));
        Assert.Equal((100m, 0m), (order.PaidAmount, order.Balance));
    }

    [Fact]
    public void Payment_WithTheBalance_ShouldNeedThePhone_AndNoTip()
    {
        var withoutPhone = CounterSale(customerPhone: null);
        Assert.Equal(OrderPaymentMethodRules.CustomerBalanceNeedsPhoneMessage, Assert.Throws<DomainException>(() =>
            withoutPhone.RegisterPayment(10m, OrderPaymentMethod.CustomerBalance, Now, null, null, null, null)).Message);

        var order = CounterSale();
        Assert.Equal(OrderPaymentMethodRules.CustomerBalanceNoTipMessage, Assert.Throws<DomainException>(() =>
            order.RegisterPayment(10m, OrderPaymentMethod.CustomerBalance, Now, null, null, null, null, tip: 5m)).Message);
        Assert.Throws<DomainException>(() =>
            order.RegisterPayment(10m, OrderPaymentMethod.CustomerBalance, Now, null, null, null, null, cashReceived: 20m));
        Assert.Empty(order.Payments);
    }

    [Fact]
    public void Refund_ToTheBalance_ShouldBeOfAnOrderWithAPhone()
    {
        var order = CounterSale();
        order.RegisterPayment(100m, OrderPaymentMethod.Cash, Now, null, null, null, null);

        var refund = order.RegisterRefund(30m, OrderPaymentMethod.CustomerBalance, "No le gustó", Now, "dueno", null, null, false, []);

        Assert.Equal((OrderPaymentMethod.CustomerBalance, 30m, 70m), (refund.Method, refund.Amount, order.NetPaidAmount));

        var withoutPhone = CounterSale(customerPhone: "  ");
        withoutPhone.RegisterPayment(100m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(OrderPaymentMethodRules.CustomerBalanceNeedsPhoneMessage, Assert.Throws<DomainException>(() =>
            withoutPhone.RegisterRefund(30m, OrderPaymentMethod.CustomerBalance, "No le gustó", Now, "dueno", null, null, false, [])).Message);
        Assert.Empty(withoutPhone.Refunds);
    }

    private static string Money(decimal amount) => amount.ToString("$#,##0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static Order CounterSale(string? customerPhone = Phone)
    {
        var order = new Order(
            Tenant, null, null, createdByBusiness: true, "ORD-000001", BusinessType.Retail, OrderType.Retail,
            "Ana López", customerPhone: customerPhone, customerNotes: null, tableNumber: null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Producto", 2, 50m);
        order.TransitionTo(OrderStatus.Confirmed, new RetailOrderStateMachine(), "cajero");
        return order;
    }
}
