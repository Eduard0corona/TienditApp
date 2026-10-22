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
        Assert.Equal(PlatformWithholdingCertificate.RemovedMessage, (await RemoveAsync(fixture, certificate.Id, "Otra vez")).Error);
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
        // Of DiDi, or of 2025: other reconciliations.
        await RegisterAsync(fixture, OrderChannel.DidiFood, PlatformWithholdingXmlTests.Xml(uuid: "33333333-2222-4333-8444-555555555555"));
        await RegisterAsync(fixture, OrderChannel.UberEats, PlatformWithholdingXmlTests.Xml(
            uuid: "44444444-2222-4333-8444-555555555555", year: 2025, monthFrom: 12, monthTo: 12));

        var report = await WithholdingsAsync(fixture, OrderChannel.UberEats, 2026);

        Assert.Equal((OrderChannel.UberEats, 2026, (decimal?)2.5m, (decimal?)50m),
            (report.Channel, report.Year, report.IsrWithholdingPercent, report.VatWithholdingPercent));
        Assert.Equal(Enumerable.Range(1, 12).Select(m => (int?)m), report.Months.Select(m => m.Month));
        var september = report.Months[8];
        // U1 whole and U2 half: U3 was given back whole.
        Assert.Equal((2, 1293.11m, 32.33m, 103.45m), (september.OrderCount, september.SalesWithoutTax, september.CalculatedIsr, september.CalculatedVat));
        Assert.Equal(
            (2, (decimal?)32539.52m, (decimal?)813.48m, (decimal?)2603.16m, (decimal?)781.15m, (decimal?)2499.71m, true),
            (september.CertificateCount, september.CertificateOperationTotal, september.CertificateIsr, september.CertificateVat,
                september.IsrDifference, september.VatDifference, september.SpansMonths));
        var august = report.Months[7];
        Assert.Equal((1, 21.55m, 68.97m, 0, (decimal?)null, (decimal?)null), (august.OrderCount, august.CalculatedIsr, august.CalculatedVat,
            august.CertificateCount, august.CertificateIsr, august.IsrDifference));
        Assert.All(report.Months.Where(m => m.Month is not 8 and not 9), m => Assert.Equal((0, 0m, 0), (m.OrderCount, m.CalculatedIsr, m.CertificateCount)));
        Assert.Equal(
            (null, 3, 53.88m, 172.42m, 2, (decimal?)813.48m, (decimal?)759.60m, (decimal?)2430.74m),
            (report.Total.Month, report.Total.OrderCount, report.Total.CalculatedIsr, report.Total.CalculatedVat,
                report.Total.CertificateCount, report.Total.CertificateIsr, report.Total.IsrDifference, report.Total.VatDifference));
        // The list brings the removed one too, by the month each counts in.
        Assert.Equal(
            [(8, 8, true), (7, 9, false), (9, 9, false)],
            report.Certificates.Select(c => (c.MonthFrom, c.MonthTo, c.RemovedAt is not null)));
        Assert.False(report.CertificatesTruncated);
    }

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
