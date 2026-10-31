using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Fiscal.Events;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The CFDI of a month to the business's accountant (Fase 28O, ADR 0060): one email per request, of a month of CFDI 4.0, to
/// one plain address, asked for by a person or by TienditApp at the start of the month; only its outcome changes afterwards.
/// And the accountant's email the invoicing profile keeps, with whether the month goes on its own.
/// </summary>
public class CfdiMonthEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(-6));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public void Request_ShouldCarryTheMonth_ToTheAccountant_AndGoOutFromTheOutbox()
    {
        var email = CfdiMonthEmail.Request(TenantId, 2026, 10, " contador@despacho.mx ", OwnerId, Now);

        Assert.Equal((TenantId, 2026, 10, "contador@despacho.mx", (Guid?)OwnerId, Now),
            (email.TenantId, email.Year, email.Month, email.To, email.RequestedByUserId, email.RequestedAt));
        Assert.True(email.IsPending);
        Assert.False(email.IsMonthly);
        Assert.Equal(email.Id, Assert.IsType<CfdiMonthEmailRequestedEvent>(Assert.Single(email.DomainEvents)).CfdiMonthEmailId);
    }

    [Fact]
    public void Request_ByTienditApp_ShouldBeTheMonthly_One()
    {
        var email = CfdiMonthEmail.Request(TenantId, 2026, 10, "contador@despacho.mx", null, Now);

        Assert.True(email.IsMonthly);
        Assert.Null(email.RequestedByUserId);
    }

    [Theory]
    [InlineData(2021, 12)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void Request_ShouldRefuse_AMonthBeforeCfdi40_OrNoMonth(int year, int month)
    {
        var refused = Assert.Throws<DomainException>(() => CfdiMonthEmail.Request(TenantId, year, month, "contador@despacho.mx", OwnerId, Now));

        Assert.Equal(CfdiMonthEmail.InvalidMonthMessage, refused.Message);
    }

    [Theory]
    [InlineData(null, CfdiMonthEmail.MissingEmailMessage)]
    [InlineData("  ", CfdiMonthEmail.MissingEmailMessage)]
    [InlineData("Contador <contador@despacho.mx>", InvoiceEmailAddress.InvalidMessage)]
    [InlineData("contador@despacho.mx, otro@despacho.mx", InvoiceEmailAddress.InvalidMessage)]
    [InlineData("peña@despacho.mx", InvoiceEmailAddress.InvalidMessage)]
    public void Request_ShouldRefuse_AnythingButOnePlainAddress(string? to, string message)
    {
        var refused = Assert.Throws<DomainException>(() => CfdiMonthEmail.Request(TenantId, 2026, 10, to, OwnerId, Now));

        Assert.Equal(message, refused.Message);
    }

    [Fact]
    public void Request_ShouldNeedItsBusiness_AndWhoAskedForIt_WhenAPersonDid()
    {
        Assert.Throws<DomainException>(() => CfdiMonthEmail.Request(Guid.Empty, 2026, 10, "contador@despacho.mx", OwnerId, Now));
        Assert.Throws<DomainException>(() => CfdiMonthEmail.Request(TenantId, 2026, 10, "contador@despacho.mx", Guid.Empty, Now));
    }

    [Fact]
    public void Sent_ShouldKeepHowManyCfdiTheListCarried_Once()
    {
        var email = CfdiMonthEmail.Request(TenantId, 2026, 10, "contador@despacho.mx", OwnerId, Now);
        email.AttemptFailed(Now.AddMinutes(1));

        email.Sent(128, Now.AddMinutes(5));
        // The outbox ran it twice, or a late refusal came in: the first outcome stands.
        email.Sent(130, Now.AddMinutes(6));
        email.Fail(CfdiMonthEmailFailure.Rejected, Now.AddMinutes(7));
        email.AttemptFailed(Now.AddMinutes(8));

        Assert.Equal((Now.AddMinutes(5), (int?)128, (DateTimeOffset?)null, (CfdiMonthEmailFailure?)null, 1),
            (email.SentAt, email.CfdiCount, email.FailedAt, email.Failure, email.FailedAttempts));
        Assert.False(email.IsPending);
        Assert.Throws<DomainException>(() => CfdiMonthEmail.Request(TenantId, 2026, 10, "contador@despacho.mx", OwnerId, Now).Sent(-1, Now));
    }

    [Fact]
    public void Fail_ShouldKeepWhy_AndThen_NothingElse()
    {
        var email = CfdiMonthEmail.Request(TenantId, 2026, 10, "contador@despacho.mx", null, Now);

        email.Fail(CfdiMonthEmailFailure.TooLarge, Now.AddMinutes(1));
        email.Sent(12, Now.AddMinutes(2));

        Assert.Equal((Now.AddMinutes(1), (CfdiMonthEmailFailure?)CfdiMonthEmailFailure.TooLarge, (DateTimeOffset?)null, (int?)null),
            (email.FailedAt, email.Failure, email.SentAt, email.CfdiCount));
    }

    [Fact]
    public void AttemptFailed_ShouldCountTheTries_TheOutboxRepeats()
    {
        var email = CfdiMonthEmail.Request(TenantId, 2026, 10, "contador@despacho.mx", OwnerId, Now);

        email.AttemptFailed(Now.AddMinutes(1));
        email.AttemptFailed(Now.AddMinutes(3));

        Assert.Equal((2, (DateTimeOffset?)Now.AddMinutes(3)), (email.FailedAttempts, email.LastFailedAttemptAt));
        Assert.True(email.IsPending);
    }

    [Fact]
    public void UseAccountant_ShouldKeepTheEmail_AndSendEachMonthOnlyWithIt()
    {
        var profile = InvoicingProfile.Create(TenantId);
        Assert.Equal(((string?)null, false), (profile.AccountantEmail, profile.SendsMonthToAccountant));

        profile.UseAccountant(" contador@despacho.mx ", monthly: true);
        Assert.Equal(("contador@despacho.mx", true), (profile.AccountantEmail, profile.SendsMonthToAccountant));

        // Without an email nothing goes on its own.
        var refused = Assert.Throws<DomainException>(() => profile.UseAccountant(" ", monthly: true));
        Assert.Equal(InvoicingProfile.MonthlyNeedsAccountantMessage, refused.Message);
        Assert.Equal(("contador@despacho.mx", true), (profile.AccountantEmail, profile.SendsMonthToAccountant));
        Assert.Throws<DomainException>(() => profile.UseAccountant("Contador <contador@despacho.mx>", monthly: false));

        profile.UseAccountant(null, monthly: false);
        Assert.Equal(((string?)null, false), (profile.AccountantEmail, profile.SendsMonthToAccountant));
    }

    [Fact]
    public void RememberAccountant_ShouldKeepTheLastAddress_AndLeaveTheMonthlySendingAsItWas()
    {
        var profile = InvoicingProfile.Create(TenantId);
        profile.UseAccountant("contador@despacho.mx", monthly: true);

        profile.RememberAccountant(" otra@despacho.mx ");

        Assert.Equal(("otra@despacho.mx", true), (profile.AccountantEmail, profile.SendsMonthToAccountant));
        Assert.Throws<DomainException>(() => profile.RememberAccountant(" "));
        Assert.Equal("otra@despacho.mx", profile.AccountantEmail);
    }
}
