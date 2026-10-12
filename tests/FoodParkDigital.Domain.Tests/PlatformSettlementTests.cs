using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// A settlement of a delivery platform's deposit: what each order is expected to bring, the period and the deposit it
/// explains, the adjustments, and that it closes only when it balances, never changes after, and is voided, not deleted.
/// </summary>
public class PlatformSettlementTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Sunday = new(2026, 10, 11);

    [Fact]
    public void ExpectedNet_PaidInFull_ShouldBeWhatTheOrderCopied()
    {
        Assert.Equal(561.48m, PlatformSettlementCalculator.ExpectedNet(561.48m, 1000m, 1000m));
        Assert.Equal(-348m, PlatformSettlementCalculator.ExpectedNet(-348m, 1000m, 1000m));
    }

    [Fact]
    public void ExpectedNet_ShouldFollowWhatIsStillPaid()
    {
        // Half given back: half the expected net. All given back, or not collected yet by the business: nothing.
        Assert.Equal(280.74m, PlatformSettlementCalculator.ExpectedNet(561.48m, 500m, 1000m));
        Assert.Equal(0m, PlatformSettlementCalculator.ExpectedNet(561.48m, 0m, 1000m));
        Assert.Equal(0m, PlatformSettlementCalculator.ExpectedNet(-348m, 0m, 1000m));
    }

    [Theory]
    [InlineData(10.01, 5.00)]
    [InlineData(10.03, 5.02)]
    public void ExpectedNet_ShouldRoundToTheEvenCent(decimal expectedSettlement, decimal expected)
    {
        // 5.005 and 5.015: banker's rounding, as every amount of an order of a platform.
        Assert.Equal(expected, PlatformSettlementCalculator.ExpectedNet(expectedSettlement, 50m, 100m));
    }

    [Fact]
    public void ExpectedNet_ShouldRefuseAnOrderWithoutTotal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformSettlementCalculator.ExpectedNet(10m, 0m, 0m));
    }

    [Fact]
    public void Constructor_ShouldOpenItAtTheFirstRevision_WithItsAccounts()
    {
        var first = Order(500m);
        var second = Order(-36.25m);

        var settlement = new PlatformSettlement(
            Tenant,
            OrderChannel.Rappi,
            Deposit(depositedAmount: 420.25m, reference: "  SPEI 123 ", notes: "   "),
            [first, second],
            [new PlatformSettlementAdjustmentInput(PlatformSettlementAdjustmentKind.Promotion, "  Semana del 2x1 ", -43.50m)],
            Today);

        Assert.Equal(PlatformSettlementStatus.Open, settlement.Status);
        Assert.Equal(1, settlement.Revision);
        Assert.Equal((Tenant, OrderChannel.Rappi), (settlement.TenantId, settlement.Channel));
        Assert.Equal((Monday, Sunday, new DateOnly(2026, 10, 13)), (settlement.PeriodFrom, settlement.PeriodTo, settlement.DepositedOn));
        Assert.Equal(("SPEI 123", (string?)null), (settlement.Reference, settlement.Notes));
        Assert.Equal(463.75m, settlement.OrdersTotal);
        Assert.Equal(-43.50m, settlement.AdjustmentsTotal);
        Assert.Equal(420.25m, settlement.ExpectedDeposit);
        Assert.Equal(0m, settlement.Difference);
        Assert.All(settlement.Orders, line => Assert.True(line.IsActive));
        Assert.All(settlement.Orders, line => Assert.Equal((Tenant, settlement.Id), (line.TenantId, line.SettlementId)));
        Assert.Equal([first.OrderId, second.OrderId], settlement.Orders.Select(line => line.OrderId));
        var adjustment = Assert.Single(settlement.Adjustments);
        Assert.Equal((Tenant, settlement.Id, "Semana del 2x1"), (adjustment.TenantId, adjustment.SettlementId, adjustment.Description));
    }

    [Theory]
    [InlineData(OrderChannel.Counter)]
    [InlineData(OrderChannel.WhatsApp)]
    [InlineData(OrderChannel.Online)]
    [InlineData((OrderChannel)6)]
    public void Constructor_ShouldRefuseAChannelThatIsNotADeliveryPlatform(OrderChannel channel)
    {
        var error = Assert.Throws<DomainException>(() => new PlatformSettlement(Tenant, channel, Deposit(), [], [], Today));

        Assert.Equal(DeliveryPlatformSetting.NotADeliveryPlatformMessage, error.Message);
    }

    [Fact]
    public void Constructor_ShouldRequireTheBusiness()
    {
        var error = Assert.Throws<DomainException>(
            () => new PlatformSettlement(Guid.Empty, OrderChannel.Rappi, Deposit(), [], [], Today));

        Assert.Equal(DeliveryPlatformSetting.TenantRequiredMessage, error.Message);
    }

    [Fact]
    public void Period_ShouldGoForwardAndLastUpToAMonth_WithoutReachingTheFuture()
    {
        Refused(PlatformSettlement.PeriodOrderMessage, Deposit(from: Sunday, to: Monday, depositedOn: Today));
        Refused(PlatformSettlement.PeriodTooLongMessage, Deposit(from: new DateOnly(2026, 9, 10), to: Sunday));
        Refused(PlatformSettlement.PeriodInTheFutureMessage, Deposit(to: Today.AddDays(1), depositedOn: Today));

        // 31 days, and one single day ending today, are periods.
        Assert.Equal(PlatformSettlementStatus.Open, Open(Deposit(from: new DateOnly(2026, 9, 11), to: Sunday)).Status);
        Assert.Equal(PlatformSettlementStatus.Open, Open(Deposit(from: Today, to: Today, depositedOn: Today)).Status);
    }

    [Fact]
    public void DepositDate_ShouldBeFromThePeriodOn_WithoutReachingTheFuture()
    {
        Refused(PlatformSettlement.DepositBeforePeriodMessage, Deposit(depositedOn: Monday.AddDays(-1)));
        Refused(PlatformSettlement.DepositInTheFutureMessage, Deposit(depositedOn: Today.AddDays(1)));

        Assert.Equal(Monday, Open(Deposit(depositedOn: Monday)).DepositedOn);
    }

    [Theory]
    [InlineData(10_000_000.01)]
    [InlineData(-10_000_000.01)]
    [InlineData(12.345)]
    public void DepositedAmount_ShouldBeCentsUpToTenMillionEitherWay(decimal depositedAmount)
    {
        Refused(PlatformSettlement.DepositedAmountMessage, Deposit(depositedAmount: depositedAmount));
    }

    [Theory]
    [InlineData(-1234.50)]
    [InlineData(0)]
    [InlineData(10_000_000)]
    public void DepositedAmount_MayBeNegativeOrZero_WhenThePlatformChargedOrPaidNothing(decimal depositedAmount)
    {
        Assert.Equal(depositedAmount, Open(Deposit(depositedAmount: depositedAmount)).DepositedAmount);
    }

    [Fact]
    public void ReferenceAndNotes_ShouldKeepTheirLimits()
    {
        Refused(PlatformSettlement.ReferenceTooLongMessage, Deposit(reference: new string('R', 61)));
        Refused(PlatformSettlement.NotesTooLongMessage, Deposit(notes: new string('n', 501)));

        var settlement = Open(Deposit(reference: new string('R', 60), notes: new string('n', 500)));
        Assert.Equal((60, 500), (settlement.Reference!.Length, settlement.Notes!.Length));
    }

    [Fact]
    public void Orders_ShouldBeOfItsPlatform_RegisteredUpToTheEndOfThePeriod_AndEachOnce()
    {
        var order = Order(100m);

        RefusedOrders(PlatformSettlement.RepeatedOrderMessage, [order, order with { ExpectedNet = 50m }]);
        RefusedOrders(PlatformSettlement.OrderOfAnotherPlatformMessage, [Order(100m) with { Channel = OrderChannel.UberEats }]);
        RefusedOrders(PlatformSettlement.OrderAfterThePeriodMessage, [Order(100m) with { RegisteredOn = Sunday.AddDays(1) }]);
        RefusedOrders(PlatformSettlement.TooManyOrdersMessage, Enumerable.Range(0, 2_001).Select(_ => Order(1m)).ToList());

        // A pending order of before the period still enters: the statement may bring it late.
        var settlement = Open(Deposit(depositedAmount: 101m), [order, Order(1m) with { RegisteredOn = Monday.AddDays(-20) }]);
        Assert.Equal(2, settlement.Orders.Count);
        Assert.Equal(2_000, Open(Deposit(depositedAmount: 0m), Enumerable.Range(0, 2_000).Select(_ => Order(0m)).ToList()).Orders.Count);
    }

    [Fact]
    public void Adjustments_ShouldHaveAReasonAndAnAmount_AndOtherADescription()
    {
        RefusedAdjustment(PlatformSettlement.AdjustmentAmountMessage, new(PlatformSettlementAdjustmentKind.Fee, null, 0m));
        RefusedAdjustment(PlatformSettlement.AdjustmentAmountMessage, new(PlatformSettlementAdjustmentKind.Fee, null, 1.005m));
        RefusedAdjustment(PlatformSettlement.AdjustmentAmountMessage, new(PlatformSettlementAdjustmentKind.Claim, null, -1_000_000.01m));
        RefusedAdjustment(PlatformSettlement.AdjustmentKindMessage, new((PlatformSettlementAdjustmentKind)7, "?", 10m));
        RefusedAdjustment(PlatformSettlement.AdjustmentDescriptionRequiredMessage, new(PlatformSettlementAdjustmentKind.Other, "  ", 10m));
        RefusedAdjustment(
            PlatformSettlement.AdjustmentDescriptionTooLongMessage,
            new(PlatformSettlementAdjustmentKind.Claim, new string('d', 201), -10m));

        var tooMany = Enumerable.Range(0, 101)
            .Select(_ => new PlatformSettlementAdjustmentInput(PlatformSettlementAdjustmentKind.Fee, null, -1m))
            .ToList();
        var error = Assert.Throws<DomainException>(
            () => new PlatformSettlement(Tenant, OrderChannel.Rappi, Deposit(), [], tooMany, Today));
        Assert.Equal(PlatformSettlement.TooManyAdjustmentsMessage, error.Message);

        // A reason other than «Otro» goes without description; amounts go up to a million either way.
        var settlement = new PlatformSettlement(Tenant, OrderChannel.Rappi, Deposit(depositedAmount: 0m), [],
        [
            new(PlatformSettlementAdjustmentKind.PreviousBalance, null, -1_000_000m),
            new(PlatformSettlementAdjustmentKind.Other, "Bono de bienvenida", 1_000_000m),
        ], Today);
        Assert.Equal(0m, settlement.AdjustmentsTotal);
        Assert.Null(settlement.Adjustments.First().Description);
    }

    [Fact]
    public void Update_ShouldReplaceItWhole_KeepingTheRowsOfTheOrdersThatStay()
    {
        var stays = Order(100m);
        var leaves = Order(50m);
        var settlement = Open(Deposit(depositedAmount: 150m), [stays, leaves]);
        var keptRow = settlement.Orders.Single(line => line.OrderId == stays.OrderId).Id;
        var arrives = Order(25m);

        settlement.Update(
            Deposit(depositedAmount: 110m, reference: "Depósito 2"),
            [stays with { ExpectedNet = 90m }, arrives],
            [new(PlatformSettlementAdjustmentKind.Fee, "Tableta", -5m)],
            Today);

        Assert.Equal(2, settlement.Revision);
        Assert.Equal([stays.OrderId, arrives.OrderId], settlement.Orders.Select(line => line.OrderId));
        Assert.Equal(keptRow, settlement.Orders.First().Id);
        Assert.Equal((115m, -5m, 110m, 0m), (settlement.OrdersTotal, settlement.AdjustmentsTotal, settlement.ExpectedDeposit, settlement.Difference));
        Assert.Equal((110m, "Depósito 2"), (settlement.DepositedAmount, settlement.Reference));
    }

    [Fact]
    public void Update_ThatIsRefused_ShouldLeaveItAsItWas()
    {
        var order = Order(100m);
        var settlement = Open(Deposit(depositedAmount: 100m), [order]);

        Assert.Throws<DomainException>(() => settlement.Update(
            Deposit(depositedAmount: 1m),
            [Order(5m)],
            [new(PlatformSettlementAdjustmentKind.Other, null, 3m)],
            Today));

        Assert.Equal(1, settlement.Revision);
        Assert.Equal((100m, 100m), (settlement.DepositedAmount, settlement.OrdersTotal));
        Assert.Equal(order.OrderId, Assert.Single(settlement.Orders).OrderId);
        Assert.Empty(settlement.Adjustments);
    }

    [Fact]
    public void Recount_ShouldTakeTheNewExpectedNets_WithANewRevision_OnlyWhenOneChanged()
    {
        var first = Order(100m);
        var second = Order(80m);
        var settlement = Open(Deposit(depositedAmount: 180m), [first, second]);

        Assert.False(settlement.Recount(new Dictionary<Guid, decimal> { [first.OrderId] = 100m, [second.OrderId] = 80m }));
        Assert.Equal(1, settlement.Revision);

        // The second order was given back by half meanwhile.
        Assert.True(settlement.Recount(new Dictionary<Guid, decimal> { [first.OrderId] = 100m, [second.OrderId] = 40m }));
        Assert.Equal(2, settlement.Revision);
        Assert.Equal((140m, 40m), (settlement.OrdersTotal, settlement.Difference));
        Assert.Throws<ArgumentException>(() => settlement.Recount(new Dictionary<Guid, decimal> { [first.OrderId] = 100m }));
    }

    [Fact]
    public void Close_ShouldNeedEveryPesoExplained()
    {
        var settlement = Open(Deposit(depositedAmount: 420m), [Order(500m)]);

        var error = Assert.Throws<DomainException>(() => settlement.Close(DateTimeOffset.UtcNow));
        Assert.Equal(PlatformSettlement.DoesNotBalanceMessage, error.Message);
        Assert.Equal(PlatformSettlementStatus.Open, settlement.Status);

        settlement.Update(Deposit(depositedAmount: 420m), [Order(500m)], [new(PlatformSettlementAdjustmentKind.Claim, null, -80m)], Today);
        var closedAt = new DateTimeOffset(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);
        settlement.Close(closedAt);

        Assert.Equal(PlatformSettlementStatus.Closed, settlement.Status);
        Assert.Equal(closedAt, settlement.ClosedAt);
        Assert.Equal(3, settlement.Revision);
    }

    [Fact]
    public void Close_ShouldNeedAnOrderOrAnAdjustment_AndMayHaveOnlyAdjustments()
    {
        var empty = Open(Deposit(depositedAmount: 0m));
        var error = Assert.Throws<DomainException>(() => empty.Close(DateTimeOffset.UtcNow));
        Assert.Equal(PlatformSettlement.NothingToCloseMessage, error.Message);

        // DiDi charged the balance of another week, and nothing else.
        var onlyBalance = new PlatformSettlement(Tenant, OrderChannel.DidiFood, Deposit(depositedAmount: -120m), [],
            [new(PlatformSettlementAdjustmentKind.PreviousBalance, null, -120m)], Today);
        onlyBalance.Close(DateTimeOffset.UtcNow);
        Assert.Equal(PlatformSettlementStatus.Closed, onlyBalance.Status);
    }

    [Fact]
    public void Closed_ShouldNeverChange_ButMayBeVoided()
    {
        var order = Order(100m);
        var settlement = Open(Deposit(depositedAmount: 100m), [order]);
        settlement.Close(DateTimeOffset.UtcNow);

        Assert.Equal(PlatformSettlement.ClosedMessage, Assert.Throws<DomainException>(
            () => settlement.Update(Deposit(depositedAmount: 100m), [order], [], Today)).Message);
        Assert.Equal(PlatformSettlement.ClosedMessage, Assert.Throws<DomainException>(() => settlement.Close(DateTimeOffset.UtcNow)).Message);
        Assert.Equal(PlatformSettlement.ClosedMessage, Assert.Throws<DomainException>(
            () => settlement.Recount(new Dictionary<Guid, decimal> { [order.OrderId] = 1m })).Message);

        settlement.Void("Se registró con el depósito de otra semana", DateTimeOffset.UtcNow);
        Assert.Equal(PlatformSettlementStatus.Voided, settlement.Status);
    }

    [Fact]
    public void Void_ShouldKeepItAsHistory_AndFreeItsOrders()
    {
        var settlement = Open(Deposit(depositedAmount: 150m), [Order(100m), Order(50m)]);
        var voidedAt = new DateTimeOffset(2026, 10, 14, 19, 30, 0, TimeSpan.Zero);

        settlement.Void("  Capturé el monto de Uber Eats  ", voidedAt);

        Assert.Equal(PlatformSettlementStatus.Voided, settlement.Status);
        Assert.Equal((voidedAt, "Capturé el monto de Uber Eats"), (settlement.VoidedAt, settlement.VoidReason));
        Assert.Equal(2, settlement.Revision);
        Assert.Equal(2, settlement.Orders.Count);
        Assert.All(settlement.Orders, line => Assert.False(line.IsActive));
        Assert.Equal(150m, settlement.OrdersTotal);
    }

    [Fact]
    public void Void_ShouldNeedAReason_AndHappenOnce()
    {
        var settlement = Open(Deposit(depositedAmount: 100m), [Order(100m)]);

        Assert.Equal(PlatformSettlement.VoidReasonRequiredMessage, Assert.Throws<DomainException>(() => settlement.Void("   ", DateTimeOffset.UtcNow)).Message);
        Assert.Equal(PlatformSettlement.VoidReasonRequiredMessage, Assert.Throws<DomainException>(() => settlement.Void(null, DateTimeOffset.UtcNow)).Message);
        Assert.Equal(PlatformSettlement.VoidReasonTooLongMessage, Assert.Throws<DomainException>(
            () => settlement.Void(new string('m', 501), DateTimeOffset.UtcNow)).Message);
        Assert.Equal(PlatformSettlementStatus.Open, settlement.Status);

        settlement.Void(new string('m', 500), DateTimeOffset.UtcNow);

        Assert.Equal(PlatformSettlement.VoidedMessage, Assert.Throws<DomainException>(() => settlement.Void("Otra vez", DateTimeOffset.UtcNow)).Message);
        Assert.Equal(PlatformSettlement.VoidedMessage, Assert.Throws<DomainException>(
            () => settlement.Update(Deposit(depositedAmount: 0m), [], [], Today)).Message);
        Assert.Equal(PlatformSettlement.VoidedMessage, Assert.Throws<DomainException>(() => settlement.Close(DateTimeOffset.UtcNow)).Message);
    }

    [Fact]
    public void EnumValues_ShouldKeepTheirStoredNumbers()
    {
        Assert.Equal([0, 1, 99], Enum.GetValues<PlatformSettlementStatus>().Select(status => (int)status));
        Assert.Equal([1, 2, 3, 4, 5, 99], Enum.GetValues<PlatformSettlementAdjustmentKind>().Select(kind => (int)kind));
    }

    private static PlatformSettlementOrderInput Order(decimal expectedNet)
        => new(Guid.NewGuid(), OrderChannel.Rappi, Monday.AddDays(2), expectedNet);

    private static PlatformSettlementDeposit Deposit(
        DateOnly? from = null,
        DateOnly? to = null,
        DateOnly? depositedOn = null,
        decimal depositedAmount = 0m,
        string? reference = null,
        string? notes = null)
        => new(from ?? Monday, to ?? Sunday, depositedOn ?? new DateOnly(2026, 10, 13), depositedAmount, reference, notes);

    private static PlatformSettlement Open(PlatformSettlementDeposit deposit, IReadOnlyList<PlatformSettlementOrderInput>? orders = null)
        => new(Tenant, OrderChannel.Rappi, deposit, orders ?? [], [], Today);

    private static void Refused(string message, PlatformSettlementDeposit deposit)
    {
        var error = Assert.Throws<DomainException>(() => Open(deposit));
        Assert.Equal(message, error.Message);
    }

    private static void RefusedOrders(string message, IReadOnlyList<PlatformSettlementOrderInput> orders)
    {
        var error = Assert.Throws<DomainException>(() => Open(Deposit(), orders));
        Assert.Equal(message, error.Message);
    }

    private static void RefusedAdjustment(string message, PlatformSettlementAdjustmentInput adjustment)
    {
        var error = Assert.Throws<DomainException>(
            () => new PlatformSettlement(Tenant, OrderChannel.Rappi, Deposit(), [], [adjustment], Today));
        Assert.Equal(message, error.Message);
    }
}
