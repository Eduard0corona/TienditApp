using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The accepted quote becomes the job (Fase 29E, ADR 0053 point 17): once, a service order with its lines at the price and
/// with the IVA the customer accepted, and what was paid for the visit stays as what it takes off.
/// </summary>
public class QuoteConversionTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly QuoteCustomer Valeria = new(null, "Valeria Ruiz", "5522107765", "valeria@correo.mx");
    private static readonly Guid Heater = Guid.NewGuid();
    private static readonly OrderLineFiscalData HeaterFiscal = new(UnitType.Service, "72151500", "E48", CfdiTaxObject.Subject);

    private static Quote Accepted(bool pricesIncludeTax = true, QuoteVisitCredit? credit = null, params QuoteLineInput[] lines)
    {
        var quote = Quote.Start(Business, Folios.QuoteNumber(7), Valeria, credit is null ? null : Guid.NewGuid(), pricesIncludeTax, 15, Now);
        quote.ReplaceLines(lines.Length > 0 ? lines : [new QuoteLineInput(Heater, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        if (credit is not null)
            quote.SetVisitCredit(credit, Now);
        quote.Send(Today, new string('a', 43), null, Now);
        quote.Accept(Today, QuoteReplyChannel.WhatsApp, "Adelante", Now);
        return quote;
    }

    [Fact]
    public void AnAcceptedQuote_ShouldBecomeAServiceOrder_Once()
    {
        var quote = Accepted();
        var order = Guid.NewGuid();

        quote.ConvertToOrder(order, 0m, Now.AddHours(1));

        Assert.Equal((order, Now.AddHours(1), (decimal?)null), (quote.OrderId, quote.ConvertedAt, quote.AppliedVisitCredit));
        Assert.Equal(QuoteRules.AlreadyConvertedMessage,
            Assert.Throws<DomainException>(() => quote.ConvertToOrder(Guid.NewGuid(), 0m, Now)).Message);
    }

    [Fact]
    public void OnlyAnAcceptedQuote_ShouldBecomeTheJob()
    {
        var draft = Quote.Start(Business, Folios.QuoteNumber(8), Valeria, null, true, 15, Now);
        draft.ReplaceLines([new QuoteLineInput(null, "Revisión", 1m, 500m, VatRate.General)], Now);
        var sent = Quote.Start(Business, Folios.QuoteNumber(9), Valeria, null, true, 15, Now);
        sent.ReplaceLines([new QuoteLineInput(null, "Revisión", 1m, 500m, VatRate.General)], Now);
        sent.Send(Today, new string('b', 43), null, Now);
        var rejected = Quote.Start(Business, Folios.QuoteNumber(10), Valeria, null, true, 15, Now);
        rejected.ReplaceLines([new QuoteLineInput(null, "Revisión", 1m, 500m, VatRate.General)], Now);
        rejected.Send(Today, new string('c', 43), null, Now);
        rejected.Reject(Today, QuoteReplyChannel.Phone, null, Now);

        foreach (var quote in new[] { draft, sent, rejected })
        {
            Assert.Equal(QuoteRules.NotAcceptedMessage,
                Assert.Throws<DomainException>(() => quote.ConvertToOrder(Guid.NewGuid(), 0m, Now)).Message);
            Assert.Null(quote.OrderId);
        }
    }

    [Fact]
    public void WhatWasAppliedForTheVisit_ShouldStayAsWhatItTakesOff_WhateverTheSaleOfTheVisitHasPaidNow()
    {
        var quote = Accepted(credit: new QuoteVisitCredit(Guid.NewGuid(), 406m));
        Assert.Equal(406m, quote.VisitCreditStanding(406m));

        quote.ConvertToOrder(Guid.NewGuid(), 406m, Now);

        // The visit's money left its sale for the order: the sale has nothing paid for it now, and the quote still takes it off.
        Assert.Equal(406m, quote.AppliedVisitCredit);
        Assert.Equal(406m, quote.VisitCreditStanding(0m));
        Assert.Equal(406m, quote.VisitCreditStanding(null));
        Assert.Equal(754m, quote.ToPay(0m));
    }

    [Fact]
    public void AQuoteConvertedWithoutTheVisit_ShouldTakeNothingOff()
    {
        // Its sale was cancelled before: nothing was applied.
        var quote = Accepted(credit: new QuoteVisitCredit(Guid.NewGuid(), 406m));

        quote.ConvertToOrder(Guid.NewGuid(), 0m, Now);

        Assert.Null(quote.AppliedVisitCredit);
        Assert.Equal(0m, quote.VisitCreditStanding(406m));
        Assert.Equal(1160m, quote.ToPay(406m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(406.01)]
    public void WhatItApplies_ShouldBeAtMostWhatItTakesOffForTheVisit(decimal applied)
    {
        var quote = Accepted(credit: new QuoteVisitCredit(Guid.NewGuid(), 406m));

        Assert.Throws<ArgumentOutOfRangeException>(() => quote.ConvertToOrder(Guid.NewGuid(), applied, Now));
        Assert.Null(quote.OrderId);
    }

    [Fact]
    public void WhatItApplies_ShouldNeverPassTheTotal_EvenWhenTheVisitCostMore()
    {
        // A visit of 2,000 taken off a job of 1,160.
        var quote = Accepted(credit: new QuoteVisitCredit(Guid.NewGuid(), 2000m));

        Assert.Throws<ArgumentOutOfRangeException>(() => quote.ConvertToOrder(Guid.NewGuid(), 1160.01m, Now));
        quote.ConvertToOrder(Guid.NewGuid(), 1160m, Now);

        Assert.Equal((1160m, 0m), (quote.AppliedVisitCredit, quote.ToPay(null)));
    }

    [Fact]
    public void AQuoteWithoutTheVisit_ShouldApplyNothing()
    {
        var quote = Accepted();

        Assert.Throws<ArgumentOutOfRangeException>(() => quote.ConvertToOrder(Guid.NewGuid(), 1m, Now));
    }

    [Fact]
    public void ACatalogLine_ShouldKeepItsProductAndCodes_AtTheAcceptedPrice()
    {
        var quote = Accepted(lines: new QuoteLineInput(Heater, "Cambio de calentador", 2m, 1160m, VatRate.General));

        var line = Assert.Single(QuoteConversion.Lines(quote, new Dictionary<Guid, OrderLineFiscalData> { [Heater] = HeaterFiscal }));

        Assert.Equal((Heater, "Cambio de calentador", 2m, 1160m), (line.ProductId, line.ProductName, line.Quantity, line.UnitPrice));
        Assert.Equal((VatRate.General, HeaterFiscal), (line.VatRate, line.Fiscal));
        Assert.Empty(line.Options);
    }

    [Fact]
    public void ALineWrittenByHand_OrWhoseProductLeftTheCatalog_ShouldBeAUnitOfServiceNamedByTheLine()
    {
        var quote = Accepted(lines:
        [
            new QuoteLineInput(null, "Mano de obra", 1m, 800m, VatRate.General),
            new QuoteLineInput(Heater, "Calentador de paso", 1m, 3500m, VatRate.General),
        ]);
        var byPosition = quote.Lines.OrderBy(l => l.Position).ToList();

        var lines = QuoteConversion.Lines(quote, new Dictionary<Guid, OrderLineFiscalData>());

        Assert.Equal([byPosition[0].Id, byPosition[1].Id], lines.Select(l => l.ProductId));
        Assert.Equal(["Mano de obra", "Calentador de paso"], lines.Select(l => l.ProductName));
        Assert.All(lines, l => Assert.Equal(QuoteConversion.HandWrittenFiscal, l.Fiscal));
        Assert.Equal((UnitType.Service, SatCatalog.GenericProductCode, SatCatalog.ServiceUnitCode),
            (QuoteConversion.HandWrittenFiscal.Unit, QuoteConversion.HandWrittenFiscal.SatProductCode, QuoteConversion.HandWrittenFiscal.SatUnitCode));
    }

    [Fact]
    public void AQuantityItsUnitDoesNotSell_ShouldGoAsOneAtTheLinesAmount_WithTheQuantityInItsName()
    {
        var quote = Accepted(lines:
        [
            new QuoteLineInput(null, "Tubo de cobre", 3.5m, 99.99m, VatRate.General),
            new QuoteLineInput(Heater, "Cambio de calentador", 1.25m, 1160m, VatRate.General),
        ]);
        var hours = Guid.NewGuid();

        var lines = QuoteConversion.Lines(quote, new Dictionary<Guid, OrderLineFiscalData> { [Heater] = HeaterFiscal });

        Assert.Equal(("Tubo de cobre (3.5)", 1m, 349.96m), (lines[0].ProductName, lines[0].Quantity, lines[0].UnitPrice));
        Assert.Equal(("Cambio de calentador (1.25)", 1m, 1450m), (lines[1].ProductName, lines[1].Quantity, lines[1].UnitPrice));
        // An hour sells in fractions: it keeps its quantity.
        var byTheHour = Accepted(lines: new QuoteLineInput(hours, "Hora de técnico", 2.5m, 300m, VatRate.General));
        var hourly = Assert.Single(QuoteConversion.Lines(byTheHour, new Dictionary<Guid, OrderLineFiscalData>
        {
            [hours] = new(UnitType.Hour, SatCatalog.GenericProductCode, "HUR", CfdiTaxObject.Subject),
        }));
        Assert.Equal(("Hora de técnico", 2.5m, 300m), (hourly.ProductName, hourly.Quantity, hourly.UnitPrice));
    }

    [Fact]
    public void AProductMadeNotSubjectAfterItWasQuotedWithIva_ShouldBeInvoicedAsSubjectToIt()
    {
        var notSubject = HeaterFiscal with { TaxObject = CfdiTaxObject.NotSubject };
        var exempt = Guid.NewGuid();
        var quote = Accepted(lines:
        [
            new QuoteLineInput(Heater, "Cambio de calentador", 1m, 1160m, VatRate.General),
            new QuoteLineInput(exempt, "Material exento", 1m, 100m, VatRate.Exempt),
        ]);

        var lines = QuoteConversion.Lines(quote, new Dictionary<Guid, OrderLineFiscalData> { [Heater] = notSubject, [exempt] = notSubject });

        // With IVA it is subject to it; exempt, it keeps what the product says.
        Assert.Equal([CfdiTaxObject.Subject, CfdiTaxObject.NotSubject], lines.Select(l => l.Fiscal.TaxObject));
    }

    [Fact]
    public void ALongNameWithItsQuantity_ShouldFitInTheLine()
    {
        var name = QuoteConversion.WithQuantity(new string('x', QuoteRules.MaxDescriptionLength), 1234.567m);

        Assert.Equal(QuoteRules.MaxDescriptionLength, name.Length);
        Assert.EndsWith(" (1234.567)", name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheOrder_ShouldTotalWhatTheQuoteDid(bool pricesIncludeTax)
    {
        var quote = Accepted(pricesIncludeTax, null,
            new QuoteLineInput(Heater, "Cambio de calentador", 1m, 1160m, VatRate.General),
            new QuoteLineInput(null, "Tubo de cobre", 3.5m, 99.99m, VatRate.General),
            new QuoteLineInput(null, "Revisión de gas", 1m, 333.33m, VatRate.Of(8m)),
            new QuoteLineInput(null, "Material exento", 2m, 45.55m, VatRate.Exempt));
        var order = new Order(Business, null, null, true, Order.NumberNotAssigned, BusinessType.Service, OrderType.Service,
            "Valeria Ruiz", "+525522107765", null, null, pricesIncludeTax);

        foreach (var line in QuoteConversion.Lines(quote, new Dictionary<Guid, OrderLineFiscalData> { [Heater] = HeaterFiscal }))
            order.AddItem(line);

        Assert.Equal((quote.SubTotal, quote.Tax, quote.Total), (order.SubTotal, order.Tax, order.Total));
    }
}
