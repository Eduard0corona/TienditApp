using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Condonar y plazo del fiado (Fase 26B, ADR 0034): the business forgives part or all of what a customer owes for an
/// order with a record apart that lowers the balance, never more than is owed; and how many days its customers have to pay.
/// </summary>
public class CustomerWriteOffTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WriteOff_ShouldLowerTheBalance_KeepingTheSaleAndItsPayments_AndARecordApart()
    {
        var order = SoldOnCredit(paidNow: 30m);

        var writeOff = order.WriteOff(20m, OrderWriteOffReason.Courtesy, "  Cliente de años  ", Now, "dueno@cafe.mx");

        Assert.Equal((100m, 30m, 20m, 50m), (order.Total, order.PaidAmount, order.WrittenOffAmount, order.Balance));
        Assert.Equal(30m, Assert.Single(order.Payments).Amount);
        Assert.Same(writeOff, Assert.Single(order.WriteOffs));
        Assert.Equal(
            (Tenant, order.Id, 20m, OrderWriteOffReason.Courtesy, "Cliente de años", Now, "dueno@cafe.mx"),
            (writeOff.TenantId, writeOff.OrderId, writeOff.Amount, writeOff.Reason, writeOff.Notes, writeOff.WrittenOffAt, writeOff.WrittenOffBy));
        Assert.True(order.SoldOnCredit);
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void Payment_ShouldCoverWhatIsLeft_AfterAWriteOff_NeverMore()
    {
        var order = SoldOnCredit();
        order.WriteOff(40m, OrderWriteOffReason.WontBeCollected, null, Now, "dueno");

        var above = Assert.Throws<DomainException>(() =>
            order.RegisterPayment(60.01m, OrderPaymentMethod.Cash, Now, null, null, null, null));
        Assert.Equal(Order.PaymentAboveBalanceMessage, above.Message);

        order.RegisterPayment(60m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(0m, order.Balance);
    }

    [Fact]
    public void WriteOff_ShouldForgiveAllOfIt_InSeveralParts_ButNeverMoreThanIsOwed()
    {
        var order = SoldOnCredit();
        order.WriteOff(70m, OrderWriteOffReason.RecordingError, null, Now, "dueno");

        var above = Assert.Throws<DomainException>(() => order.WriteOff(30.01m, OrderWriteOffReason.Courtesy, null, Now, "dueno"));
        Assert.Equal(Order.WriteOffAboveBalanceMessage(30m), above.Message);
        Assert.Equal("No se puede condonar más de lo que debe el pedido ($30.00).", above.Message);

        order.WriteOff(30m, OrderWriteOffReason.Courtesy, null, Now, "dueno");
        Assert.Equal((100m, 0m, 2), (order.WrittenOffAmount, order.Balance, order.WriteOffs.Count));

        // Nothing is owed anymore: there is nothing left to forgive.
        Assert.Equal(Order.WriteOffOnlyOwedMessage,
            Assert.Throws<DomainException>(() => order.WriteOff(1m, OrderWriteOffReason.Courtesy, null, Now, "dueno")).Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public void WriteOff_ShouldBeAboveZero_InCentavos(double amount)
    {
        var order = SoldOnCredit();

        var refused = Assert.Throws<DomainException>(() => order.WriteOff((decimal)amount, OrderWriteOffReason.Courtesy, null, Now, "dueno"));

        Assert.Equal(OrderWriteOff.AmountMessage, refused.Message);
        Assert.Equal((0m, 100m), (order.WrittenOffAmount, order.Balance));
        Assert.Empty(order.WriteOffs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("no")]
    public void WriteOffForAnotherReason_ShouldSayWhy(string? notes)
    {
        var order = SoldOnCredit();

        var refused = Assert.Throws<DomainException>(() => order.WriteOff(10m, OrderWriteOffReason.Other, notes, Now, "dueno"));

        Assert.Equal(OrderWriteOff.NotesNeededMessage, refused.Message);
        Assert.Equal(100m, order.Balance);
        Assert.Equal("Pagó con trabajo", order.WriteOff(10m, OrderWriteOffReason.Other, "Pagó con trabajo", Now, "dueno").Notes);
    }

    [Fact]
    public void WriteOff_ShouldRefuse_AReasonThatIsNotOne()
    {
        var order = SoldOnCredit();

        Assert.Throws<DomainException>(() => order.WriteOff(10m, (OrderWriteOffReason)7, null, Now, "dueno"));
        Assert.Empty(order.WriteOffs);
    }

    [Fact]
    public void WriteOff_ShouldBeOfWhatTheCustomerOwes_SoldOnCreditOrHandedOverUnpaid()
    {
        // Delivered without being charged and not on credit: the customer owes it too.
        var delivered = CounterSale();
        delivered.TransitionTo(OrderStatus.Delivered, new RetailOrderStateMachine(), "cajero");
        delivered.WriteOff(100m, OrderWriteOffReason.WontBeCollected, null, Now, "dueno");
        Assert.Equal(0m, delivered.Balance);

        // Still in progress: nobody owes it yet, it is charged when handed over.
        var open = CounterSale();
        Assert.False(open.IsOwedByCustomer);
        Assert.Equal(Order.WriteOffOnlyOwedMessage,
            Assert.Throws<DomainException>(() => open.WriteOff(10m, OrderWriteOffReason.Courtesy, null, Now, "dueno")).Message);

        var cancelled = CounterSale();
        cancelled.Cancel(new RetailOrderStateMachine(), "cajero", "Se equivocó");
        Assert.Equal("La orden está cancelada: no hay nada que condonar.",
            Assert.Throws<DomainException>(() => cancelled.WriteOff(10m, OrderWriteOffReason.Courtesy, null, Now, "dueno")).Message);
    }

    [Fact]
    public void WriteOff_ShouldRefuse_WhatACourierCollects()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = new Order(
            Tenant, null, Guid.NewGuid(), createdByBusiness: true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail,
            "Ana", customerPhone: "5512345678", customerNotes: null, tableNumber: null, pricesIncludeTax: true,
            channel: OrderChannel.WhatsApp);
        order.AddItem(Guid.NewGuid(), "Comida corrida", 1, 240m, vatRate: VatRate.General);
        order.ArrangeDelivery(
            DeliveryAddress.From("72000", "Av. Juárez 123, Col. Centro", null),
            new DeliveryZone(Tenant, new DeliveryZoneFields("Centro", 35m, 0m, null, ["72000"], true)),
            VatRate.General);
        order.TransitionTo(OrderStatus.Confirmed, stateMachine, "caja");
        order.Dispatch(CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ1", "https://paquetenvia.mx/r/PQ1"), collectsCash: true, paysWith: null, Now, "caja");
        order.TransitionTo(OrderStatus.Delivered, stateMachine, "caja");

        Assert.True(order.AwaitsCourierCash);
        Assert.False(order.IsOwedByCustomer);
        Assert.Equal(Order.WriteOffCourierCashMessage,
            Assert.Throws<DomainException>(() => order.WriteOff(10m, OrderWriteOffReason.Courtesy, null, Now, "dueno")).Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(120)]
    [InlineData(null)]
    public void CreditTerm_ShouldGoFromOneTo120Days_OrNone(int? days)
    {
        var settings = new TenantSettings(Tenant);
        settings.SetCreditTermDays(30);

        settings.SetCreditTermDays(days);

        Assert.Equal(days, settings.CreditTermDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void CreditTerm_ShouldRefuse_DaysOutOfRange(int days)
    {
        var settings = new TenantSettings(Tenant);

        var refused = Assert.Throws<DomainException>(() => settings.SetCreditTermDays(days));

        Assert.Equal(TenantSettings.CreditTermRangeMessage, refused.Message);
        Assert.Null(settings.CreditTermDays);
    }

    /// <summary>A $100 counter sale of Ana's, sold on credit (part paid now, if any) and closed.</summary>
    private static Order SoldOnCredit(decimal paidNow = 0m)
    {
        var order = CounterSale();
        if (paidNow > 0)
            order.RegisterPayment(paidNow, OrderPaymentMethod.Cash, Now, null, null, null, null);
        order.SellOnCredit();
        Assert.True(order.CloseIfPaidCounterSale(new RetailOrderStateMachine(), waitsForKitchen: false, "cajero"));
        return order;
    }

    private static Order CounterSale()
    {
        var order = new Order(
            Tenant, null, null, createdByBusiness: true, "ORD-000001", BusinessType.Retail, OrderType.Retail,
            "Ana López", customerPhone: "+525512345678", customerNotes: null, tableNumber: null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Producto", 2, 50m);
        order.TransitionTo(OrderStatus.Confirmed, new RetailOrderStateMachine(), "cajero");
        return order;
    }
}
