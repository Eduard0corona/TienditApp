using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Billing.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

public class SubscriptionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Trial_ShouldGiveWayToGraceDaysAndThenToSuspension()
    {
        var subscription = new TenantSubscription(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14, Start);

        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Equal(SubscriptionStatus.Trial, subscription.Evaluate(Start.AddDays(13)));
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Evaluate(Start.AddDays(14)));
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Evaluate(Start.AddDays(14 + TenantSubscription.GraceDays).AddMinutes(-1)));
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Evaluate(Start.AddDays(14 + TenantSubscription.GraceDays)));

        Assert.True(subscription.Refresh(Start.AddDays(30)));
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        Assert.False(subscription.HasAccess);
        Assert.NotNull(subscription.StatusReason);
    }

    [Fact]
    public void WithoutTrial_TheFirstPaymentShouldBeDueAtOnce()
    {
        var subscription = new TenantSubscription(Guid.NewGuid(), Guid.NewGuid(), trialDays: 0, Start);

        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(Start.AddDays(TenantSubscription.GraceDays), subscription.SuspendsAt);
    }

    [Fact]
    public void PaymentDuringTheTrial_ShouldStartWhenTheTrialEnds()
    {
        var subscription = new TenantSubscription(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14, Start);

        var payment = subscription.RegisterPayment(299m, "MXN", 3, SubscriptionPaymentMethod.Transfer, "SPEI 123", Start.AddDays(5), "admin");

        Assert.Equal(Start.AddDays(14), payment.PeriodStart);
        Assert.Equal(Start.AddDays(14).AddMonths(3), payment.PeriodEnd);
        Assert.Equal(payment.PeriodEnd, subscription.PaidThrough);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void PaymentAfterTheCoverageEnded_ShouldStartOnTheDayItIsPaid_AndLiftTheSuspension()
    {
        var subscription = new TenantSubscription(Guid.NewGuid(), Guid.NewGuid(), trialDays: 7, Start);
        subscription.Refresh(Start.AddDays(40));
        var paidAt = Start.AddDays(40);

        var payment = subscription.RegisterPayment(150m, "MXN", 1, SubscriptionPaymentMethod.Card, null, paidAt, "admin");

        Assert.Equal(paidAt, payment.PeriodStart);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(subscription.HasAccess);
        Assert.Null(subscription.StatusReason);
    }

    [Fact]
    public void ConsecutivePayments_ShouldChainTheirPeriods()
    {
        var subscription = new TenantSubscription(Guid.NewGuid(), Guid.NewGuid(), trialDays: 0, Start);

        var first = subscription.RegisterPayment(100m, "MXN", 1, SubscriptionPaymentMethod.Cash, null, Start, "admin");
        var second = subscription.RegisterPayment(100m, "MXN", 2, SubscriptionPaymentMethod.Cash, null, Start.AddDays(10), "admin");

        Assert.Equal(first.PeriodEnd, second.PeriodStart);
        Assert.Equal(Start.AddMonths(3), subscription.PaidThrough);
        Assert.Equal(2, subscription.Payments.Count);
    }

    [Fact]
    public void SuspensionByThePlatform_ShouldStayThroughPayments_UntilItIsLifted()
    {
        var subscription = new TenantSubscription(Guid.NewGuid(), Guid.NewGuid(), trialDays: 30, Start);

        subscription.Suspend("Uso indebido", Start.AddDays(1));
        subscription.RegisterPayment(100m, "MXN", 1, SubscriptionPaymentMethod.Transfer, null, Start.AddDays(2), "admin");
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        Assert.Equal("Uso indebido", subscription.StatusReason);

        subscription.Reactivate(Start.AddDays(3));
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.False(subscription.SuspendedByPlatform);
    }

    [Fact]
    public void CancelledSubscription_ShouldRefusePaymentsUntilItIsReactivated()
    {
        var subscription = new TenantSubscription(Guid.NewGuid(), Guid.NewGuid(), trialDays: 30, Start);
        subscription.Cancel("Cerró el negocio", Start.AddDays(1));

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Evaluate(Start.AddDays(2)));
        Assert.Throws<DomainException>(() =>
            subscription.RegisterPayment(100m, "MXN", 1, SubscriptionPaymentMethod.Cash, null, Start.AddDays(2), "admin"));

        subscription.Reactivate(Start.AddDays(2));
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Null(subscription.CancelledAt);
    }

    [Fact]
    public void ExtendingTheTrial_ShouldGiveFreeDaysFromTodayWhenItAlreadyEnded()
    {
        var subscription = new TenantSubscription(Guid.NewGuid(), Guid.NewGuid(), trialDays: 7, Start);
        subscription.Refresh(Start.AddDays(20));

        subscription.ExtendTrial(10, Start.AddDays(20));

        Assert.Equal(Start.AddDays(30), subscription.TrialEndsAt);
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
    }

    [Fact]
    public void Plan_ShouldKeepAWayToSell_WhenItTrimsTheModulesOfABusiness()
    {
        var plan = new Plan("basico", "Básico", null, 199m, TenantCapability.Retail | TenantCapability.Inventory, 1, 3, 14, 0);

        Assert.Equal("BASICO", plan.Code);
        Assert.Equal(TenantCapability.Retail | TenantCapability.Inventory,
            plan.ClampModules(TenantCapability.Retail | TenantCapability.Inventory | TenantCapability.Appointments));
        // A restaurant on a plan without food keeps selling with the plan's kind of sale.
        Assert.Equal(TenantCapability.Retail | TenantCapability.Inventory,
            plan.ClampModules(TenantCapability.Food | TenantCapability.Kitchen | TenantCapability.Inventory));
        Assert.Equal(TenantCapability.Kitchen, plan.ModulesOutsidePlan(TenantCapability.Retail | TenantCapability.Kitchen));
    }

    [Theory]
    [InlineData(-1, (int)TenantCapability.Retail)]
    [InlineData(10.005, (int)TenantCapability.Retail)]
    [InlineData(100, (int)TenantCapability.Inventory)]
    public void Plan_ShouldRefuseNegativePricesAndPlansWithoutAKindOfSale(double price, int modules)
    {
        Assert.Throws<DomainException>(() =>
            new Plan("X", "Plan", null, (decimal)price, (TenantCapability)modules, 1, 1, 0, 0));
    }

    [Fact]
    public void DefaultPlan_ShouldNotBeDeactivated()
    {
        var plan = new Plan("PRO", "Pro", null, 499m, TenantCapability.Food, 3, 10, 14, 1);
        plan.MarkAsDefaultForNewBusinesses();

        Assert.Throws<DomainException>(plan.Deactivate);
        plan.UnmarkAsDefaultForNewBusinesses();
        plan.Deactivate();
        Assert.Throws<DomainException>(plan.MarkAsDefaultForNewBusinesses);
    }
}
