using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// A CFDI de retenciones of a delivery platform (Fase 22J): what its XML says is kept as it is, only within what any such
/// voucher can say, and one registered by mistake is removed with a reason, not deleted.
/// </summary>
public class PlatformWithholdingCertificateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly CurrentMonth = new(2026, 10, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Certificate_ShouldKeepWhatTheXmlSays_WithTheRfcsAsTheSatWritesThem()
    {
        var certificate = new PlatformWithholdingCertificate(Tenant, OrderChannel.UberEats, Data() with
        {
            IssuerRfc = " ubv121024tn8 ",
            IssuerName = " UBER B.V. ",
            ReceiverRfc = "cla150101ab1",
            IssuedOn = new DateTime(2026, 10, 2, 22, 28, 50, DateTimeKind.Utc)
        }, CurrentMonth);

        Assert.Equal((Tenant, OrderChannel.UberEats), (certificate.TenantId, certificate.Channel));
        Assert.Equal(("UBV121024TN8", "UBER B.V.", "CLA150101AB1"), (certificate.IssuerRfc, certificate.IssuerName, certificate.ReceiverRfc));
        Assert.Equal((2026, 9, 9), (certificate.Year, certificate.MonthFrom, certificate.MonthTo));
        // The time of its place, as written: no zone is made up for it.
        Assert.Equal(DateTimeKind.Unspecified, certificate.IssuedOn.Kind);
        Assert.Equal(new DateTime(2026, 10, 2, 22, 28, 50), certificate.IssuedOn);
        Assert.Equal(
            (16269.76m, (decimal?)16269.76m, 406.74m, (decimal?)2603.16m, 1301.58m, 1708.32m),
            (certificate.OperationTotal, certificate.IsrBase, certificate.IsrWithheld, certificate.VatBase, certificate.VatWithheld, certificate.TotalWithheld));
        Assert.False(certificate.IsRemoved);
    }

    [Fact]
    public void Certificate_WithoutTheBasesOrWithAnotherTaxWithheld_ShouldBeKept()
    {
        // The bases are optional in the XML, and an IEPS withheld counts only in the total.
        var certificate = new PlatformWithholdingCertificate(Tenant, OrderChannel.DidiFood, Data() with
        {
            IsrBase = null,
            VatBase = null,
            TotalWithheld = 1800m
        }, CurrentMonth);

        Assert.Null(certificate.IsrBase);
        Assert.Null(certificate.VatBase);
        Assert.Equal(1800m, certificate.TotalWithheld);
    }

    [Fact]
    public void Certificate_ShouldBeOfAPlatformAndABusiness()
    {
        Assert.Equal(DeliveryPlatformSetting.TenantRequiredMessage, Assert.Throws<DomainException>(
            () => new PlatformWithholdingCertificate(Guid.Empty, OrderChannel.Rappi, Data(), CurrentMonth)).Message);
        Assert.Equal(DeliveryPlatformSetting.NotADeliveryPlatformMessage, Assert.Throws<DomainException>(
            () => new PlatformWithholdingCertificate(Tenant, OrderChannel.Counter, Data(), CurrentMonth)).Message);
    }

    [Fact]
    public void Certificate_ShouldRefuseWhatNoCfdiDeRetencionesSays()
    {
        Refused(PlatformWithholdingCertificate.UuidRequiredMessage, Data() with { Uuid = Guid.Empty });
        Refused(PlatformWithholdingCertificate.IssuerRfcMessage, Data() with { IssuerRfc = "UBER" });
        Refused(PlatformWithholdingCertificate.IssuerRfcMessage, Data() with { IssuerRfc = null! });
        Refused(PlatformWithholdingCertificate.IssuerNameMessage, Data() with { IssuerName = "  " });
        Refused(PlatformWithholdingCertificate.IssuerNameMessage, Data() with { IssuerName = new string('U', 301) });
        Refused(PlatformWithholdingCertificate.ReceiverRfcMessage, Data() with { ReceiverRfc = "XAXX0101010" });
        Refused(PlatformWithholdingCertificate.PeriodMessage, Data() with { MonthFrom = 0 });
        Refused(PlatformWithholdingCertificate.PeriodMessage, Data() with { MonthTo = 13 });
        Refused(PlatformWithholdingCertificate.PeriodMessage, Data() with { MonthFrom = 10, MonthTo = 9 });
        Refused(PlatformWithholdingCertificate.YearMessage, Data() with { Year = 2018 });
        // October of the business is fine (a weekly one comes before the month ends); November is not yet.
        Assert.Equal(10, new PlatformWithholdingCertificate(
            Tenant, OrderChannel.DidiFood, Data() with { MonthFrom = 10, MonthTo = 10 }, CurrentMonth).MonthTo);
        Refused(PlatformWithholdingCertificate.PeriodInTheFutureMessage, Data() with { MonthFrom = 11, MonthTo = 11 });
        Refused(PlatformWithholdingCertificate.PeriodInTheFutureMessage, Data() with { Year = 2027, MonthFrom = 1, MonthTo = 1 });
        Refused(PlatformWithholdingCertificate.PeriodInTheFutureMessage, Data() with { Year = 99999 });
    }

    [Fact]
    public void Certificate_ShouldRefuseAmountsNoCfdiWrites()
    {
        Refused(PlatformWithholdingCertificate.AmountMessage, Data() with { OperationTotal = -0.01m });
        Refused(PlatformWithholdingCertificate.AmountMessage, Data() with { IsrWithheld = 406.745m, TotalWithheld = 1708.325m });
        Refused(PlatformWithholdingCertificate.AmountMessage, Data() with { VatBase = -1m });
        Refused(PlatformWithholdingCertificate.AmountMessage, Data() with { IsrBase = 0.001m });
        Refused(PlatformWithholdingCertificate.AmountMessage, Data() with { OperationTotal = 1_000_000_000.01m });
        Refused(PlatformWithholdingCertificate.TotalWithheldMessage, Data() with { TotalWithheld = 1708.31m });
        // A month without withholdings is still a voucher.
        var empty = new PlatformWithholdingCertificate(Tenant, OrderChannel.Rappi, Data() with
        {
            OperationTotal = 0m, IsrBase = 0m, IsrWithheld = 0m, VatBase = 0m, VatWithheld = 0m, TotalWithheld = 0m
        }, CurrentMonth);
        Assert.Equal(0m, empty.TotalWithheld);
    }

    [Fact]
    public void Remove_ShouldTakeItOutWithAReason_Once()
    {
        var certificate = new PlatformWithholdingCertificate(Tenant, OrderChannel.Rappi, Data(), CurrentMonth);

        Assert.Equal(PlatformWithholdingCertificate.RemovalReasonRequiredMessage,
            Assert.Throws<DomainException>(() => certificate.Remove("  ", Now)).Message);
        Assert.Equal(PlatformWithholdingCertificate.RemovalReasonTooLongMessage,
            Assert.Throws<DomainException>(() => certificate.Remove(new string('x', 501), Now)).Message);
        Assert.False(certificate.IsRemoved);

        certificate.Remove("  Era de otra sucursal  ", Now);

        Assert.True(certificate.IsRemoved);
        Assert.Equal((Now, "Era de otra sucursal"), (certificate.RemovedAt!.Value, certificate.RemovalReason));
        Assert.Equal(PlatformWithholdingCertificate.RemovedMessage,
            Assert.Throws<DomainException>(() => certificate.Remove("Otra vez", Now.AddMinutes(1))).Message);
        Assert.Equal(Now, certificate.RemovedAt);
    }

    /// <summary>September 2026 of Uber Eats: 2.5 % of ISR and half the IVA of $16,269.76.</summary>
    private static PlatformWithholdingCertificateData Data() => new(
        Uuid: Guid.Parse("7874d580-da5b-558f-b8ec-d92c57197242"),
        IssuerRfc: "UBV121024TN8",
        IssuerName: "UBER B.V.",
        ReceiverRfc: "CLA150101AB1",
        Year: 2026,
        MonthFrom: 9,
        MonthTo: 9,
        IssuedOn: new DateTime(2026, 10, 2, 22, 28, 50),
        OperationTotal: 16269.76m,
        IsrBase: 16269.76m,
        IsrWithheld: 406.74m,
        VatBase: 2603.16m,
        VatWithheld: 1301.58m,
        TotalWithheld: 1708.32m);

    private static void Refused(string message, PlatformWithholdingCertificateData data)
    {
        var error = Assert.Throws<DomainException>(
            () => new PlatformWithholdingCertificate(Tenant, OrderChannel.Rappi, data, CurrentMonth));
        Assert.Equal(message, error.Message);
    }
}
