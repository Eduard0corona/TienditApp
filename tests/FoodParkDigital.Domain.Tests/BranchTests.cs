using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Reporting.Entities;

namespace FoodParkDigital.Domain.Tests;

public class BranchTests
{
    [Fact]
    public void Branch_ShouldKeepAnUpperCaseCodeAndDropBlankContactFields()
    {
        var branch = new Branch(Guid.NewGuid(), "  Centro ", " cen-01 ", new BranchContact(City: " Puebla ", ContactPhone: "  "));

        Assert.Equal("Centro", branch.Name);
        Assert.Equal("CEN-01", branch.Code);
        Assert.Equal("Puebla", branch.City);
        Assert.Null(branch.ContactPhone);
        Assert.True(branch.IsActive);
        Assert.False(branch.IsDefault);
    }

    [Theory]
    [InlineData("", "CEN")]
    [InlineData("Centro", " ")]
    public void Branch_ShouldNeedNameAndCode(string name, string code)
    {
        Assert.Throws<DomainException>(() => new Branch(Guid.NewGuid(), name, code));
    }

    [Fact]
    public void DefaultBranch_ShouldNotBeDeactivated_AndAnInactiveOneShouldNotBecomeDefault()
    {
        var main = Branch.CreateDefault(Guid.NewGuid());
        var other = new Branch(main.TenantId, "Norte", "NTE");
        other.Deactivate();

        Assert.True(main.IsDefault);
        Assert.Throws<DomainException>(main.Deactivate);
        Assert.Throws<DomainException>(other.MarkAsDefault);

        other.Activate();
        other.MarkAsDefault();
        main.UnmarkAsDefault();
        main.Deactivate();
        Assert.True(other.IsDefault);
        Assert.False(main.IsActive);
    }

    [Fact]
    public void DailySalesFigures_ShouldAddUpAndKeepWhatWasNotGivenBack()
    {
        var monday = new DailySalesFigures(3, 2, 3, 300m, 100m, 150m, 50m, 0m, 0m, 1, 40m);
        var tuesday = new DailySalesFigures(1, 1, 1, 80m, 80m, 0m, 0m, 0m, 0m, 0, 0m);

        var week = monday.Add(tuesday);

        Assert.Equal(4, week.OrdersCreated);
        Assert.Equal(3, week.PaidOrders);
        Assert.Equal(380m, week.PaymentsAmount);
        Assert.Equal(180m, week.CashAmount);
        Assert.Equal(340m, week.NetSales);
        Assert.True(DailySalesFigures.Zero.IsEmpty);
        Assert.False(week.IsEmpty);
        // Figures written before the «Plataforma» method existed have none.
        Assert.Equal(0m, week.PlatformAmount);
    }

    [Fact]
    public void DailySalesFigures_ShouldAddUpWhatThePlatformsCharged_ApartFromOther()
    {
        var monday = new DailySalesFigures(2, 2, 2, 450m, 100m, 0m, 0m, 0m, 0m, 0, 0m, PlatformAmount: 350m);
        var tuesday = new DailySalesFigures(1, 1, 1, 245m, 0m, 0m, 0m, 0m, 0m, 1, 245m, PlatformAmount: 245m);

        var week = monday.Add(tuesday);

        Assert.Equal(595m, week.PlatformAmount);
        Assert.Equal(0m, week.OtherAmount);
        Assert.Equal(695m, week.PaymentsAmount);
        Assert.Equal(week.PaymentsAmount,
            week.CashAmount + week.CardAmount + week.TransferAmount + week.WalletAmount + week.OtherAmount + week.PlatformAmount);
        Assert.Equal(0m, DailySalesFigures.Zero.PlatformAmount);
    }

    [Fact]
    public void DailySalesSnapshot_ShouldStoreWhatThePlatformsCharged()
    {
        var figures = new DailySalesFigures(1, 1, 1, 350m, 0m, 0m, 0m, 0m, 0m, 0, 0m, PlatformAmount: 350m);

        var snapshot = new DailySalesSnapshot(
            Guid.NewGuid(), new DateOnly(2026, 10, 11), null, OrderType.Retail, "America/Mexico_City", figures,
            new DateTimeOffset(2026, 10, 12, 6, 30, 0, TimeSpan.Zero));

        Assert.Equal(350m, snapshot.PlatformAmount);
        Assert.Equal(figures, snapshot.Figures);
    }
}
