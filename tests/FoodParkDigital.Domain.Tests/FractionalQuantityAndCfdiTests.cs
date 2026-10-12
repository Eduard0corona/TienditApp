using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Taxes;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>Weighed and measured sales, CFDI data per line and the split of an order into CFDI concepts.</summary>
public class FractionalQuantityAndCfdiTests
{
    private static readonly OrderLineFiscalData Kilo = new(UnitType.Kg, "50202301", "KGM", CfdiTaxObject.Subject);

    [Theory]
    [InlineData(UnitType.Kg, 1.25, true)]
    [InlineData(UnitType.Liter, 0.5, true)]
    [InlineData(UnitType.Hour, 1.5, true)]
    [InlineData(UnitType.Kg, 0.0005, false)]
    [InlineData(UnitType.Unit, 1.5, false)]
    [InlineData(UnitType.Portion, 0.5, false)]
    [InlineData(UnitType.Unit, 3, true)]
    [InlineData(UnitType.Kg, 0, false)]
    public void UnitRules_ShouldAllowFractionsOnlyForMeasuredUnits(UnitType unit, double quantity, bool expected)
    {
        Assert.Equal(expected, UnitTypeRules.IsValidQuantity((decimal)quantity, unit));
    }

    [Fact]
    public void WeighedLine_ShouldRoundItsAmountOnceWithBankersRounding()
    {
        var order = NewOrder(pricesIncludeTax: true);

        // 0.125 kg × $1.00 = 0.125: banker's rounding gives 0.12 (away from zero would give 0.13).
        order.AddItem(Guid.NewGuid(), "Chile", 0.125m, 1m, fiscal: Kilo);
        order.AddItem(Guid.NewGuid(), "Jamón", 1.255m, 189.90m, fiscal: Kilo);

        Assert.Equal(0.12m, order.Items.First().TotalPrice);
        // 1.255 × 189.90 = 238.3245 -> 238.32
        Assert.Equal(238.32m, order.Items.Last().TotalPrice);
        Assert.Equal(238.44m, order.Total);
    }

    [Fact]
    public void PieceProduct_ShouldRejectFractionalQuantities()
    {
        var order = NewOrder(pricesIncludeTax: true);

        var error = Assert.Throws<DomainException>(() => order.AddItem(Guid.NewGuid(), "Refresco", 1.5m, 29m));

        Assert.Contains("número entero", error.Message);
    }

    [Fact]
    public void MeasuredProduct_ShouldRejectMoreThanThreeDecimals()
    {
        var order = NewOrder(pricesIncludeTax: true);

        Assert.Throws<DomainException>(() => order.AddItem(Guid.NewGuid(), "Queso", 0.1234m, 200m, fiscal: Kilo));
    }

    [Fact]
    public void Line_ShouldKeepTheUnitAndSatCodesOfTheProductWhenSold()
    {
        var product = new Product(Guid.NewGuid(), Guid.NewGuid(), "Queso Oaxaca", null, 180m, "MXN", unit: UnitType.Kg);
        product.SetFiscalData("50131700", null, CfdiTaxObject.Subject);
        var order = NewOrder(pricesIncludeTax: true);

        order.AddItem(product.Id, product.Name, 0.75m, product.PriceAmount, fiscal: OrderLineFiscalData.From(product));
        product.SetFiscalData("50131702", "GRM", CfdiTaxObject.Subject);

        var line = Assert.Single(order.Items);
        Assert.Equal(UnitType.Kg, line.Unit);
        Assert.Equal("50131700", line.SatProductCode);
        Assert.Equal("KGM", line.SatUnitCode);
        Assert.Equal(135m, line.TotalPrice);
    }

    [Fact]
    public void LineWithoutFiscalData_ShouldBeAPieceWithTheGenericCodes()
    {
        var order = NewOrder(pricesIncludeTax: true);

        order.AddItem(Guid.NewGuid(), "Refresco", 2, 29m);

        var line = Assert.Single(order.Items);
        Assert.Equal(UnitType.Unit, line.Unit);
        Assert.Equal(SatCatalog.GenericProductCode, line.SatProductCode);
        Assert.Equal("H87", line.SatUnitCode);
        Assert.Equal(CfdiTaxObject.Subject, line.TaxObject);
    }

    [Fact]
    public void Refund_ShouldReturnFractionsOfAWeighedLine()
    {
        var order = NewOrder(pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Jamón", 1.25m, 200m, fiscal: Kilo);
        order.RegisterPayment(250m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow, null, null, null, null);
        var line = order.Items.Single();

        order.RegisterRefund(100m, OrderPaymentMethod.Cash, "Pesó de más", DateTimeOffset.UtcNow, "caja", null, null, true, [(line.Id, 0.5m)]);

        Assert.Equal(0.5m, line.ReturnedQuantity);
        Assert.Equal(0.75m, line.ReturnableQuantity);
        Assert.Throws<DomainException>(() =>
            order.RegisterRefund(10m, OrderPaymentMethod.Cash, "Otra", DateTimeOffset.UtcNow, "caja", null, null, true, [(line.Id, 0.8m)]));
    }

    [Fact]
    public void ProductFiscalData_ShouldNormalizeCodesAndDefaultToTheUnit()
    {
        var product = new Product(Guid.NewGuid(), Guid.NewGuid(), "Leche", null, 28m, "MXN", unit: UnitType.Liter, taxRate: 0m);

        Assert.Equal(SatCatalog.GenericProductCode, product.GetSatProductCode());
        Assert.Equal("LTR", product.GetSatUnitCode());

        product.SetFiscalData(" 50131800 ", " xlt ", CfdiTaxObject.NotSubject);

        Assert.Equal("50131800", product.SatProductCode);
        Assert.Equal("XLT", product.GetSatUnitCode());
        Assert.Equal(CfdiTaxObject.NotSubject, product.TaxObject);
    }

    [Theory]
    [InlineData("5020230", null)]
    [InlineData("5020230A", null)]
    [InlineData(null, "KILO")]
    [InlineData(null, "K")]
    public void ProductFiscalData_ShouldRejectMalformedCodes(string? productCode, string? unitCode)
    {
        var product = new Product(Guid.NewGuid(), Guid.NewGuid(), "Queso", null, 180m, "MXN");

        Assert.Throws<DomainException>(() => product.SetFiscalData(productCode, unitCode, CfdiTaxObject.Subject));
    }

    [Fact]
    public void ProductOutsideIva_ShouldBeExemptOrAtZero()
    {
        var taxed = new Product(Guid.NewGuid(), Guid.NewGuid(), "Refresco", null, 29m, "MXN");
        var exempt = new Product(Guid.NewGuid(), Guid.NewGuid(), "Consulta", null, 500m, "MXN", isTaxExempt: true);

        Assert.Throws<DomainException>(() => taxed.SetFiscalData(null, null, CfdiTaxObject.NotSubject));
        exempt.SetFiscalData(null, null, CfdiTaxObject.NotSubject);
        Assert.Equal(CfdiTaxObject.NotSubject, exempt.TaxObject);
    }

    [Fact]
    public void Cfdi_WithPricesIncludingTax_ShouldAddUpToTheOrderAndMoveTheLeftoverCent()
    {
        // Three lines of 100.00 with IVA: the order takes out 41.38 (300 × 16 / 116 = 41.3793), while each concept
        // rounds on its own (86.21 -> 13.79). The calculator moves the cents so the invoice equals the sale.
        var lines = new[] { 100m, 100m, 100m }.Select(amount => Line(amount, VatRate.General)).ToList();

        var cfdi = CfdiAmountCalculator.Calculate(pricesIncludeTax: true, lines);

        Assert.Equal(258.62m, cfdi.SubTotal);
        Assert.Equal(41.38m, cfdi.TotalTransferred);
        Assert.Equal(300m, cfdi.Total);
        Assert.Equal([86.20m, 86.21m, 86.21m], cfdi.Concepts.Select(c => c.Amount).Order());
        Assert.All(cfdi.Concepts, AssertWithinSatBounds);
        var transfer = Assert.Single(cfdi.Transfers);
        Assert.Equal(0.16m, transfer.RateOrQuota);
        Assert.Equal("Tasa", transfer.FactorType);
        Assert.Equal(41.38m, transfer.Amount);
    }

    [Fact]
    public void Cfdi_WithTaxAdded_ShouldKeepTheBasesAndMatchTheOrderTax()
    {
        // 3 × 10.03 = 30.09; the order rounds 4.8144 -> 4.81 while each concept gives 1.6048 -> 1.60.
        var lines = new[] { 10.03m, 10.03m, 10.03m }.Select(amount => Line(amount, VatRate.General)).ToList();

        var cfdi = CfdiAmountCalculator.Calculate(pricesIncludeTax: false, lines);

        Assert.All(cfdi.Concepts, concept => Assert.Equal(10.03m, concept.Amount));
        Assert.Equal(4.81m, cfdi.TotalTransferred);
        Assert.Equal(34.90m, cfdi.Total);
        Assert.All(cfdi.Concepts, AssertWithinSatBounds);
    }

    [Fact]
    public void Cfdi_ShouldDeclareExemptZeroRateAndNonTaxableConceptsApart()
    {
        var lines = new List<CfdiLineInput>
        {
            Line(116m, VatRate.General),
            Line(30m, VatRate.Of(0m)),
            Line(500m, VatRate.Exempt),
            Line(50m, VatRate.Exempt, CfdiTaxObject.NotSubject)
        };

        var cfdi = CfdiAmountCalculator.Calculate(pricesIncludeTax: true, lines);

        Assert.Equal([100m, 30m, 500m, 50m], cfdi.Concepts.Select(c => c.Amount));
        Assert.Null(cfdi.Concepts[3].Transfer);
        Assert.Equal("Exento", cfdi.Concepts[2].Transfer!.FactorType);
        Assert.Null(cfdi.Concepts[2].Transfer!.RateOrQuota);
        Assert.Equal(0m, cfdi.Concepts[1].Transfer!.RateOrQuota);
        Assert.Collection(
            cfdi.Transfers,
            general => Assert.Equal((100m, 16m), (general.Base, general.Amount)),
            zero => Assert.Equal((30m, 0m), (zero.Base, zero.Amount)),
            exempt => Assert.Equal((500m, true), (exempt.Base, exempt.IsExempt)));
        Assert.Equal(696m, cfdi.Total);
    }

    [Fact]
    public void Cfdi_ShouldGiveTheUnitValueWithSixDecimals()
    {
        var cfdi = CfdiAmountCalculator.Calculate(pricesIncludeTax: true, [Line(238.32m, VatRate.General, quantity: 1.255m)]);

        var concept = Assert.Single(cfdi.Concepts);
        Assert.Equal(205.45m, concept.Amount);
        Assert.Equal(163.705179m, concept.UnitValue);
    }

    [Fact]
    public void Cfdi_ForRandomOrders_ShouldAlwaysMatchTheOrderWithinSatBounds()
    {
        var random = new Random(20261004);
        for (var run = 0; run < 500; run++)
        {
            var pricesIncludeTax = random.Next(2) == 0;
            var rates = new[] { VatRate.General, VatRate.Of(8m), VatRate.Of(0m), VatRate.Exempt };
            var lines = Enumerable.Range(0, random.Next(1, 25))
                .Select(_ => Line(random.Next(1, 500_000) / 100m, rates[random.Next(rates.Length)], quantity: random.Next(1, 5000) / 1000m))
                .ToList();

            var order = OrderTaxCalculator.Calculate(pricesIncludeTax, lines.Select(line => new TaxableAmount(line.Amount, line.Rate)));
            var cfdi = CfdiAmountCalculator.Calculate(pricesIncludeTax, lines);

            Assert.Equal(order.Sum(group => group.TaxableBase), cfdi.SubTotal);
            Assert.Equal(order.Sum(group => group.TaxAmount), cfdi.TotalTransferred);
            Assert.All(cfdi.Concepts, AssertWithinSatBounds);
            Assert.All(cfdi.Concepts, concept =>
                Assert.InRange(concept.Quantity * concept.UnitValue - concept.Amount, -0.005m, 0.005m));
        }
    }

    [Theory]
    [InlineData(OrderPaymentMethod.Cash, "01")]
    [InlineData(OrderPaymentMethod.Transfer, "03")]
    [InlineData(OrderPaymentMethod.Card, "04")]
    [InlineData(OrderPaymentMethod.DigitalWallet, "06")]
    [InlineData(OrderPaymentMethod.Other, SatCatalog.PaymentFormToBeDefined)]
    // What Rappi, Uber Eats or DiDi Food charged in their app: "Intermediario pagos".
    [InlineData(OrderPaymentMethod.Platform, "31")]
    public void PaymentForm_ShouldDeclareEachMethodWithItsSatCode(OrderPaymentMethod method, string expected)
    {
        Assert.Equal(expected, SatCatalog.PaymentForm(method));
        Assert.Equal("31", SatCatalog.PaymentFormIntermediary);
    }

    private static void AssertWithinSatBounds(CfdiConceptAmounts concept)
    {
        var transfer = concept.Transfer!;
        if (transfer.IsExempt)
            return;

        Assert.InRange(
            transfer.Amount,
            CfdiAmountCalculator.MinimumTax(transfer.Base, transfer.RatePercent),
            CfdiAmountCalculator.MaximumTax(transfer.Base, transfer.RatePercent));
    }

    private static CfdiLineInput Line(decimal amount, VatRate rate, CfdiTaxObject taxObject = CfdiTaxObject.Subject, decimal quantity = 1m)
        => new(Guid.NewGuid(), quantity, amount, rate, taxObject);

    private static Order NewOrder(bool pricesIncludeTax) => new(
        tenantId: Guid.NewGuid(),
        customerUserId: null,
        temporaryCustomerId: null,
        createdByBusiness: true,
        orderNumber: "ORD-000001",
        businessType: BusinessType.Retail,
        orderType: OrderType.Retail,
        customerName: "Público en general",
        customerPhone: null,
        customerNotes: null,
        tableNumber: null,
        pricesIncludeTax: pricesIncludeTax);
}
