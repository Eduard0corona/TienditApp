using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The global invoice of a period (Fase 28B, ADR 0046): the days each periodicity covers and how InformacionGlobal names
/// them, the sales it takes, and how a refusal or a cancellation frees them without deleting anything.
/// </summary>
public class GlobalInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(-6));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private const string Uuid = "5f0b7c1e-2a3d-4e5f-8a9b-0c1d2e3f4a5b";
    private const string Document = "{\"conceptos\":[]}";

    [Theory]
    // A day is itself.
    [InlineData(GlobalInvoicePeriodicity.Daily, "2026-10-23", "2026-10-23", "2026-10-23", "01", "10")]
    // A week goes Monday to Sunday…
    [InlineData(GlobalInvoicePeriodicity.Weekly, "2026-10-23", "2026-10-19", "2026-10-25", "02", "10")]
    // …but never crosses into another month: the sales of each month are declared in it.
    [InlineData(GlobalInvoicePeriodicity.Weekly, "2026-10-30", "2026-10-26", "2026-10-31", "02", "10")]
    [InlineData(GlobalInvoicePeriodicity.Weekly, "2026-11-01", "2026-11-01", "2026-11-01", "02", "11")]
    [InlineData(GlobalInvoicePeriodicity.Fortnightly, "2026-10-15", "2026-10-01", "2026-10-15", "03", "10")]
    [InlineData(GlobalInvoicePeriodicity.Fortnightly, "2026-02-16", "2026-02-16", "2026-02-28", "03", "02")]
    [InlineData(GlobalInvoicePeriodicity.Monthly, "2026-10-23", "2026-10-01", "2026-10-31", "04", "10")]
    [InlineData(GlobalInvoicePeriodicity.Monthly, "2028-02-10", "2028-02-01", "2028-02-29", "04", "02")]
    // Two months are named 13 (January and February) to 18 (November and December).
    [InlineData(GlobalInvoicePeriodicity.Bimonthly, "2026-02-20", "2026-01-01", "2026-02-28", "05", "13")]
    [InlineData(GlobalInvoicePeriodicity.Bimonthly, "2026-11-02", "2026-11-01", "2026-12-31", "05", "18")]
    public void Period_ShouldCoverTheDaysOfItsPeriodicity_AndNameThemAsTheSatDoes(
        GlobalInvoicePeriodicity periodicity, string day, string from, string to, string code, string months)
    {
        var period = GlobalInvoicePeriod.Containing(periodicity, DateOnly.Parse(day));

        Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to)), (period.From, period.To));
        Assert.Equal((code, months, DateOnly.Parse(from).Year), (period.PeriodicityCode, period.Months, period.Year));
    }

    [Fact]
    public void Period_ShouldBeInvoicedOnlyOnceItIsOver()
    {
        var october = GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Monthly, new DateOnly(2026, 10, 5));

        Assert.False(october.EndedBy(new DateOnly(2026, 10, 31)));
        Assert.True(october.EndedBy(new DateOnly(2026, 11, 1)));
        Assert.True(october.Holds(new DateOnly(2026, 10, 31)));
        Assert.False(october.Holds(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Request_ShouldTakeItsSales_WithTheFolioDateAndKeyFixedBeforeThePacIsAsked()
    {
        var invoice = Global();

        Assert.Equal((TenantId, "F", 12L, "F-12", Now, OrderInvoiceStatus.Stamping),
            (invoice.TenantId, invoice.Serie, invoice.Folio, invoice.Number, invoice.IssuedAt, invoice.Status));
        Assert.Equal((GlobalInvoicePeriodicity.Monthly, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "10", 2026),
            (invoice.Periodicity, invoice.PeriodFrom, invoice.PeriodTo, invoice.Months, invoice.Year));
        Assert.Equal(("01", "PUE", 200m, 32m, 232m), (invoice.PaymentForm, invoice.PaymentMethod, invoice.SubTotal, invoice.TaxTotal, invoice.Total));
        Assert.Equal(2, invoice.OrderCount);
        Assert.Equal(["ORD-000001", "ORD-000002"], invoice.Orders.Select(order => order.OrderNumber));
        Assert.All(invoice.Orders, order => Assert.Equal((TenantId, invoice.Id, false), (order.TenantId, order.GlobalInvoiceId, order.Released)));
        Assert.NotEqual(Guid.Empty, invoice.IdempotencyKey);
        Assert.True(invoice.IsLive);
    }

    [Fact]
    public void Request_ShouldRefuseSalesThatDoNotMakeAGlobalInvoice()
    {
        var sale = new GlobalInvoiceSale(Guid.NewGuid(), "ORD-000001", 116m);

        Assert.Throws<DomainException>(() => Global(sales: []));
        // The same sale twice, a sale without its folio or amount, or sales that do not add up to the total.
        Assert.Throws<DomainException>(() => Global(sales: [sale, sale], total: 232m));
        Assert.Throws<DomainException>(() => Global(sales: [sale with { OrderNumber = " " }], subTotal: 100m, tax: 16m, total: 116m));
        Assert.Throws<DomainException>(() => Global(sales: [sale with { Total = 0m }], subTotal: 0m, tax: 0m, total: 0m));
        Assert.Throws<DomainException>(() => Global(sales: [sale], subTotal: 200m, tax: 32m, total: 232m));
        var tooMany = Enumerable.Range(1, GlobalInvoice.MaxSales + 1)
            .Select(index => new GlobalInvoiceSale(Guid.NewGuid(), $"ORD-{index:000000}", 1m))
            .ToList();
        var error = Assert.Throws<DomainException>(() => Global(sales: tooMany, subTotal: tooMany.Count, tax: 0m, total: tooMany.Count));
        Assert.Equal(GlobalInvoice.TooManySalesMessage(GlobalInvoice.MaxSales + 1), error.Message);
    }

    [Fact]
    public void Refusal_ShouldFreeItsSales_AndKeepThem()
    {
        var invoice = Global();

        invoice.Refuse("El RFC del emisor no está en la LCO.", Now);

        Assert.Equal((OrderInvoiceStatus.Refused, false), (invoice.Status, invoice.IsLive));
        Assert.Equal(2, invoice.Orders.Count);
        Assert.All(invoice.Orders, order => Assert.True(order.Released));
    }

    [Fact]
    public void Cancellation_ShouldTakeMotive02Or04_AndFreeItsSalesOnlyOnceTheSatCancelsIt()
    {
        var invoice = Stamped();

        invoice.RequestCancellation(CfdiCancellationMotive.NominativeInGlobalInvoice, OwnerId, Now);
        Assert.Equal((OrderInvoiceStatus.Cancelling, true), (invoice.Status, invoice.IsLive));
        Assert.All(invoice.Orders, order => Assert.False(order.Released));

        invoice.Cancel(Now.AddMinutes(1));
        Assert.Equal((OrderInvoiceStatus.Cancelled, false), (invoice.Status, invoice.IsLive));
        Assert.All(invoice.Orders, order => Assert.True(order.Released));

        Assert.True(Stamped().TakesCancellationMotive(CfdiCancellationMotive.ErrorsWithoutReplacement));
        foreach (var motive in new[] { CfdiCancellationMotive.ErrorsWithReplacement, CfdiCancellationMotive.NotCarriedOut })
        {
            var error = Assert.Throws<DomainException>(() => Stamped().RequestCancellation(motive, OwnerId, Now));
            Assert.Contains("04", error.Message);
        }
    }

    [Fact]
    public void Cancellation_ThatTheSatRefuses_ShouldKeepItsSalesTaken()
    {
        var invoice = Stamped();
        invoice.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, OwnerId, Now);

        invoice.KeepAfterRefusedCancellation("El SAT no canceló la factura.");

        Assert.Equal(OrderInvoiceStatus.Stamped, invoice.Status);
        Assert.All(invoice.Orders, order => Assert.False(order.Released));
    }

    private static GlobalInvoice Stamped()
    {
        var invoice = Global();
        invoice.Stamp(Uuid, Uuid, Now.AddSeconds(3), "invoices/t/202611/g.xml");
        return invoice;
    }

    private static GlobalInvoice Global(
        IReadOnlyList<GlobalInvoiceSale>? sales = null, decimal subTotal = 200m, decimal tax = 32m, decimal total = 232m)
        => GlobalInvoice.Request(
            TenantId,
            GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Monthly, new DateOnly(2026, 10, 1)),
            "F",
            12,
            "01",
            Document,
            subTotal,
            tax,
            total,
            sales ?? [new GlobalInvoiceSale(Guid.NewGuid(), "ORD-000001", 116m), new GlobalInvoiceSale(Guid.NewGuid(), "ORD-000002", 116m)],
            OwnerId,
            Now);
}
