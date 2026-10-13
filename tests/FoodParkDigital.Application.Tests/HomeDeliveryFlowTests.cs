using System.Text.Json;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Orders sent to the customer's address (Fase 22C): taken in Vender or placed from the online menu, with the active zone
/// of the business that reaches the postal code, its minimum and its fee charged as one more concept with its IVA; the
/// courier's trip in Paquetenvia written down by folio; the activity log and the invoice draft telling it, never the
/// address.
/// </summary>
public class HomeDeliveryFlowTests
{
    private const string Address = "Av. Reforma 123, Centro";

    [Fact]
    public async Task Vender_ShouldSendTheOrderToTheAddress_AddingTheZoneFeeWithItsIva()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await KitchenAsync(fixture);

        var sale = await SellAsync(fixture, Delivery(tacos, 2, " 72000 ", references: " Portón verde "));

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal(275m, sale.Value.Total);
        var delivery = sale.Value.Delivery!;
        Assert.Equal(("72000", Address, "Portón verde", "Centro", 35m, 16m),
            (delivery.PostalCode, delivery.AddressLine, delivery.References, delivery.ZoneName, delivery.Fee, delivery.FeeTaxRate));
        Assert.Null(delivery.Courier);
        // The fee is one more concept of the order, with the IVA included like its prices.
        var tax = Assert.Single(sale.Value.TaxBreakdown);
        Assert.Equal((275m, 237.07m, 37.93m), (tax.Total, tax.TaxableBase, tax.TaxAmount));
        var stored = await fixture.Context.Orders.SingleAsync();
        Assert.Equal((275m, 35m, "Centro"), (stored.Total, stored.Delivery!.Fee, stored.Delivery.ZoneName));

        // Read again, the order carries its delivery; the kitchen sees only that it goes out.
        var detail = await new GetOrderByIdQueryHandler(fixture.Context).Handle(new GetOrderByIdQuery(sale.Value.Id), CancellationToken.None);
        Assert.Equal((Address, 35m), (detail.Delivery!.AddressLine, detail.Delivery.Fee));
        Assert.Equal(sale.Value.TaxBreakdown.Single().Total, detail.TaxBreakdown.Single().Total);
    }

    [Fact]
    public async Task Delivery_ShouldBeFree_FromWhatTheZoneSays_AndRefusedBelowItsMinimum()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await KitchenAsync(fixture);

        var free = await SellAsync(fixture, Delivery(tacos, 4));
        var small = await SellAsync(fixture, Delivery(tacos, 1));

        Assert.True(free.IsSuccess, free.Error);
        Assert.Equal((480m, 0m), (free.Value.Total, free.Value.Delivery!.Fee));
        Assert.Equal(HomeDeliveryRules.BelowMinimumMessage("72000", 150m), small.Error);
        Assert.Single(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task PostalCodeNoActiveZoneReaches_ShouldBeRefused_WithoutSavingAnything()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        var norte = (await AddZoneAsync(fixture, "Norte", 45m, 0m, null, "72100")).Value;
        await new UpdateDeliveryZoneCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser).Handle(
            new UpdateDeliveryZoneCommand(norte.Id, norte.Revision, "Norte", 45m, 0m, null, ["72100"], false), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        // Another business reaches 72999: its zones are not this business's.
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "vecina");
        await AddZoneAsync(fixture, "Sur", 20m, 0m, null, "72999");
        fixture.TenantService.SetTenant(tenant.Id);

        var nowhere = await SellAsync(fixture, Delivery(tacos, 2, "72999"));
        var inactive = await SellAsync(fixture, Delivery(tacos, 2, "72100"));
        var notAPostalCode = await SellAsync(fixture, Delivery(tacos, 2, "7200"));

        Assert.Equal(HomeDeliveryRules.NotCoveredMessage("72999"), nowhere.Error);
        Assert.Equal(HomeDeliveryRules.NotCoveredMessage("72100"), inactive.Error);
        Assert.Equal(DeliveryZone.NotAPostalCodeMessage("7200"), notAPostalCode.Error);
        Assert.Empty(await fixture.Context.Orders.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task OrderPaidWhenTaken_ShouldStayOpen_UntilSomeoneMarksItDelivered()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        await fixture.OpenCashSessionAsync(tenant);

        var sale = await SellAsync(fixture, Delivery(tacos, 2) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 275m, Method = OrderPaymentMethod.Cash }]
        });

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal(0m, sale.Value.Balance);
        // A sale at the counter paid in full closes as delivered; this one still has to leave.
        Assert.NotEqual(OrderStatus.Delivered, sale.Value.Status);
        Assert.NotEmpty(sale.Value.AllowedTransitions);
        var counter = await SellAsync(fixture, new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 1 }],
            Payments = [new CreateOrderPaymentDto { Amount = 120m, Method = OrderPaymentMethod.Cash }]
        });
        Assert.Equal(OrderStatus.Delivered, counter.Value.Status);
    }

    [Fact]
    public void Validator_ShouldRefuse_ADeliveryOfAPlatform_ATab_ATable_AServiceOrAnOfflineSale()
    {
        var validator = new CreateOrderCommandValidator();
        var product = Guid.NewGuid();
        var delivery = Delivery(product);

        Assert.Contains(HomeDeliveryRules.NotForPlatformsMessage, Errors(validator.Validate(
            delivery with { Channel = OrderChannel.Rappi, ExternalOrderNumber = "152390", CustomerPhone = null })));
        Assert.Equal(HomeDeliveryRules.NotForTabsMessage, FirstError(validator.Validate(delivery with { OpenTab = true, DiningTableId = Guid.NewGuid() })));
        Assert.Equal(CreateOrderCommandValidator.DeliveryNoTableMessage, FirstError(validator.Validate(delivery with { TableNumber = "4" })));
        Assert.Equal(HomeDeliveryRules.OnlyProductsMessage, FirstError(validator.Validate(delivery with { OrderType = OrderType.Service })));
        Assert.Equal(CreateOrderCommandValidator.DeliveryWithConnectionMessage,
            FirstError(validator.Validate(delivery with { CapturedOfflineAt = SalesTestFixture.Now, IdempotencyKey = Guid.NewGuid() })));
        Assert.Equal(DeliveryAddress.AddressLineRequiredMessage,
            FirstError(validator.Validate(delivery with { Delivery = new OrderDeliveryInput { PostalCode = "72000", AddressLine = " " } })));
        Assert.Equal(DeliveryAddress.PostalCodeRequiredMessage,
            FirstError(validator.Validate(delivery with { Delivery = new OrderDeliveryInput { AddressLine = Address } })));
        Assert.Equal(DeliveryAddress.ReferencesTooLongMessage, FirstError(validator.Validate(delivery with
        {
            Delivery = new OrderDeliveryInput { PostalCode = "72000", AddressLine = Address, References = new string('a', 501) }
        })));
        Assert.True(validator.Validate(delivery).IsValid);
    }

    [Fact]
    public async Task CreatedOrder_ShouldTellItGoesToAnAddress_AndWhatItCharged_ButNeverTheAddress()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, tacos) = await KitchenAsync(fixture);

        var sale = await SellAsync(fixture, Delivery(tacos, 2));
        var created = Assert.Single(mediator.Published.OfType<OrderCreatedEvent>());
        await new OrderAuditHandlers(fixture.Context).HandleAsync(created, OutboxContext(tenant.Id), CancellationToken.None);
        await fixture.Context.SaveChangesAsync();

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal((true, 35m, 275m), (created.IsHomeDelivery, created.DeliveryFee, created.Total));
        var log = await fixture.Context.AuditLogs.SingleAsync(a => a.Action == "OrderCreated");
        var metadata = JsonDocument.Parse(log.Metadata!).RootElement;
        Assert.True(metadata.GetProperty("IsHomeDelivery").GetBoolean());
        Assert.Equal(35m, metadata.GetProperty("DeliveryFee").GetDecimal());
        Assert.DoesNotContain("Reforma", log.Metadata);
        Assert.DoesNotContain("72000", log.Metadata);

        // An order at the counter says it is not one.
        var counter = new OrderCreatedEvent(Guid.NewGuid(), tenant.Id, "ORD-2", BusinessType.Food, 120m);
        Assert.False(counter.IsHomeDelivery);
    }

    [Fact]
    public async Task OrderTakenInVender_ForTheCustomersAddress_ShouldMessageTheCustomer_WhoWaitsAtHome()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, tacos) = await KitchenAsync(fixture);
        var whatsApp = new RecordingWhatsApp();
        var notifications = new OrderCustomerNotifications(fixture.Context, whatsApp, NullLogger<OrderCustomerNotifications>.Instance);
        var context = OutboxContext(tenant.Id);

        // With the policy of omission (remote orders and services), a sale at the counter leaves with its ticket...
        var counter = await SellAsync(fixture, Delivery(tacos, 2) with { Delivery = null });
        Assert.True(counter.IsSuccess, counter.Error);
        await notifications.HandleAsync(Assert.Single(mediator.Published.OfType<OrderCreatedEvent>()), context, CancellationToken.None);
        Assert.Empty(whatsApp.Templates);

        // ...while the customer of an order sent to their address hears of it, like one of the online menu.
        mediator.Published.Clear();
        var sale = await SellAsync(fixture, Delivery(tacos, 2));
        Assert.True(sale.IsSuccess, sale.Error);
        await notifications.HandleAsync(Assert.Single(mediator.Published.OfType<OrderCreatedEvent>()), context, CancellationToken.None);
        Assert.Equal(["order_confirmed"], whatsApp.Templates);
    }

    [Fact]
    public async Task CfdiDraft_ShouldInvoiceTheFee_AsItsOwnConcept_ThatAddsUpToTheOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        tenant.SetBusinessInfo(
            "EKU9003173C9", "ESCUELA KEMPER URGATE", "Comercio", "626 - Régimen Simplificado de Confianza (RESICO)",
            new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
        await fixture.Context.SaveChangesAsync();
        var sale = await SellAsync(fixture, Delivery(tacos, 2));

        var draft = await new GetOrderCfdiDraftQueryHandler(fixture.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = sale.Value.Id }, CancellationToken.None);

        Assert.True(draft.IsSuccess, draft.Error);
        Assert.Equal((275m, 0m), (draft.Value.Total, draft.Value.Difference));
        Assert.Equal(sale.Value.Tax, draft.Value.TotalTransferred);
        var fee = Assert.Single(draft.Value.Concepts, concept => concept.IsDeliveryFee);
        Assert.Equal((SatCatalog.DeliveryFeeProductCode, SatCatalog.ServiceUnitCode, GetOrderCfdiDraftQueryHandler.DeliveryFeeDescription),
            (fee.ProductCode, fee.UnitCode, fee.Description));
        Assert.Equal((1m, "02", 0.160000m), (fee.Quantity, fee.TaxObject, fee.Transfer!.RateOrQuota));
        Assert.Equal(35m, fee.Amount + fee.Transfer.Amount);
        Assert.Contains(GetOrderCfdiDraftQueryHandler.DeliveryFeeWarning, draft.Value.Warnings);
        Assert.Single(draft.Value.Concepts, concept => !concept.IsDeliveryFee);
    }

    [Fact]
    public async Task CfdiDraft_OfPricesBeforeIva_ShouldInvoiceTheFeeAtItsRate_BesideAnExemptProduct()
    {
        await using var fixture = SalesTestFixture.Create();
        // Prices before IVA, tortillas exempt and tacos at 16 %: the fee goes at the business's 16 %.
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: false, slug: "demo");
        tenant.SetBusinessInfo(
            "EKU9003173C9", "ESCUELA KEMPER URGATE", "Comercio", "626 - Régimen Simplificado de Confianza (RESICO)",
            new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
        await fixture.Context.SaveChangesAsync();
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var tortillas = await fixture.AddProductAsync(tenant, "Tortillas", 25m, isTaxExempt: true);
        await AddZoneAsync(fixture, "Centro", 35m, 150m, null, "72000");
        var sale = await SellAsync(fixture, new CreateOrderCommand
        {
            CustomerName = "Ana",
            Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 2 }, new CreateOrderItemDto { ProductId = tortillas.Id, Quantity = 1 }],
            Delivery = new OrderDeliveryInput { PostalCode = "72000", AddressLine = Address }
        });
        Assert.True(sale.IsSuccess, sale.Error);
        // $240 + $35 at 16 % and $25 exempt: 275 × 1.16 + 25.
        Assert.Equal(344m, sale.Value.Total);

        var draft = await new GetOrderCfdiDraftQueryHandler(fixture.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = sale.Value.Id }, CancellationToken.None);

        Assert.True(draft.IsSuccess, draft.Error);
        Assert.Equal((344m, 0m, 44m), (draft.Value.Total, draft.Value.Difference, draft.Value.TotalTransferred));
        var fee = Assert.Single(draft.Value.Concepts, concept => concept.IsDeliveryFee);
        Assert.Equal((35m, 0.160000m, 5.60m), (fee.Amount, fee.Transfer!.RateOrQuota, fee.Transfer.Amount));
        Assert.Equal("Exento", Assert.Single(draft.Value.Concepts, concept => concept.Description == "Tortillas").Transfer!.FactorType);
    }

    [Fact]
    public async Task CourierShipment_ShouldBeWrittenDown_Corrected_AndRecorded_WithoutTheLink()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await KitchenAsync(fixture);
        var sale = await SellAsync(fixture, Delivery(tacos, 2));

        var written = await ShipAsync(fixture, sale.Value.Id, " #pq 1234 ", "https://paquetenvia.mx/rastreo/PQ1234");
        var again = await ShipAsync(fixture, sale.Value.Id, "PQ1234", "https://paquetenvia.mx/rastreo/PQ1234");
        var corrected = await ShipAsync(fixture, sale.Value.Id, "PQ1250", null);

        Assert.True(written.IsSuccess, written.Error);
        Assert.True(again.IsSuccess, again.Error);
        Assert.True(corrected.IsSuccess, corrected.Error);
        var delivery = (await fixture.Context.Orders.SingleAsync()).Delivery!;
        Assert.Equal((DeliveryCourier.Paquetenvia, "PQ1250", null, "cajero"),
            (delivery.Courier, delivery.CourierFolio, delivery.TrackingUrl, delivery.ShipmentRegisteredBy));
        // The same trip again records nothing; the correction keeps the folio it replaced.
        var logs = (await fixture.Context.AuditLogs.Where(a => a.Action == RegisterCourierShipmentCommandHandler.ShipmentRegistered).ToListAsync())
            .Select(log => JsonDocument.Parse(log.Metadata!).RootElement).ToList();
        Assert.Equal(2, logs.Count);
        var first = Assert.Single(logs, log => log.GetProperty("PreviousFolio").ValueKind == JsonValueKind.Null);
        Assert.Equal(("Paquetenvia", "PQ1234", true), (first.GetProperty("Courier").GetString(), first.GetProperty("Folio").GetString(), first.GetProperty("HasTrackingUrl").GetBoolean()));
        var correction = Assert.Single(logs, log => log.GetProperty("PreviousFolio").GetString() == "PQ1234");
        Assert.Equal(("PQ1250", false), (correction.GetProperty("Folio").GetString(), correction.GetProperty("HasTrackingUrl").GetBoolean()));
        Assert.All(logs, log => Assert.DoesNotContain("paquetenvia.mx", log.GetRawText()));
    }

    [Fact]
    public async Task CourierShipment_ShouldBeRefused_ForAnInsecureLink_AnOrderThatStays_OrOneOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        var delivery = await SellAsync(fixture, Delivery(tacos, 2));
        var counter = await SellAsync(fixture, new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 1 }] });

        var insecure = await ShipAsync(fixture, delivery.Value.Id, "PQ1234", "http://paquetenvia.mx/rastreo/PQ1234");
        var stays = await ShipAsync(fixture, counter.Value.Id, "PQ1234", null);

        Assert.Equal(CourierShipment.TrackingUrlMessage, insecure.Error);
        Assert.Equal(HomeDeliveryRules.NotADeliveryMessage, stays.Error);
        Assert.Null((await fixture.Context.Orders.SingleAsync(o => o.Id == delivery.Value.Id)).Delivery!.CourierFolio);
        Assert.Empty(await fixture.Context.AuditLogs.Where(a => a.Action == RegisterCourierShipmentCommandHandler.ShipmentRegistered).ToListAsync());

        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "vecina");
        await Assert.ThrowsAsync<EntityNotFoundException>(() => ShipAsync(fixture, delivery.Value.Id, "PQ1234", null));
        fixture.TenantService.SetTenant(tenant.Id);

        var validator = new RegisterCourierShipmentCommandValidator();
        Assert.Equal(CourierShipment.FolioRequiredMessage,
            validator.Validate(new RegisterCourierShipmentCommand(delivery.Value.Id, DeliveryCourier.Paquetenvia, " ", null)).Errors.First().ErrorMessage);
        Assert.Equal(CourierShipment.CourierMessage,
            validator.Validate(new RegisterCourierShipmentCommand(delivery.Value.Id, (DeliveryCourier)9, "PQ1", null)).Errors.First().ErrorMessage);
    }

    [Fact]
    public async Task OnlineMenu_ShouldTakeAnOrderToTheAddress_WithTheFeeInTheTotalTheCustomerSaw()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await KitchenAsync(fixture);
        await OpenOnlineMenuAsync(fixture, tenant);

        var productsOnly = await PlaceAsync(fixture, PublicOrder(tacos, 2) with { ExpectedTotal = 240m });
        var placed = await PlaceAsync(fixture, PublicOrder(tacos, 2) with { ExpectedTotal = 275m });

        Assert.True(productsOnly.IsFailure);
        Assert.Contains("$275.00", productsOnly.Error);
        Assert.True(placed.IsSuccess, placed.Error);
        Assert.Equal((275m, 35m), (placed.Value.Total, placed.Value.DeliveryFee));
        var order = await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((OrderChannel.Online, OrderStatus.Pending, "72000", Address),
            (order.Channel, order.Status, order.Delivery!.PostalCode, order.Delivery.AddressLine));
        Assert.Null(order.TableNumber);

        var notCovered = await PlaceAsync(fixture, PublicOrder(tacos, 2, "72999"));
        Assert.Equal(HomeDeliveryRules.NotCoveredMessage("72999"), notCovered.Error);
    }

    [Fact]
    public async Task OnlineMenu_OfAKitchenWithoutPremises_ShouldAskWhereTheOrderGoes_OnceItHasZones()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: "cocina");
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var branch = await fixture.DefaultBranchAsync();
        EntityFiller.Set(branch, nameof(branch.ServiceMode), BranchServiceMode.DeliveryOnly);
        await fixture.Context.SaveChangesAsync();
        await OpenOnlineMenuAsync(fixture, tenant);
        var pickup = PublicOrder(tacos, 2) with { Slug = "cocina", Delivery = null };

        // Without zones, orders arrive as before and the business arranges the delivery.
        Assert.True((await PlaceAsync(fixture, pickup)).IsSuccess);

        await AddZoneAsync(fixture, "Centro", 35m, 150m, 400m, "72000");
        var withoutAddress = await PlaceAsync(fixture, pickup);
        var toTheAddress = await PlaceAsync(fixture, PublicOrder(tacos, 2) with { Slug = "cocina" });

        Assert.Equal(PlacePublicOrderCommandHandler.DeliveryRequiredMessage, withoutAddress.Error);
        Assert.True(toTheAddress.IsSuccess, toTheAddress.Error);
        // Anyone can type a table into the link: only one of the business in use counts as a customer already there.
        var madeUpTable = await PlaceAsync(fixture, pickup with { TableLabel = "Mesa 9" });
        Assert.Equal(PlacePublicOrderCommandHandler.DeliveryRequiredMessage, madeUpTable.Error);
    }

    [Fact]
    public void OnlineMenuValidator_ShouldRefuse_AnOrderForATableSentToAnAddress()
    {
        var validator = new PlacePublicOrderCommandValidator();

        var result = validator.Validate(PublicOrder(Guid.NewGuid()) with { TableLabel = "Mesa 4" });

        Assert.Equal(PlacePublicOrderCommandHandler.TableNotDeliveredMessage, Assert.Single(result.Errors).ErrorMessage);
        Assert.True(validator.Validate(PublicOrder(Guid.NewGuid())).IsValid);
    }

    [Fact]
    public async Task Menu_ShouldOfferDelivery_OnlyWhileItTakesOrders_AndAZoneIsActive()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "demo");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.Orders);
        await fixture.OpenCashSessionAsync(tenant);

        Assert.False((await MenuAsync(fixture))!.DeliveryAvailable);

        await AddZoneAsync(fixture, "Centro", 35m, 150m, 400m, "72000");
        Assert.True((await MenuAsync(fixture))!.DeliveryAvailable);

        await SetModeAsync(fixture, tenant, OnlineMenuMode.MenuOnly);
        Assert.False((await MenuAsync(fixture))!.DeliveryAvailable);
    }

    [Fact]
    public async Task Quote_ShouldTellWhatADeliveryCosts_WithoutTheZoneName_AndNothingWithoutAMenu()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "demo");
        await AddZoneAsync(fixture, "Interna 1", 35m, 150m, 400m, "72000");
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "vecina");
        await AddZoneAsync(fixture, "Sur", 20m, 0m, null, "72999");

        Assert.Null(await QuoteAsync(fixture, "demo", "72000"));

        await SetModeAsync(fixture, tenant, OnlineMenuMode.MenuOnly);
        // Whatever business the request had in scope, the slug decides.
        fixture.TenantService.SetTenant(other.Id);
        var covered = await QuoteAsync(fixture, " DEMO ", " 72000 ");
        var notCovered = await QuoteAsync(fixture, "demo", "72999");

        Assert.Equal(("72000", true, 35m, 150m, 400m),
            (covered!.PostalCode, covered.Covered, covered.Fee, covered.MinimumOrder, covered.FreeDeliveryFrom));
        Assert.Equal(("72999", false, null, null, null),
            (notCovered!.PostalCode, notCovered.Covered, notCovered.Fee, notCovered.MinimumOrder, notCovered.FreeDeliveryFrom));
        Assert.DoesNotContain("Interna", JsonSerializer.Serialize(covered));

        var validator = new GetPublicDeliveryQuoteQueryValidator();
        Assert.False(validator.Validate(new GetPublicDeliveryQuoteQuery("demo", "7200")).IsValid);
        Assert.False(validator.Validate(new GetPublicDeliveryQuoteQuery("demo", " ")).IsValid);
        Assert.True(validator.Validate(new GetPublicDeliveryQuoteQuery("demo", " 72000 ")).IsValid);
    }

    /// <summary>A kitchen selling with IVA included: $120 tacos, and its zone Centro reaching 72000 for $35, from $150, free from $400.</summary>
    private static async Task<(Tenant Tenant, Product Tacos)> KitchenAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: "demo");
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var zone = await AddZoneAsync(fixture, "Centro", 35m, 150m, 400m, "72000");
        Assert.True(zone.IsSuccess, zone.Error);
        return (tenant, tacos);
    }

    private static async Task<Result<DeliveryZoneDto>> AddZoneAsync(
        SalesTestFixture fixture, string name, decimal fee, decimal minimum, decimal? freeFrom, params string[] postalCodes)
    {
        var result = await new CreateDeliveryZoneCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new CreateDeliveryZoneCommand(name, fee, minimum, freeFrom, postalCodes), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static CreateOrderCommand Delivery(Product product, decimal quantity = 2, string postalCode = "72000", string? references = null)
        => Delivery(product.Id, quantity, postalCode, references);

    private static CreateOrderCommand Delivery(Guid productId, decimal quantity = 2, string postalCode = "72000", string? references = null) => new()
    {
        CustomerName = "Ana",
        CustomerPhone = "55 1234 5678",
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = quantity }],
        Delivery = new OrderDeliveryInput { PostalCode = postalCode, AddressLine = $" {Address} ", References = references }
    };

    private static PlacePublicOrderCommand PublicOrder(Product product, decimal quantity = 2, string postalCode = "72000")
        => PublicOrder(product.Id, quantity, postalCode);

    private static PlacePublicOrderCommand PublicOrder(Guid productId, decimal quantity = 2, string postalCode = "72000") => new()
    {
        Slug = "demo",
        CustomerName = "Ana",
        CustomerPhone = "55 1234 5678",
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = quantity }],
        Delivery = new OrderDeliveryInput { PostalCode = postalCode, AddressLine = Address }
    };

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result<OrderDto>> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var result = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result> ShipAsync(SalesTestFixture fixture, Guid orderId, string folio, string? trackingUrl)
    {
        try
        {
            return await new RegisterCourierShipmentCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
                .Handle(new RegisterCourierShipmentCommand(orderId, DeliveryCourier.Paquetenvia, folio, trackingUrl), CancellationToken.None);
        }
        finally
        {
            fixture.Context.ChangeTracker.Clear();
        }
    }

    private static async Task OpenOnlineMenuAsync(SalesTestFixture fixture, Tenant tenant)
    {
        await SetModeAsync(fixture, tenant, OnlineMenuMode.Orders);
        await fixture.OpenCashSessionAsync(tenant, (await fixture.DefaultBranchAsync()).Id);
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task SetModeAsync(SalesTestFixture fixture, Tenant tenant, OnlineMenuMode mode)
    {
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(mode);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<Result<PublicOrderReceiptDto>> PlaceAsync(SalesTestFixture fixture, PlacePublicOrderCommand command)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        var result = await new PlacePublicOrderCommandHandler(
                fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance,
                new FolioGenerator(fixture.Context))
            .Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<PublicMenuDto?> MenuAsync(SalesTestFixture fixture)
        => new GetPublicMenuQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicMenuQuery("demo"), CancellationToken.None);

    private static Task<PublicDeliveryQuoteDto?> QuoteAsync(SalesTestFixture fixture, string slug, string postalCode)
        => new GetPublicDeliveryQuoteQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPublicDeliveryQuoteQuery(slug, postalCode), CancellationToken.None);

    private static string? FirstError(FluentValidation.Results.ValidationResult result) => result.Errors.FirstOrDefault()?.ErrorMessage;

    private static IEnumerable<string> Errors(FluentValidation.Results.ValidationResult result) => result.Errors.Select(error => error.ErrorMessage);

    private static OutboxEventContext OutboxContext(Guid tenantId)
        => new(Guid.NewGuid(), tenantId, "user-1", "cajero", SalesTestFixture.Now, 1);

    private sealed class RecordingMediator : IMediator
    {
        public List<DomainEvent> Published { get; } = [];

        public Task Publish(object notification, CancellationToken ct = default)
        {
            if (notification is DomainEvent domainEvent)
                Published.Add(domainEvent);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
            => Publish((object)notification!, ct);

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class RecordingWhatsApp : IWhatsAppService
    {
        public List<string> Templates { get; } = [];

        public Task<MessageSendResult> SendTemplateMessageAsync(
            Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
        {
            Templates.Add(templateName);
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Templates.Count}"));
        }

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Order notices are templates.");
    }
}
