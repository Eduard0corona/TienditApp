using FluentValidation;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Retenciones de las plataformas (Fase 22J, ADR 0042): a CFDI de retenciones is registered once in its business, only when
/// it is made out to it, removed with a reason, and compared month by month with what TienditApp calculated for the orders
/// the platform charged, less what it gave back.
/// </summary>
public class PlatformWithholdingFlowTests
{
    private static readonly TimeZoneInfo Mexico = TenantDateTimeResolver.Resolve(null);
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Fact]
    public async Task Register_ShouldKeepWhatTheXmlSays_AndLeaveItInTheActivity()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);

        var result = await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml());

        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Value;
        Assert.Equal((OrderChannel.UberEats, Guid.Parse("7874D580-DA5B-558F-B8EC-D92C57197242")), (dto.Channel, dto.Uuid));
        Assert.Equal(("UBV121024TN8", "UBER B.V.", 2026, 9, 9), (dto.IssuerRfc, dto.IssuerName, dto.Year, dto.MonthFrom, dto.MonthTo));
        Assert.Equal(
            (406.74m, (decimal?)2.50m, 1301.58m, (decimal?)50.00m, 1708.32m),
            (dto.IsrWithheld, dto.IsrRatePercent, dto.VatWithheld, dto.VatRatePercent, dto.TotalWithheld));
        var stored = await fixture.Context.PlatformWithholdingCertificates.SingleAsync();
        Assert.Equal((shop.Tenant.Id, "CLA150101AB1"), (stored.TenantId, stored.ReceiverRfc));
        var audit = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal((PlatformWithholdingAudit.RegisteredAction, nameof(PlatformWithholdingCertificate), stored.Id.ToString()),
            (audit.Action, audit.EntityName, audit.EntityId));
        Assert.Contains("\"IsrWithheld\":406.74", audit.Metadata);
        // Neither RFC goes into the activity.
        Assert.DoesNotContain("CLA150101AB1", audit.Metadata);
        Assert.DoesNotContain("UBV121024TN8", audit.Metadata);
    }

    [Fact]
    public async Task Register_ShouldCountACfdiOnce_InWhicheverPlatform_UntilItIsRemoved()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);
        var first = (await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml())).Value;

        var again = await RegisterAsync(fixture, OrderChannel.DidiFood, PlatformWithholdingXmlTests.Xml());

        Assert.Equal("Este CFDI ya está registrado en Uber Eats, septiembre de 2026.", again.Error);
        Assert.True(PlatformWithholdingErrors.IsConflict(again.Error));

        Assert.True((await RemoveAsync(fixture, first.Id, "Lo subí con la plataforma equivocada")).IsSuccess);
        var corrected = await RegisterAsync(fixture, OrderChannel.DidiFood, PlatformWithholdingXmlTests.Xml());

        Assert.True(corrected.IsSuccess, corrected.Error);
        Assert.Equal(2, await fixture.Context.PlatformWithholdingCertificates.CountAsync());
    }

    [Fact]
    public async Task Register_ShouldRefuseAVoucherOfAnotherRfc_OnlyWhenTheBusinessWroteItsOwn()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);

        // Without its RFC written, the person's word stands.
        Assert.True((await RegisterAsync(fixture, OrderChannel.Rappi, PlatformWithholdingXmlTests.Xml(receiverRfc: "XAXX010101000"))).IsSuccess);

        await SetRfcAsync(fixture, shop.Tenant.Id, " cla150101ab1 ");
        var other = await RegisterAsync(
            fixture, OrderChannel.Rappi, PlatformWithholdingXmlTests.Xml(uuid: Guid.NewGuid().ToString(), receiverRfc: "XAXX010101000"));
        var mine = await RegisterAsync(fixture, OrderChannel.Rappi, PlatformWithholdingXmlTests.Xml(uuid: Guid.NewGuid().ToString()));

        Assert.Equal(PlatformWithholdingErrors.OtherReceiver, other.Error);
        Assert.True(mine.IsSuccess, mine.Error);
    }

    [Fact]
    public async Task Register_ShouldRefuseWhatTheReaderOrTheDomainRefuses_WithoutSavingAnything()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);

        Assert.Equal(PlatformWithholdingXml.InvoiceMessage, (await RegisterAsync(fixture, OrderChannel.Rappi,
            """<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0"/>""")).Error);
        // November has not started for the business.
        Assert.Equal(PlatformWithholdingCertificate.PeriodInTheFutureMessage, (await RegisterAsync(fixture, OrderChannel.Rappi,
            PlatformWithholdingXmlTests.Xml(monthFrom: 11, monthTo: 11))).Error);
        Assert.Equal(PlatformWithholdingCertificate.TotalWithheldMessage, (await RegisterAsync(fixture, OrderChannel.Rappi,
            PlatformWithholdingXmlTests.Xml(totalWithheld: "100.00"))).Error);
        Assert.Empty(await fixture.Context.PlatformWithholdingCertificates.ToListAsync());
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());

        var validator = new RegisterPlatformWithholdingCertificateCommandValidator();
        Assert.Contains(validator.Validate(new RegisterPlatformWithholdingCertificateCommand(OrderChannel.Counter, "x")).Errors,
            e => e.ErrorMessage == DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        Assert.Contains(validator.Validate(new RegisterPlatformWithholdingCertificateCommand(OrderChannel.Rappi, " ")).Errors,
            e => e.ErrorMessage == PlatformWithholdingXml.EmptyMessage);
        Assert.Contains(validator.Validate(new RegisterPlatformWithholdingCertificateCommand(
                OrderChannel.Rappi, new string('x', PlatformWithholdingXml.MaxLength + 1))).Errors,
            e => e.ErrorMessage == PlatformWithholdingXml.TooLongMessage);
    }

    [Fact]
    public async Task Remove_ShouldTakeItOutWithItsReason_Once_AndOnlyInItsBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);
        var certificate = (await RegisterAsync(fixture, OrderChannel.Rappi, PlatformWithholdingXmlTests.Xml())).Value;

        var removed = await RemoveAsync(fixture, certificate.Id, "  Era de otra sucursal ");

        Assert.Equal(("Era de otra sucursal", true), (removed.Value.RemovalReason, removed.Value.RemovedAt is not null));
        // Removed already (someone else did, after the list was loaded): theirs stands, and the list is read again (409).
        Assert.Equal(PlatformWithholdingErrors.Stale, (await RemoveAsync(fixture, certificate.Id, "Otra vez")).Error);
        var audit = await fixture.Context.AuditLogs.SingleAsync(a => a.Action == PlatformWithholdingAudit.RemovedAction);
        Assert.Contains("Era de otra sucursal", audit.Metadata);

        var validator = new RemovePlatformWithholdingCertificateCommandValidator();
        Assert.Contains(validator.Validate(new RemovePlatformWithholdingCertificateCommand(certificate.Id, "  ")).Errors,
            e => e.ErrorMessage == PlatformWithholdingCertificate.RemovalReasonRequiredMessage);
        Assert.Contains(validator.Validate(new RemovePlatformWithholdingCertificateCommand(certificate.Id, new string('x', 501))).Errors,
            e => e.ErrorMessage == PlatformWithholdingCertificate.RemovalReasonTooLongMessage);

        // Another business does not find it.
        var other = await ShopAsync(fixture, "otro");
        fixture.TenantService.SetTenant(other.Tenant.Id);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => RemoveAsync(fixture, certificate.Id, "No es mío"));
    }

    [Fact]
    public async Task Withholdings_ShouldCompareEachMonth_WithTheOrdersThePlatformCharged()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        // $1,000 with IVA included: $862.07 without it, $21.55 of ISR (2.5 %) and $68.97 of IVA withheld (half of $137.93);
        // half of it, $431.04, $10.78 and $34.48, each rounded to even.
        await SellAtAsync(fixture, At(new DateOnly(2026, 9, 1), 0, 30), Uber(shop, "U1"));
        var halfRefunded = await SellAtAsync(fixture, At(new DateOnly(2026, 9, 15), 13), Uber(shop, "U2"));
        var refunded = await SellAtAsync(fixture, At(new DateOnly(2026, 9, 20), 13), Uber(shop, "U3"));
        // Charged by the business: nothing withheld. Of another platform, or of another month of the business: elsewhere.
        await SellAtAsync(fixture, At(new DateOnly(2026, 9, 21), 13), Uber(shop, "U4", PlatformCollectedBy.Business));
        await SellAtAsync(fixture, At(new DateOnly(2026, 9, 22), 13), Rappi(shop, "R1"));
        await SellAtAsync(fixture, At(new DateOnly(2026, 8, 31), 23, 30), Uber(shop, "U5"));
        await SellAtAsync(fixture, At(new DateOnly(2025, 12, 31), 23, 30), Uber(shop, "U6"));
        await RefundAsync(fixture, halfRefunded.Id, 500m);
        await RefundAsync(fixture, refunded.Id, 1000m);

        // September: the CFDI of the month and one of July to September; one removed does not count.
        await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml());
        await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(
            uuid: "11111111-2222-4333-8444-555555555555", monthFrom: 7, monthTo: 9));
        var mistaken = (await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(
            uuid: "22222222-2222-4333-8444-555555555555", monthFrom: 8, monthTo: 8))).Value;
        await RemoveAsync(fixture, mistaken.Id, "Duplicado");
        // Of DiDi, or of 2025: registered, and in other reconciliations.
        Assert.True((await RegisterAsync(fixture, OrderChannel.DidiFood, PlatformWithholdingXmlTests.Xml(
            uuid: "33333333-2222-4333-8444-555555555555"))).IsSuccess);
        Assert.True((await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(
            uuid: "44444444-2222-4333-8444-555555555555", year: 2025, monthFrom: 12, monthTo: 12))).IsSuccess);

        var report = await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026);

        Assert.Equal((OrderChannel.UberEats, 2026, (decimal?)2.5m, (decimal?)50m),
            (report.Channel, report.Year, report.IsrWithholdingPercent, report.VatWithholdingPercent));
        Assert.Equal(Enumerable.Range(1, 12).Select(m => (int?)m), report.Months.Select(m => m.Month));
        var september = report.Months[8];
        // U1 whole and U2 half: U3 was given back whole.
        Assert.Equal((2, 1293.11m, 32.33m, 103.45m), (september.OrderCount, september.SalesWithoutTax, september.CalculatedIsr, september.CalculatedVat));
        // The CFDI of July to September counts in September, so September compares with what July to September calculated.
        Assert.Equal(
            (2, (decimal?)32539.52m, (decimal?)813.48m, (decimal?)2603.16m, (decimal?)759.60m, (decimal?)2430.74m, (int?)7),
            (september.CertificateCount, september.CertificateOperationTotal, september.CertificateIsr, september.CertificateVat,
                september.IsrDifference, september.VatDifference, september.CoveredFromMonth));
        var august = report.Months[7];
        Assert.Equal((1, 21.55m, 68.97m, 0, (decimal?)null, (decimal?)null), (august.OrderCount, august.CalculatedIsr, august.CalculatedVat,
            august.CertificateCount, august.CertificateIsr, august.IsrDifference));
        // July and August are compared in September, where the CFDI that covers them counts.
        Assert.Equal([(int?)9, 9, null], new[] { report.Months[6], august, september }.Select(m => m.ComparedInMonth));
        Assert.All(report.Months.Where(m => m.Month is < 7 or > 9), m => Assert.Null(m.ComparedInMonth));
        Assert.All(report.Months.Where(m => m.Month is not 8 and not 9), m => Assert.Equal((0, 0m, 0), (m.OrderCount, m.CalculatedIsr, m.CertificateCount)));
        Assert.Equal(
            (null, 3, 53.88m, 172.42m, 2, (decimal?)813.48m, (decimal?)759.60m, (decimal?)2430.74m),
            (report.Total.Month, report.Total.OrderCount, report.Total.CalculatedIsr, report.Total.CalculatedVat,
                report.Total.CertificateCount, report.Total.CertificateIsr, report.Total.IsrDifference, report.Total.VatDifference));
        Assert.Null(report.Total.CoveredFromMonth);
        // The list brings the removed one too, by the month each counts in.
        Assert.Equal(
            [(8, 8, true), (7, 9, false), (9, 9, false)],
            report.Certificates.Select(c => (c.MonthFrom, c.MonthTo, c.RemovedAt is not null)));
        Assert.False(report.CertificatesTruncated);
    }

    [Fact]
    public async Task Withholdings_OfTheYear_ShouldOnlyCompareTheMonthsSomeCfdiCovers()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        // August has an order and no CFDI yet; September has its CFDI.
        await SellAtAsync(fixture, At(new DateOnly(2026, 8, 10), 12), Uber(shop, "U1"));
        await SellAtAsync(fixture, At(new DateOnly(2026, 9, 10), 12), Uber(shop, "U2"));
        await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml());

        var report = await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026);

        Assert.Equal((21.55m, 68.97m), (report.Total.CalculatedIsr - report.Months[8].CalculatedIsr, report.Total.CalculatedVat - report.Months[8].CalculatedVat));
        // The year's difference is September's: August waits for its CFDI.
        Assert.Equal((406.74m - 21.55m, 1301.58m - 68.97m), (report.Months[8].IsrDifference!.Value, report.Months[8].VatDifference!.Value));
        Assert.Equal((report.Months[8].IsrDifference, report.Months[8].VatDifference), (report.Total.IsrDifference, report.Total.VatDifference));
        Assert.Null(report.Months[8].CoveredFromMonth);
    }

    [Fact]
    public async Task Withholdings_ShouldCompareCfdiWhosePeriodsOverlap_Together_AndEachMonthOnce()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        // $21.55 of ISR and $68.97 of IVA calculated in September and in October.
        await SellAtAsync(fixture, At(new DateOnly(2026, 9, 10), 12), Uber(shop, "U1"));
        await SellAtAsync(fixture, At(new DateOnly(2026, 10, 10), 12), Uber(shop, "U2"));
        // Weekly, as DiDi issues them: the weeks of September, a week that crosses into October and the weeks of October.
        Assert.True((await RegisterAsync(fixture, OrderChannel.UberEats, Weekly("11111111-2222-4333-8444-555555555555", 9, 9, 15.00m, 50.00m))).IsSuccess);
        Assert.True((await RegisterAsync(fixture, OrderChannel.UberEats, Weekly("22222222-2222-4333-8444-555555555555", 9, 10, 12.00m, 37.94m))).IsSuccess);
        Assert.True((await RegisterAsync(fixture, OrderChannel.UberEats, Weekly("33333333-2222-4333-8444-555555555555", 10, 10, 16.10m, 50.00m))).IsSuccess);

        var report = await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026);

        // The three count together in October, against September and October: they add up. September is not compared alone.
        var september = report.Months[8];
        Assert.Equal((21.55m, 0, (decimal?)null, (decimal?)null, (int?)10),
            (september.CalculatedIsr, september.CertificateCount, september.CertificateIsr, september.IsrDifference, september.ComparedInMonth));
        var october = report.Months[9];
        Assert.Equal(
            (3, (decimal?)43.10m, (decimal?)137.94m, (decimal?)0m, (decimal?)0m, (int?)9, (int?)null),
            (october.CertificateCount, october.CertificateIsr, october.CertificateVat, october.IsrDifference, october.VatDifference,
                october.CoveredFromMonth, october.ComparedInMonth));
        Assert.Equal((3, (decimal?)0m, (decimal?)0m), (report.Total.CertificateCount, report.Total.IsrDifference, report.Total.VatDifference));
    }

    [Fact]
    public async Task Register_ACfdiThatReplacesAnother_ShouldTakeThatOneOut_AndTheReplacedOneShouldNotComeBack()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);
        const string Original = "11111111-2222-4333-8444-555555555555";
        const string Replacement = "22222222-2222-4333-8444-555555555555";
        var original = (await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(uuid: Original))).Value;

        var replacing = await RegisterAsync(fixture, OrderChannel.UberEats, Replacing(Replacement, Original));

        Assert.True(replacing.IsSuccess, replacing.Error);
        Assert.Equal(Guid.Parse(Original), replacing.Value.ReplacesUuid);
        var replaced = await fixture.Context.PlatformWithholdingCertificates.AsNoTracking().SingleAsync(c => c.Id == original.Id);
        Assert.Equal((true, "Lo sustituye el CFDI con folio 22222222."), (replaced.IsRemoved, replaced.RemovalReason));
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == PlatformWithholdingAudit.RemovedAction));
        // Only the replacement counts in September.
        Assert.Equal(1, (await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026)).Months[8].CertificateCount);

        // The cancelled one uploaded again (a downloaded folder brings both) does not count.
        var again = await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(uuid: Original));
        Assert.Equal("Este CFDI ya se sustituyó por otro que está registrado en Uber Eats, septiembre de 2026: no cuenta.", again.Error);
        Assert.True(PlatformWithholdingErrors.IsConflict(again.Error));
    }

    [Theory]
    // A replaced by B and B by C, as a downloaded folder brings them: in any order, only C counts.
    [InlineData("A", "C", "B")]
    [InlineData("C", "B", "A")]
    [InlineData("B", "C", "A")]
    [InlineData("A", "B", "C")]
    [InlineData("B", "A", "C")]
    [InlineData("C", "A", "B")]
    public async Task Register_AChainOfReplacements_InAnyOrder_ShouldLeaveOnlyTheLastOneCounting(string first, string second, string third)
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);
        const string A = "aaaaaaaa-2222-4333-8444-555555555555", B = "bbbbbbbb-2222-4333-8444-555555555555", C = "cccccccc-2222-4333-8444-555555555555";
        var files = new Dictionary<string, string>
        {
            ["A"] = PlatformWithholdingXmlTests.Xml(uuid: A),
            ["B"] = Replacing(B, A),
            ["C"] = Replacing(C, B)
        };

        foreach (var name in new[] { first, second, third })
            Assert.True((await RegisterAsync(fixture, OrderChannel.UberEats, files[name])).IsSuccess, name);

        // One that came after its replacement is kept as history, already out, with why.
        var stored = await fixture.Context.PlatformWithholdingCertificates.AsNoTracking().ToListAsync();
        Assert.Equal([Guid.Parse(C)], stored.Where(c => !c.IsRemoved).Select(c => c.Uuid));
        Assert.Equal("Lo sustituye el CFDI con folio BBBBBBBB.", stored.Single(c => c.Uuid == Guid.Parse(A)).RemovalReason);
        Assert.Equal("Lo sustituye el CFDI con folio CCCCCCCC.", stored.Single(c => c.Uuid == Guid.Parse(B)).RemovalReason);
        Assert.Equal(1, (await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026)).Months[8].CertificateCount);
        // Each one taken out left its entry in the activity.
        Assert.Equal(2, await fixture.Context.AuditLogs.CountAsync(a => a.Action == PlatformWithholdingAudit.RemovedAction));

        // Uploaded again, nothing new is kept.
        var again = await RegisterAsync(fixture, OrderChannel.UberEats, files["B"]);
        Assert.Equal("Este CFDI ya se sustituyó por otro que está registrado en Uber Eats, septiembre de 2026: no cuenta.", again.Error);
        Assert.Equal(3, await fixture.Context.PlatformWithholdingCertificates.CountAsync());
    }

    [Fact]
    public async Task Register_ACfdiThatReplacesItsPredecessor_AfterTheReplacement_ShouldSayItIsKeptWithoutCounting()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);
        const string Original = "11111111-2222-4333-8444-555555555555", Replacement = "22222222-2222-4333-8444-555555555555";
        Assert.True((await RegisterAsync(fixture, OrderChannel.UberEats, Replacing(Replacement, Original))).IsSuccess);

        var original = await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(uuid: Original));

        // It goes into the list as removed: the person sees it, and it does not count.
        Assert.True(original.IsSuccess, original.Error);
        Assert.Equal((true, "Lo sustituye el CFDI con folio 22222222."), (original.Value.RemovedAt is not null, original.Value.RemovalReason));
        Assert.Equal(1, (await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026)).Months[8].CertificateCount);
    }

    [Fact]
    public async Task Register_ACfdiOfAnotherBusiness_ThatSaysItReplacesOneOfMine_ShouldNotTakeMineOut()
    {
        await using var fixture = SalesTestFixture.Create();
        var mine = await ShopAsync(fixture);
        const string Original = "11111111-2222-4333-8444-555555555555", Replacement = "22222222-2222-4333-8444-555555555555";
        var original = (await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(uuid: Original))).Value;

        var other = await ShopAsync(fixture, "otro");
        fixture.TenantService.SetTenant(other.Tenant.Id);
        var theirs = await RegisterAsync(fixture, OrderChannel.UberEats, Replacing(Replacement, Original));
        Assert.True(theirs.IsSuccess, theirs.Error);
        Assert.Null(theirs.Value.RemovedAt);

        fixture.TenantService.SetTenant(mine.Tenant.Id);
        var stored = await fixture.Context.PlatformWithholdingCertificates.AsNoTracking().SingleAsync(c => c.Id == original.Id);
        Assert.False(stored.IsRemoved);
        Assert.Equal(1, (await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026)).Months[8].CertificateCount);
    }

    [Fact]
    public async Task Certificate_ShouldGiveTheShareOfTheIvaWithheld_OnlyWhenItsBaseIsTheIva()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);

        // Its base is the amount without IVA: 8 % of it says nothing of the share of the IVA.
        var amountAsBase = await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(withheld: """
            <retenciones:ImpRetenidos BaseRet="16269.76" ImpuestoRet="001" MontoRet="406.74" TipoPagoRet="03"/>
            <retenciones:ImpRetenidos BaseRet="16269.76" ImpuestoRet="002" MontoRet="1301.58" TipoPagoRet="01"/>
            """));

        Assert.Equal(((decimal?)2.50m, (decimal?)null), (amountAsBase.Value.IsrRatePercent, amountAsBase.Value.VatRatePercent));
    }

    /// <summary>A weekly CFDI of the months given, with the ISR and IVA withheld (no bases).</summary>
    private static string Weekly(string uuid, int monthFrom, int monthTo, decimal isr, decimal vat)
        => PlatformWithholdingXmlTests.Xml(
            uuid: uuid,
            monthFrom: monthFrom,
            monthTo: monthTo,
            totalWithheld: (isr + vat).ToString(System.Globalization.CultureInfo.InvariantCulture),
            withheld: string.Create(System.Globalization.CultureInfo.InvariantCulture, $"""
                <retenciones:ImpRetenidos ImpuestoRet="001" MontoRet="{isr}" TipoPagoRet="03"/>
                <retenciones:ImpRetenidos ImpuestoRet="002" MontoRet="{vat}" TipoPagoRet="01"/>
                """));

    /// <summary>A CFDI of September that replaces another one the platform cancelled.</summary>
    private static string Replacing(string uuid, string replaces)
        => PlatformWithholdingXmlTests.Xml(uuid: uuid).Replace(
            "<retenciones:Emisor",
            $"""<retenciones:CfdiRetenRelacionados TipoRelacion="04" UUID="{replaces}"/><retenciones:Emisor""");

    [Fact]
    public async Task Withholdings_ShouldOnlyReadTheBusinessInScope_AndAskForAPlatformAndAYear()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        await SellAtAsync(fixture, At(new DateOnly(2026, 9, 1), 12), Uber(shop, "U1"));
        await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml());

        var other = await ShopAsync(fixture, "otro");
        fixture.TenantService.SetTenant(other.Tenant.Id);
        var theirs = await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026);

        Assert.All(theirs.Months, m => Assert.Equal((0, 0), (m.OrderCount, m.CertificateCount)));
        Assert.Empty(theirs.Certificates);

        var validator = new GetPlatformWithholdingsQueryValidator();
        Assert.Contains(validator.Validate(new GetPlatformWithholdingsQuery { Channel = OrderChannel.Counter, Year = 2026 }).Errors,
            e => e.ErrorMessage == DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        Assert.Contains(validator.Validate(new GetPlatformWithholdingsQuery { Channel = OrderChannel.Rappi, Year = 2018 }).Errors,
            e => e.ErrorMessage == GetPlatformWithholdingsQueryValidator.YearMessage);
    }

    private sealed record Shop(Tenant Tenant, Product Pack);

    private static async Task<Shop> ShopAsync(SalesTestFixture fixture, string slug = "demo")
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: slug);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 1000m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        return new Shop(tenant, pack);
    }

    private static async Task SetRfcAsync(SalesTestFixture fixture, Guid tenantId, string rfc)
    {
        var tenant = await fixture.Context.Tenants.SingleAsync(t => t.Id == tenantId);
        tenant.SetBusinessInfo(rfc, "Cocina La Abuela", null, null, null);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static CreateOrderCommand Uber(Shop shop, string folio, PlatformCollectedBy? collectedBy = null)
        => SalesTestFixture.PlatformOrder(OrderChannel.UberEats, folio, shop.Pack.Id, collectedBy: collectedBy);

    private static CreateOrderCommand Rappi(Shop shop, string folio)
        => SalesTestFixture.PlatformOrder(OrderChannel.Rappi, folio, shop.Pack.Id);

    /// <summary>A local time of Mexico City.</summary>
    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0)
        => new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Mexico), TimeSpan.Zero);

    private static async Task<OrderDto> SellAtAsync(SalesTestFixture fixture, DateTimeOffset at, CreateOrderCommand command)
    {
        fixture.Clock.UtcNow = at;
        var sale = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.True(sale.IsSuccess, sale.Error);
        return sale.Value;
    }

    /// <summary>What the platform gave back of an order, as Anular registers it.</summary>
    private static async Task RefundAsync(SalesTestFixture fixture, Guid orderId, decimal amount)
    {
        var order = await fixture.Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == orderId);
        order.RegisterRefund(amount, OrderPaymentMethod.Platform, "La plataforma lo canceló", At(Today, 9), "duena", null, null, false, []);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<T> AtTodayAsync<T>(SalesTestFixture fixture, Func<Task<T>> action)
    {
        fixture.Clock.UtcNow = At(Today, 12);
        var result = await action();
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<Result<PlatformWithholdingCertificateDto>> RegisterAsync(SalesTestFixture fixture, OrderChannel channel, string xml)
        => AtTodayAsync(fixture, () => new RegisterPlatformWithholdingCertificateCommandHandler(
                fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new RegisterPlatformWithholdingCertificateCommand(channel, xml), CancellationToken.None));

    private static Task<Result<PlatformWithholdingCertificateDto>> RemoveAsync(SalesTestFixture fixture, Guid id, string reason)
        => AtTodayAsync(fixture, () => new RemovePlatformWithholdingCertificateCommandHandler(
                fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new RemovePlatformWithholdingCertificateCommand(id, reason), CancellationToken.None));

    private static Task<PlatformWithholdingsDto> WithholdingsAsync(SalesTestFixture fixture, OrderChannel channel, int year)
        => AtTodayAsync(fixture, () => new GetPlatformWithholdingsQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPlatformWithholdingsQuery { Channel = channel, Year = year }, CancellationToken.None));
}
