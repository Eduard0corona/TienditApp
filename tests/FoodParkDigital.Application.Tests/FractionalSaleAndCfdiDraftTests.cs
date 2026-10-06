using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Selling by weight or measure and previewing the CFDI of a sale.</summary>
public class FractionalSaleAndCfdiDraftTests
{
    [Fact]
    public async Task Sale_ByWeight_ShouldKeepThreeDecimalsAndTheUnitOfTheProduct()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var ham = await fixture.AddProductAsync(tenant, "Jamón de pierna", 189.90m, unit: UnitType.Kg);
        await fixture.OpenCashSessionAsync(tenant);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = ham.Id, Quantity = 1.255m }],
                ExpectedTotal = 238.32m,
                Payments = [new CreateOrderPaymentDto { Amount = 238.32m, Method = OrderPaymentMethod.Cash }]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var line = Assert.Single(result.Value.Items);
        Assert.Equal(1.255m, line.Quantity);
        Assert.Equal(238.32m, line.TotalPrice);
        Assert.Equal(UnitType.Kg, line.Unit);
        Assert.Equal("KGM", line.SatUnitCode);
        Assert.Equal(238.32m, result.Value.Total);
    }

    [Fact]
    public async Task Sale_OfPiecesWithAFraction_ShouldBeRejected()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);

        await Assert.ThrowsAsync<DomainException>(() => fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1.5m }] },
            CancellationToken.None));
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task Sale_OfATrackedServiceByTheHour_ShouldNeedWholeUnits()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service, pricesIncludeTax: true);
        var category = new Category(tenant.Id, "Servicios", null);
        var repair = new Product(tenant.Id, category.Id, "Reparación", null, 300m, "MXN", unit: UnitType.Hour, isTrackable: true);
        fixture.Context.Categories.Add(category);
        fixture.Context.Products.Add(repair);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                OrderType = OrderType.Service,
                Items = [new CreateOrderItemDto { ProductId = repair.Id, Quantity = 1.5m }]
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("pieza por unidad", result.Error);
    }

    [Theory]
    [InlineData(1.5, true)]
    [InlineData(0.125, true)]
    [InlineData(0.1255, false)]
    [InlineData(0, false)]
    public void SaleValidator_ShouldAcceptUpToThreeDecimals(double quantity, bool valid)
    {
        var result = new CreateOrderCommandValidator().Validate(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = (decimal)quantity }]
        });

        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData("50202301", "KGM", CfdiTaxObject.Subject, null, false, true)]
    [InlineData(null, null, CfdiTaxObject.Subject, null, false, true)]
    [InlineData("5020230", null, CfdiTaxObject.Subject, null, false, false)]
    [InlineData(null, "KILO", CfdiTaxObject.Subject, null, false, false)]
    [InlineData(null, null, CfdiTaxObject.NotSubject, 16, false, false)]
    [InlineData(null, null, CfdiTaxObject.NotSubject, null, true, true)]
    [InlineData(null, null, CfdiTaxObject.NotSubject, 0, false, true)]
    public void ProductValidator_ShouldCheckTheSatCodes(
        string? productCode,
        string? unitCode,
        CfdiTaxObject taxObject,
        int? taxRate,
        bool isExempt,
        bool valid)
    {
        var result = new CreateProductCommandValidator().Validate(new CreateProductCommand
        {
            CategoryId = Guid.NewGuid(),
            Name = "Jamón",
            PriceAmount = 189.90m,
            PriceCurrency = "MXN",
            TaxRate = (decimal?)taxRate,
            IsTaxExempt = isExempt,
            SatProductCode = productCode,
            SatUnitCode = unitCode,
            TaxObject = taxObject
        });

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task CfdiDraft_OfAPaidSale_ShouldBeReadyAndMatchTheSale()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        await SetFiscalProfileAsync(fixture, tenant);
        var coffee = await fixture.AddProductAsync(tenant, "Café", 100m);
        var tortilla = await fixture.AddProductAsync(tenant, "Tortilla", 25m, taxRate: 0m, unit: UnitType.Kg);
        await fixture.OpenCashSessionAsync(tenant);
        var order = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items =
                [
                    new CreateOrderItemDto { ProductId = coffee.Id, Quantity = 3 },
                    new CreateOrderItemDto { ProductId = tortilla.Id, Quantity = 1.5m }
                ],
                Payments =
                [
                    new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Cash },
                    new CreateOrderPaymentDto { Amount = 237.50m, Method = OrderPaymentMethod.Card }
                ]
            },
            CancellationToken.None);
        Assert.True(order.IsSuccess, order.Error);

        var draft = await new GetOrderCfdiDraftQueryHandler(fixture.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = order.Value.Id }, CancellationToken.None);

        Assert.True(draft.IsSuccess, draft.Error);
        var cfdi = draft.Value;
        Assert.True(cfdi.IsReadyToStamp, string.Join(" | ", cfdi.Blockers));
        Assert.Equal(337.50m, cfdi.Total);
        Assert.Equal(0m, cfdi.Difference);
        Assert.Equal(order.Value.Tax, cfdi.TotalTransferred);
        Assert.Equal("PUE", cfdi.PaymentMethod);
        Assert.Equal("04", cfdi.PaymentForm);
        Assert.Equal("EKU9003173C9", cfdi.Issuer.Rfc);
        Assert.Equal("626", cfdi.Issuer.FiscalRegime);
        Assert.Equal("06000", cfdi.IssuedAtPostalCode);
        Assert.True(cfdi.IsGeneralPublic);
        Assert.Equal(SatCatalog.GeneralPublicRfc, cfdi.Receiver.Rfc);
        Assert.Equal("06000", cfdi.Receiver.PostalCode);
        var weighed = Assert.Single(cfdi.Concepts, concept => concept.UnitCode == "KGM");
        Assert.Equal(1.5m, weighed.Quantity);
        Assert.Equal(0m, weighed.Transfer!.RateOrQuota);
        Assert.Contains(cfdi.Warnings, warning => warning.Contains("01010101"));
    }

    [Fact]
    public async Task CfdiDraft_WithoutFiscalProfileOrFullPayment_ShouldListWhatIsMissing()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);
        var order = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 2 }] },
            CancellationToken.None);

        var draft = await new GetOrderCfdiDraftQueryHandler(fixture.Context).Handle(
            new GetOrderCfdiDraftQuery
            {
                OrderId = order.Value.Id,
                Receiver = new CfdiReceiverInput
                {
                    Rfc = "xaxx010101000",
                    Name = "Juan Pérez",
                    PostalCode = "64000",
                    FiscalRegime = "616",
                    CfdiUse = "S01"
                }
            },
            CancellationToken.None);

        Assert.True(draft.IsSuccess, draft.Error);
        Assert.False(draft.Value.IsReadyToStamp);
        Assert.Equal(4, draft.Value.Blockers.Count);
        Assert.Equal("PPD", draft.Value.PaymentMethod);
        Assert.Equal("99", draft.Value.PaymentForm);
        Assert.False(draft.Value.IsGeneralPublic);
        Assert.Equal("XAXX010101000", draft.Value.Receiver.Rfc);
        Assert.Equal("JUAN PÉREZ", draft.Value.Receiver.Name);
    }

    [Fact]
    public async Task CfdiDraft_OfACancelledSale_ShouldBeRefused()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);
        var created = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }] },
            CancellationToken.None);
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == created.Value.Id);
        order.Cancel(OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "caja", "Error de captura");
        await fixture.Context.SaveChangesAsync();

        var draft = await new GetOrderCfdiDraftQueryHandler(fixture.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = order.Id }, CancellationToken.None);

        Assert.True(draft.IsFailure);
    }

    [Fact]
    public async Task CfdiDraft_ShouldNotFindOrdersOfAnotherTenant()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        var soda = await fixture.AddProductAsync(other, "Refresco", 29m);
        var foreignOrder = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }] },
            CancellationToken.None);
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "mio");

        await Assert.ThrowsAsync<EntityNotFoundException>(() => new GetOrderCfdiDraftQueryHandler(fixture.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = foreignOrder.Value.Id }, CancellationToken.None));
    }

    [Theory]
    [InlineData("EKU9003173C9", "Cliente", "06000", "601", "G03", true)]
    [InlineData("EKU9003173C", "Cliente", "06000", "601", "G03", false)]
    [InlineData("EKU9003173C9", "", "06000", "601", "G03", false)]
    [InlineData("EKU9003173C9", "Cliente", "6000", "601", "G03", false)]
    [InlineData("EKU9003173C9", "Cliente", "06000", "999", "G03", false)]
    [InlineData("EKU9003173C9", "Cliente", "06000", "601", "Z99", false)]
    public void CfdiDraftValidator_ShouldCheckTheReceiver(string rfc, string name, string postalCode, string regime, string use, bool valid)
    {
        var result = new GetOrderCfdiDraftQueryValidator().Validate(new GetOrderCfdiDraftQuery
        {
            OrderId = Guid.NewGuid(),
            Receiver = new CfdiReceiverInput { Rfc = rfc, Name = name, PostalCode = postalCode, FiscalRegime = regime, CfdiUse = use }
        });

        Assert.Equal(valid, result.IsValid);
    }

    private static async Task SetFiscalProfileAsync(SalesTestFixture fixture, Tenant tenant)
    {
        tenant.SetBusinessInfo(
            "EKU9003173C9",
            "ESCUELA KEMPER URGATE",
            "Comercio",
            "626 - Régimen Simplificado de Confianza (RESICO)",
            new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
        await fixture.Context.SaveChangesAsync();
    }
}
