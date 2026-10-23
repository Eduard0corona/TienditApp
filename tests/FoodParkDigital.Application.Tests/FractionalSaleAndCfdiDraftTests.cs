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

/// <summary>Selling by weight or measure and previewing the CFDI of a sale (an order of a delivery platform too).</summary>
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

    [Fact]
    public async Task CfdiDraft_OfAnOrderThePlatformCharged_ShouldBePueWithForm31_AndSayHowToInvoiceIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        await SetFiscalProfileAsync(fixture, tenant);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 350m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var order = await fixture.CreateOrderHandler().Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id), CancellationToken.None);
        Assert.True(order.IsSuccess, order.Error);
        var handler = new GetOrderCfdiDraftQueryHandler(fixture.Context);

        var globalInvoice = await handler.Handle(new GetOrderCfdiDraftQuery { OrderId = order.Value.Id }, CancellationToken.None);
        var toACustomer = await handler.Handle(new GetOrderCfdiDraftQuery
        {
            OrderId = order.Value.Id,
            Receiver = new CfdiReceiverInput { Rfc = "XAXX010101000", Name = "Juan Pérez", PostalCode = "64000", FiscalRegime = "616", CfdiUse = "S01" }
        }, CancellationToken.None);

        // I9: paid in full by the platform, it declares PUE and form 31 (intermediary of payments).
        Assert.True(globalInvoice.IsSuccess, globalInvoice.Error);
        var cfdi = globalInvoice.Value;
        Assert.Equal(("PUE", SatCatalog.PaymentFormIntermediary), (cfdi.PaymentMethod, cfdi.PaymentForm));
        Assert.Equal("31", cfdi.PaymentForm);
        Assert.Equal(350m, cfdi.Total);
        Assert.True(cfdi.IsReadyToStamp, string.Join(" | ", cfdi.Blockers));
        Assert.True(cfdi.IsGeneralPublic);
        Assert.Contains(cfdi.Warnings, warning => warning.StartsWith(
            "Rappi cobró este pedido en su app: la factura lleva la forma de pago 31 (intermediario de pagos)", StringComparison.Ordinal)
            && warning.Contains("factura global") && warning.Contains("ISR e IVA") && warning.Contains("contador"));
        Assert.DoesNotContain(cfdi.Warnings, warning => warning.Contains("no comparte los datos"));
        // The platform shares no customer data: invoicing someone needs theirs, and keeping it out of the global invoice.
        Assert.True(toACustomer.IsSuccess, toACustomer.Error);
        Assert.Contains(toACustomer.Value.Warnings, warning =>
            warning == "Rappi no comparte los datos de sus clientes: factura este pedido a nombre de alguien solo si te dio sus datos fiscales, y déjalo fuera de la factura global.");
    }

    [Fact]
    public async Task CfdiDraft_OfAnOrderTheBusinessCollects_ShouldDeclareHowTheBusinessCharged()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        await SetFiscalProfileAsync(fixture, tenant);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 245m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        var order = await fixture.CreateOrderHandler().Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", tacos.Id, collectedBy: PlatformCollectedBy.Business), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var handler = new GetOrderCfdiDraftQueryHandler(fixture.Context);

        var unpaid = await handler.Handle(new GetOrderCfdiDraftQuery { OrderId = order.Value.Id }, CancellationToken.None);
        await fixture.OpenCashSessionAsync(tenant);
        var payment = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = order.Value.Id, Amount = 245m, Method = OrderPaymentMethod.Cash }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var paid = await handler.Handle(new GetOrderCfdiDraftQuery { OrderId = order.Value.Id }, CancellationToken.None);

        Assert.Equal(("PPD", SatCatalog.PaymentFormToBeDefined), (unpaid.Value.PaymentMethod, unpaid.Value.PaymentForm));
        Assert.True(payment.IsSuccess, payment.Error);
        Assert.Equal(("PUE", "01"), (paid.Value.PaymentMethod, paid.Value.PaymentForm));
        Assert.Contains(paid.Value.Warnings, warning =>
            warning == "Pedido de DiDi Food que cobra el negocio: normalmente va en la factura global del periodo. Confirma con tu contador cómo facturar los pedidos de plataformas.");
        Assert.DoesNotContain(paid.Value.Warnings, warning => warning.Contains("forma de pago 31"));
    }

    [Theory]
    [InlineData("EKU9003173C9", "Cliente", "06000", "601", "G03", true)]
    [InlineData("EKU9003173C", "Cliente", "06000", "601", "G03", false)]
    [InlineData("EKU9003173C9", "", "06000", "601", "G03", false)]
    [InlineData("EKU9003173C9", "Cliente", "6000", "601", "G03", false)]
    [InlineData("EKU9003173C9", "Cliente", "06000", "999", "G03", false)]
    [InlineData("EKU9003173C9", "Cliente", "06000", "601", "Z99", false)]
    // A use the regime cannot ask for (Fase 28A): the PAC would refuse it with CFDI40161.
    [InlineData("EKU9003173C9", "Cliente", "06000", "601", "D01", false)]
    [InlineData("EKU9003173C9", "Cliente", "06000", "605", "G03", false)]
    [InlineData("EKU9003173C9", "Cliente", "06000", "605", "D01", true)]
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
