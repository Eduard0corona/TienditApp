using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Tests;

public class CashSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CashTransactionType.Expense)]
    [InlineData(CashTransactionType.Withdrawal)]
    [InlineData(CashTransactionType.Refund)]
    public void OutflowTypes_ShouldReduceExpectedBalance_EvenWhenAmountIsPositive(CashTransactionType type)
    {
        var session = NewSession(openingAmount: 1000m);

        var transaction = session.RegisterTransaction(type, 200m, Now);

        Assert.Equal(-200m, transaction.Amount);
        Assert.Equal(200m, session.Outflows);
        Assert.Equal(800m, session.ExpectedBalance);
    }

    [Fact]
    public void Deposit_ShouldIncreaseExpectedBalance()
    {
        var session = NewSession(openingAmount: 100m);

        session.RegisterTransaction(CashTransactionType.Deposit, 50m, Now);

        Assert.Equal(150m, session.ExpectedBalance);
    }

    [Theory]
    [InlineData(CashTransactionType.Deposit, -10)]
    [InlineData(CashTransactionType.SalePayment, -10)]
    [InlineData(CashTransactionType.Expense, 0)]
    public void InvalidAmounts_ShouldBeRejected(CashTransactionType type, decimal amount)
    {
        var session = NewSession(openingAmount: 0m);

        Assert.Throws<DomainException>(() => session.RegisterTransaction(type, amount, Now));
    }

    [Fact]
    public void NonCashPayments_ShouldNotCountAsCashInTheDrawer()
    {
        var session = NewSession(openingAmount: 500m);

        session.RegisterTransaction(CashTransactionType.SalePayment, 300m, Now, OrderPaymentMethod.Cash);
        session.RegisterTransaction(CashTransactionType.SalePayment, 700m, Now, OrderPaymentMethod.Card);

        Assert.Equal(300m, session.Inflows);
        Assert.Equal(700m, session.NonCashInflows);
        Assert.Equal(800m, session.ExpectedBalance);
    }

    [Fact]
    public void Close_ShouldRejectNegativeCountedAmount()
    {
        var session = NewSession(openingAmount: 0m);

        Assert.Throws<DomainException>(() => session.Close(Now, "cajero", -1m));
        Assert.Equal(CashSessionStatus.Open, session.Status);
    }

    [Fact]
    public void MovementsAndClose_ShouldIncrementRevision()
    {
        var session = NewSession(openingAmount: 0m);

        session.RegisterTransaction(CashTransactionType.Deposit, 10m, Now);
        session.Close(Now, "cajero", 10m);

        Assert.Equal(2, session.Revision);
        Assert.Throws<DomainException>(() => session.RegisterTransaction(CashTransactionType.Deposit, 10m, Now));
    }

    [Theory]
    [InlineData(CashTransactionType.Deposit, true)]
    [InlineData(CashTransactionType.Expense, true)]
    [InlineData(CashTransactionType.Withdrawal, true)]
    [InlineData(CashTransactionType.Refund, true)]
    [InlineData(CashTransactionType.SalePayment, false)]
    [InlineData(CashTransactionType.OpeningAdjustment, false)]
    [InlineData(CashTransactionType.ClosingAdjustment, false)]
    public void IsManualTransactionType_ShouldOnlyAllowDrawerMovements(CashTransactionType type, bool expected)
    {
        Assert.Equal(expected, CashSession.IsManualTransactionType(type));
    }

    [Theory]
    [InlineData(OrderPaymentMethod.Cash, true)]
    [InlineData(OrderPaymentMethod.Card, true)]
    [InlineData(OrderPaymentMethod.Transfer, true)]
    [InlineData(OrderPaymentMethod.DigitalWallet, true)]
    [InlineData(OrderPaymentMethod.Other, true)]
    [InlineData(OrderPaymentMethod.Platform, false)]
    [InlineData(OrderPaymentMethod.Online, false)]
    public void OnlyWhatAPlatformOrTheGatewayCharged_ShouldStayOutOfTheDrawers(OrderPaymentMethod method, bool entersADrawer)
    {
        Assert.Equal(entersADrawer, OrderPaymentMethodRules.EntersADrawer(method));
        Assert.Equal(!entersADrawer, OrderPaymentMethodRules.RecordedOnlyOnCreation(method));
        // Added at the end of the enum: stored payments keep their method.
        Assert.Equal(5, (int)OrderPaymentMethod.Platform);
        Assert.Equal(6, (int)OrderPaymentMethod.Online);
    }

    [Fact]
    public void WhatTheCustomerPaidOnline_ShouldNeverEnterADrawer()
    {
        var session = NewSession(openingAmount: 100m);

        var error = Assert.Throws<DomainException>(
            () => session.RegisterTransaction(CashTransactionType.SalePayment, 350m, Now, OrderPaymentMethod.Online));

        Assert.Equal(OrderPaymentMethodRules.OnlineNeverEntersADrawerMessage, error.Message);
        Assert.Empty(session.Transactions);
        Assert.Equal(100m, session.ExpectedBalance);
    }

    [Theory]
    [InlineData(CashTransactionType.SalePayment)]
    [InlineData(CashTransactionType.Tip)]
    [InlineData(CashTransactionType.Refund)]
    [InlineData(CashTransactionType.Deposit)]
    public void WhatAPlatformCharged_ShouldNeverEnterADrawer(CashTransactionType type)
    {
        var session = NewSession(openingAmount: 100m);

        var error = Assert.Throws<DomainException>(
            () => session.RegisterTransaction(type, 350m, Now, OrderPaymentMethod.Platform));

        Assert.Equal(OrderPaymentMethodRules.NeverEntersADrawerMessage, error.Message);
        Assert.Empty(session.Transactions);
        Assert.Equal(0, session.Revision);
        Assert.Equal(100m, session.ExpectedBalance);
    }

    [Fact]
    public void EveryOtherMethod_ShouldStillEnterTheDrawer()
    {
        var session = NewSession(openingAmount: 0m);

        foreach (var method in Enum.GetValues<OrderPaymentMethod>().Where(OrderPaymentMethodRules.EntersADrawer))
            session.RegisterTransaction(CashTransactionType.SalePayment, 10m, Now, method);

        Assert.Equal(Enum.GetValues<OrderPaymentMethod>().Length - 2, session.Transactions.Count);
    }

    private static CashSession NewSession(decimal openingAmount) =>
        new(Guid.NewGuid(), Now, "cajero", openingAmount);
}
