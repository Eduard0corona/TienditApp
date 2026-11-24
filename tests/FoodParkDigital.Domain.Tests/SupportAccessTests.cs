using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The platform admin's access to one business (Fase 32G, ADR 0099): a reason of 10 to 500 characters, 15 minutes to 4
/// hours, open until its time or until the admin closes it.
/// </summary>
public class SupportAccessTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 18, 0, 0, TimeSpan.Zero);
    private const string Reason = "El dueño reporta que su corte de caja no cuadra.";

    [Fact]
    public void Start_ShouldKeepTheBusinessTheAdminTheReasonAndItsTime()
    {
        var access = SupportAccess.Start(Business, Admin, $"  {Reason}  ", 30, Now);

        Assert.Equal(Business, access.TenantId);
        Assert.Equal(Admin, access.AdminUserId);
        Assert.Equal(Reason, access.Reason);
        Assert.Equal(Now, access.StartedAt);
        Assert.Equal(Now.AddMinutes(30), access.ExpiresAt);
        Assert.Null(access.EndedAt);
    }

    [Fact]
    public void Start_ShouldNeedABusinessAndAnAccount()
    {
        Assert.Throws<DomainException>(() => SupportAccess.Start(Guid.Empty, Admin, Reason, 30, Now));
        Assert.Throws<DomainException>(() => SupportAccess.Start(Business, Guid.Empty, Reason, 30, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("revisar")]
    [InlineData("  revisar  ")]
    public void Start_ShouldNeedAReasonOfTenCharacters(string? reason)
    {
        Assert.Equal(SupportAccess.ReasonMessage,
            Assert.Throws<DomainException>(() => SupportAccess.Start(Business, Admin, reason, 30, Now)).Message);
    }

    [Fact]
    public void Start_ShouldTakeAReasonOfTenToFiveHundredCharacters()
    {
        Assert.Equal(10, SupportAccess.Start(Business, Admin, new string('a', 10), 30, Now).Reason.Length);
        Assert.Equal(500, SupportAccess.Start(Business, Admin, new string('a', 500), 30, Now).Reason.Length);

        Assert.Equal(SupportAccess.ReasonMessage,
            Assert.Throws<DomainException>(() => SupportAccess.Start(Business, Admin, new string('a', 501), 30, Now)).Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(241)]
    [InlineData(-30)]
    public void Start_ShouldLastFifteenMinutesToFourHours(int minutes)
    {
        Assert.Equal(SupportAccess.MinutesMessage,
            Assert.Throws<DomainException>(() => SupportAccess.Start(Business, Admin, Reason, minutes, Now)).Message);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(240)]
    public void Start_ShouldTakeTheShortestAndTheLongest(int minutes)
    {
        Assert.Equal(Now.AddMinutes(minutes), SupportAccess.Start(Business, Admin, Reason, minutes, Now).ExpiresAt);
    }

    [Fact]
    public void IsOpenAt_ShouldBeTrueUntilItsTime()
    {
        var access = SupportAccess.Start(Business, Admin, Reason, 30, Now);

        Assert.True(access.IsOpenAt(Now));
        Assert.True(access.IsOpenAt(Now.AddMinutes(30).AddTicks(-1)));
        Assert.False(access.IsOpenAt(Now.AddMinutes(30)));
        Assert.False(access.IsOpenAt(Now.AddHours(5)));
    }

    [Fact]
    public void End_ShouldCloseItBeforeItsTime()
    {
        var access = SupportAccess.Start(Business, Admin, Reason, 60, Now);

        Assert.True(access.End(Now.AddMinutes(10)));

        Assert.Equal(Now.AddMinutes(10), access.EndedAt);
        Assert.False(access.IsOpenAt(Now.AddMinutes(10)));
        Assert.False(access.IsOpenAt(Now.AddMinutes(20)));
    }

    [Fact]
    public void End_ShouldChangeNothingOnceClosedOrExpired()
    {
        var closed = SupportAccess.Start(Business, Admin, Reason, 60, Now);
        closed.End(Now.AddMinutes(10));
        Assert.False(closed.End(Now.AddMinutes(20)));
        Assert.Equal(Now.AddMinutes(10), closed.EndedAt);

        var expired = SupportAccess.Start(Business, Admin, Reason, 15, Now);
        Assert.False(expired.End(Now.AddMinutes(15)));
        Assert.Null(expired.EndedAt);
    }
}
