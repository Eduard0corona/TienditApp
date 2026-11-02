using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The accepted quote becomes the job (Fase 29E, ADR 0053 point 17): «Convertir en orden de servicio» makes the business's
/// service order with the quote's customer, its lines at the accepted prices and its tax policy, once; what was paid for the
/// visit leaves the sale of the visit and becomes a payment of the order, without entering or leaving a drawer.
/// </summary>
public class QuoteConversionFlowTests
{
    [Fact]
    public async Task AnAcceptedQuote_ShouldBecomeTheBusinessesServiceOrder_AtTheAcceptedPrices()
    {
        await using var shop = await Shop.OpenAsync();
        // The catalog charges 1,500 for it today; the customer accepted 1,160.
        var heater = await shop.ProductAsync("Cambio de calentador", 1500m, trackable: true);
        var quote = await shop.AcceptedAsync(
            new QuoteLineRequest { ProductId = heater.Id, Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1160m },
            new QuoteLineRequest { Description = "Mano de obra", Quantity = 1m, UnitPrice = 800m },
            new QuoteLineRequest { Description = "Tubo de cobre", Quantity = 3.5m, UnitPrice = 99.99m });
        var promised = SalesTestFixture.Now.AddDays(3);

        var converted = await shop.ConvertAsync(quote.Id, promised);

        Assert.True(converted.IsSuccess, converted.Error);
        var order = await shop.Context.Orders
            .Include(o => o.Items).Include(o => o.ServiceDetail).Include(o => o.ServicePieces).Include(o => o.Payments)
            .SingleAsync();
        Assert.Equal((OrderType.Service, OrderStatus.Accepted, true, OrderChannel.Counter), (order.OrderType, order.Status, order.CreatedByBusiness, order.Channel));
        Assert.Equal(("Valeria Ruiz", "+525522107765", true), (order.CustomerName, order.CustomerPhone, order.PricesIncludeTax));
        Assert.Equal((quote.SubTotal, quote.Tax, quote.Total), (order.SubTotal, order.Tax, order.Total));
        Assert.Equal(2309.96m, order.Total);
        Assert.Empty(order.Payments);
        var lines = (await shop.Context.Quotes.Include(q => q.Lines).AsNoTracking().SingleAsync()).Lines.OrderBy(l => l.Position).ToList();
        Assert.Equal(
            [(heater.Id, "Cambio de calentador", 1m, 1160m), (lines[1].Id, "Mano de obra", 1m, 800m), (lines[2].Id, "Tubo de cobre (3.5)", 1m, 349.96m)],
            order.Items.Select(i => (i.ProductId, i.ProductName, i.Quantity, i.UnitPrice)));
        Assert.Equal(SatCatalog.ServiceUnitCode, order.Items.Single(i => i.ProductName == "Mano de obra").SatUnitCode);
        // As any service order of the counter: when it is promised, where it comes from, and a piece of the trackable service.
        Assert.Equal((promised, "De la cotización COT-000001 (versión 1)."), (order.ServiceDetail!.PromisedAt, order.ServiceDetail.IntakeNotes));
        Assert.Equal(heater.Id, Assert.Single(order.ServicePieces).ServiceProductId);
        Assert.False(string.IsNullOrEmpty(order.OrderNumber));

        var detail = converted.Value!;
        Assert.Equal((order.Id, order.OrderNumber, OrderStatus.Accepted), (detail.Order!.Id, detail.Order.OrderNumber, detail.Order.Status));
        Assert.Equal((false, (DateTimeOffset?)SalesTestFixture.Now), (detail.CanConvertToOrder, detail.ConvertedAt));
        // The activity keeps the folio, the version, the total and the order, never the customer.
        var audit = await shop.Context.AuditLogs.SingleAsync(a => a.Action == QuoteAudit.Converted);
        Assert.Contains(order.OrderNumber, audit.Metadata);
        Assert.DoesNotContain("Valeria", audit.Metadata);
        Assert.DoesNotContain("5522107765", audit.Metadata);
        // The order's detail opens its quote.
        var orderDetail = await new GetOrderByIdQueryHandler(shop.Context).Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);
        Assert.Equal((quote.Id, quote.Number, 1), (orderDetail.Quote!.Id, orderDetail.Quote.Number, orderDetail.Quote.Revision));
        // And so does the list of orders, where the detail of an order opens.
        var listed = Assert.Single((await new GetOrdersByBusinessQueryHandler(shop.Context, shop.Fixture.TenantService)
            .Handle(new GetOrdersByBusinessQuery(), CancellationToken.None)).Items);
        Assert.Equal(quote.Id, listed.Quote!.Id);
    }

    [Fact]
    public async Task TheOrder_ShouldKeepTheQuotesTaxPolicy_AfterTheBusinessChangedIts()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1160m });
        var settings = await shop.Context.TenantSettings.SingleAsync();
        settings.UpdateSalesPolicy(pricesIncludeTax: false, defaultTaxRate: 16m, autoCloseCounterSales: true);
        await shop.Context.SaveChangesAsync();

        Assert.True((await shop.ConvertAsync(quote.Id)).IsSuccess);

        var order = await shop.Context.Orders.SingleAsync();
        Assert.Equal((true, 1160m, 160m), (order.PricesIncludeTax, order.Total, order.Tax));
    }

    [Fact]
    public async Task OnlyAnAcceptedQuote_ShouldBecomeAnOrder_AndOnce()
    {
        await using var shop = await Shop.OpenAsync();
        var sent = await shop.SentAsync(new QuoteLineRequest { Description = "Revisión", Quantity = 1m, UnitPrice = 500m });
        Assert.Equal(QuoteRules.NotAcceptedMessage, (await shop.ConvertAsync(sent.Id)).Error);
        Assert.True(sent.CanConvertToOrder is false);

        var accepted = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1160m });
        Assert.True(accepted.CanConvertToOrder);
        Assert.True((await shop.ConvertAsync(accepted.Id)).IsSuccess);
        shop.Context.ChangeTracker.Clear();

        Assert.Equal(QuoteRules.AlreadyConvertedMessage, (await shop.ConvertAsync(accepted.Id)).Error);
        Assert.Equal(1, await shop.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task ABusinessWithoutServiceOrders_ShouldNotConvertIt()
    {
        // A shop that schedules visits and sells products, without service orders.
        await using var shop = await Shop.OpenAsync(TenantCapability.Retail | TenantCapability.Appointments);
        var quote = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1160m });

        Assert.False(quote.CanConvertToOrder);
        Assert.Equal(QuoteRules.NoServiceOrdersMessage, (await shop.ConvertAsync(quote.Id)).Error);
        Assert.Equal(0, await shop.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task WhatWasPaidForTheVisit_ShouldBecomeAPaymentOfTheOrder_WithoutEnteringOrLeavingADrawer()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (sale, quote) = await shop.CreditedVisitQuoteAsync();
        var drawerBefore = await shop.Context.CashTransactions.CountAsync();
        Assert.Equal((1160m, 440.80m, 719.20m), (quote.Total, quote.VisitCredit!.Amount, quote.ToPay));

        var converted = await shop.ConvertAsync(quote.Id);

        Assert.True(converted.IsSuccess, converted.Error);
        var order = await shop.Context.Orders.Include(o => o.Payments).AsNoTracking().SingleAsync(o => o.Id != sale.Id);
        var payment = Assert.Single(order.Payments);
        Assert.Equal((OrderPaymentMethod.CustomerBalance, 440.80m), (payment.Method, payment.Amount));
        Assert.Equal((1160m, 719.20m), (order.Total, order.Balance));
        // The sale of the visit gave it back to the balance of its phone, which the order spent at once.
        var visitSale = await shop.Context.Orders.Include(o => o.Refunds).AsNoTracking().SingleAsync(o => o.Id == sale.Id);
        var refund = Assert.Single(visitSale.Refunds);
        Assert.Equal((OrderPaymentMethod.CustomerBalance, 440.80m, 0m), (refund.Method, refund.Amount, visitSale.NetPaidAmount));
        Assert.Contains(order.OrderNumber, refund.Reason);
        var entries = await shop.Context.CustomerBalanceEntries.AsNoTracking().OrderBy(e => e.Kind).ToListAsync();
        Assert.Equal([(CustomerBalanceEntryKind.Refund, 440.80m), (CustomerBalanceEntryKind.Applied, -440.80m)], entries.Select(e => (e.Kind, e.Amount)));
        Assert.Equal(0m, entries.Sum(e => e.Amount));
        Assert.Equal(drawerBefore, await shop.Context.CashTransactions.CountAsync());
        // The quote still takes it off, although its sale has nothing paid for it now.
        var detail = await shop.GetAsync(quote.Id);
        Assert.Equal((440.80m, true, 719.20m), (detail.VisitCredit!.Amount, detail.VisitCredit.Applies, detail.ToPay));
        Assert.Equal(440.80m, (await shop.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == quote.Id)).AppliedVisitCredit);
    }

    [Fact]
    public async Task AVisitThatCostMoreThanTheJob_ShouldPayTheJobWhole_AndLeaveTheRestInItsSale()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        // The visit paid 440.80; the job is 250 plus IVA, 290.
        var (sale, quote) = await shop.CreditedVisitQuoteAsync(jobPrice: 250m);
        Assert.Equal((290m, 0m), (quote.Total, quote.ToPay));

        var converted = await shop.ConvertAsync(quote.Id);

        Assert.True(converted.IsSuccess, converted.Error);
        var order = await shop.Context.Orders.Include(o => o.Payments).AsNoTracking().SingleAsync(o => o.Id != sale.Id);
        Assert.Equal((290m, 290m, 0m), (Assert.Single(order.Payments).Amount, order.Total, order.Balance));
        var visitSale = await shop.Context.Orders.Include(o => o.Refunds).AsNoTracking().SingleAsync(o => o.Id == sale.Id);
        Assert.Equal((290m, 150.80m), (Assert.Single(visitSale.Refunds).Amount, visitSale.NetPaidAmount));
        Assert.Equal(290m, (await shop.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == quote.Id)).AppliedVisitCredit);
    }

    [Fact]
    public async Task ApplyingTheVisit_ShouldNeedWhoMayChargeAndRefund_AndConvertingWithoutIt_ShouldNot()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (_, credited) = await shop.CreditedVisitQuoteAsync();
        var plain = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Revisión", Quantity = 1m, UnitPrice = 500m });
        shop.Permissions = [nameof(TenantPermission.ManageOrders)];

        var refused = await Assert.ThrowsAsync<ForbiddenAccessException>(() => shop.ConvertAsync(credited.Id));

        Assert.Equal(QuoteConversions.ChargeToApplyVisitMessage, refused.Message);
        var withoutVisit = await shop.ConvertAsync(plain.Id);
        Assert.True(withoutVisit.IsSuccess, withoutVisit.Error);
        // Charging is not enough: the sale of the visit gives the money back, which is a refund; nor is refunding: the order is
        // paid with it, which is charging.
        shop.Permissions = [nameof(TenantPermission.ManageOrders), nameof(TenantPermission.ChargeOrders)];
        shop.Context.ChangeTracker.Clear();
        Assert.Equal(QuoteConversions.ChargeToApplyVisitMessage,
            (await Assert.ThrowsAsync<ForbiddenAccessException>(() => shop.ConvertAsync(credited.Id))).Message);
        shop.Permissions = [nameof(TenantPermission.ManageOrders), nameof(TenantPermission.ProcessRefunds)];
        shop.Context.ChangeTracker.Clear();
        Assert.Equal(QuoteConversions.ChargeToApplyVisitMessage,
            (await Assert.ThrowsAsync<ForbiddenAccessException>(() => shop.ConvertAsync(credited.Id))).Message);
        shop.Permissions = [nameof(TenantPermission.ManageOrders), nameof(TenantPermission.ChargeOrders), nameof(TenantPermission.ProcessRefunds)];
        shop.Context.ChangeTracker.Clear();
        Assert.True((await shop.ConvertAsync(credited.Id)).IsSuccess);
    }

    [Fact]
    public async Task AVisitWhoseSaleWasCancelled_ShouldApplyNothing()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (sale, quote) = await shop.CreditedVisitQuoteAsync();
        var visitSale = await shop.Context.Orders.SingleAsync(o => o.Id == sale.Id);
        shop.Context.Entry(visitSale).Property(o => o.Status).CurrentValue = OrderStatus.Cancelled;
        await shop.Context.SaveChangesAsync();
        shop.Context.ChangeTracker.Clear();

        Assert.True((await shop.ConvertAsync(quote.Id)).IsSuccess);

        var order = await shop.Context.Orders.Include(o => o.Payments).AsNoTracking().SingleAsync(o => o.Id != sale.Id);
        Assert.Empty(order.Payments);
        Assert.Empty(await shop.Context.CustomerBalanceEntries.ToListAsync());
        Assert.Null((await shop.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == quote.Id)).AppliedVisitCredit);
    }

    [Fact]
    public async Task AVisitChargedWithoutAPhone_ShouldNotApply_AndTheQuoteShouldWaitAsItWas()
    {
        // Charged to «Público en general»: its money has no account to go through.
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (_, quote) = await shop.CreditedVisitQuoteAsync(salePhone: null);

        var converted = await shop.ConvertAsync(quote.Id);

        Assert.Equal(QuoteConversions.VisitNotAppliedMessage(OrderPaymentMethodRules.CustomerBalanceNeedsPhoneMessage), converted.Error);
        shop.Context.ChangeTracker.Clear();
        Assert.Equal(1, await shop.Context.Orders.CountAsync());
        Assert.Null((await shop.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == quote.Id)).OrderId);
    }

    [Fact]
    public async Task AProductNotSoldInFractions_ShouldBeQuotedWhole()
    {
        await using var shop = await Shop.OpenAsync();
        var heater = await shop.ProductAsync("Cambio de calentador", 1500m);
        var hours = await shop.ProductAsync("Hora de técnico", 300m, unit: UnitType.Hour);

        var fractional = await shop.SaveDraftAsync(
            new QuoteLineRequest { ProductId = heater.Id, Description = "Cambio de calentador", Quantity = 1.5m, UnitPrice = 1160m });
        var byTheHour = await shop.SaveDraftAsync(
            new QuoteLineRequest { ProductId = hours.Id, Description = "Hora de técnico", Quantity = 2.5m, UnitPrice = 300m });

        Assert.Equal(QuoteRules.WholeQuantityMessage("Cambio de calentador"), fractional.Error);
        Assert.True(byTheHour.IsSuccess, byTheHour.Error);
    }

    [Fact]
    public async Task ADraftWhoseOrderWouldGiveMorePiecesThanAnOrderGenerates_ShouldBeRefused()
    {
        await using var shop = await Shop.OpenAsync();
        var uniform = await shop.ProductAsync("Lavado de uniforme", 45m, trackable: true);
        var ironing = await shop.ProductAsync("Planchado", 20m);

        // A piece per unit of a trackable service, across its lines; a service without tracking gives none.
        var past = await shop.SaveDraftAsync(
            new QuoteLineRequest { ProductId = uniform.Id, Description = "Lavado de uniforme", Quantity = 300m, UnitPrice = 45m },
            new QuoteLineRequest { ProductId = uniform.Id, Description = "Lavado de camisas", Quantity = 201m, UnitPrice = 45m });
        var upToIt = await shop.SaveDraftAsync(
            new QuoteLineRequest { ProductId = uniform.Id, Description = "Lavado de uniforme", Quantity = 500m, UnitPrice = 45m },
            new QuoteLineRequest { ProductId = ironing.Id, Description = "Planchado", Quantity = 600m, UnitPrice = 20m });

        Assert.Equal(QuoteConversions.TooManyPiecesMessage(501), past.Error);
        Assert.True(upToIt.IsSuccess, upToIt.Error);
    }

    [Theory]
    [InlineData(500, 500, "De la cotización COT-000001 (versión 1).")]
    [InlineData(600, 0, "De la cotización COT-000001 (versión 1). Sin piezas: serían 600 y una orden genera como máximo 500.")]
    public async Task AServiceTrackedAfterTheQuoteWasAccepted_ShouldGiveItsPieces_UpToWhatAnOrderGenerates_AndNeverStickTheQuote(
        int quantity, int pieces, string intakeNote)
    {
        await using var shop = await Shop.OpenAsync();
        var uniform = await shop.ProductAsync("Lavado de uniforme", 45m);
        var quote = await shop.AcceptedAsync(
            new QuoteLineRequest { ProductId = uniform.Id, Description = "Lavado de uniforme", Quantity = quantity, UnitPrice = 45m });
        // The business tracks the service once the customer accepted: an accepted quote is no longer corrected.
        var tracked = await shop.Context.Products.SingleAsync(p => p.Id == uniform.Id);
        shop.Context.Entry(tracked).Property(p => p.IsTrackable).CurrentValue = true;
        await shop.Context.SaveChangesAsync();

        var converted = await shop.ConvertAsync(quote.Id);

        Assert.True(converted.IsSuccess, converted.Error);
        var order = await shop.Context.Orders.Include(o => o.ServiceDetail).Include(o => o.ServicePieces).SingleAsync();
        Assert.Equal((pieces, intakeNote, quote.Total), (order.ServicePieces.Count, order.ServiceDetail!.IntakeNotes, order.Total));
    }

    [Fact]
    public async Task AQuoteWhosePhoneDoesNotRead_ShouldStillBecomeAnOrder()
    {
        await using var shop = await Shop.OpenAsync();
        // The quote only asks that the phone is written; accepted, it is no longer corrected.
        var quote = await shop.AcceptedForAsync("555-1234", new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1160m });

        var converted = await shop.ConvertAsync(quote.Id);

        Assert.True(converted.IsSuccess, converted.Error);
        var order = await shop.Context.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(("Valeria Ruiz", (string?)null, 1160m), (order.CustomerName, order.CustomerPhone, order.Total));
    }

    [Fact]
    public async Task AQuoteWhosePhoneDoesNotRead_ShouldTakeThePhoneOfTheSaleOfItsVisit()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (sale, quote) = await shop.CreditedVisitQuoteAsync(quotePhone: "555-1234");

        var converted = await shop.ConvertAsync(quote.Id);

        Assert.True(converted.IsSuccess, converted.Error);
        var order = await shop.Context.Orders.Include(o => o.Payments).AsNoTracking().SingleAsync(o => o.Id != sale.Id);
        Assert.Equal(("+525522107765", 440.80m), (order.CustomerPhone, Assert.Single(order.Payments).Amount));
    }

    [Fact]
    public async Task AQuoteOfAnotherBusiness_ShouldNotBeFound()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1160m });
        var other = await shop.Fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "otro");
        shop.Fixture.TenantService.SetTenant(other.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => shop.ConvertAsync(quote.Id));
        Assert.Equal(0, await shop.Context.Orders.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void APromiseAYearAway_ShouldBeAccepted_ButNotAMinuteLater_AndNoPromiseAtAll_Too()
    {
        var aYearAway = SalesTestFixture.Now.AddYears(1);

        Assert.True(PromiseFor(aYearAway).IsValid);
        Assert.Contains(PromiseFor(aYearAway.AddMinutes(1)).Errors, e => e.ErrorMessage == QuoteConversions.PromisedAtMessage);
        Assert.True(PromiseFor(null).IsValid);
    }

    [Theory]
    // Now is 11:00 in Mexico City (UTC−6): its today began at 06:00 UTC, and 05:59 is yesterday there.
    [InlineData("America/Mexico_City", "2026-10-04T06:00:00Z", true)]
    [InlineData("America/Mexico_City", "2026-10-04T05:59:00Z", false)]
    // And 02:00 of the 5th in Tokyo (UTC+9): its today began at 15:00 UTC, two hours ago, and that still counts.
    [InlineData("Asia/Tokyo", "2026-10-04T15:00:00Z", true)]
    [InlineData("Asia/Tokyo", "2026-10-04T14:59:00Z", false)]
    public async Task APromise_ShouldGoFromTheStartOfToday_WhereTheBusinessLivesItsDays(string timeZone, string promisedAt, bool accepted)
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Mano de obra", Quantity = 1m, UnitPrice = 800m });
        (await shop.Context.TenantSettings.SingleAsync()).SetTimeZone(timeZone);
        await shop.Context.SaveChangesAsync();

        var converted = await shop.ConvertAsync(quote.Id, DateTimeOffset.Parse(promisedAt, System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(converted.IsSuccess == accepted, converted.Error);
        if (!accepted)
        {
            Assert.Equal(QuoteConversions.PromisedAtMessage, converted.Error);
            Assert.Equal(0, await shop.Context.Orders.CountAsync());
        }
    }

    private static FluentValidation.Results.ValidationResult PromiseFor(DateTimeOffset? promisedAt)
        => new ConvertQuoteToOrderCommandValidator(new SalesClock()).Validate(new ConvertQuoteToOrderCommand(Guid.NewGuid(), promisedAt));

    private sealed class Access(Func<IReadOnlyCollection<string>> permissions) : ITenantAccessService
    {
        private const TenantCapability Modules = TenantCapability.Service | TenantCapability.Appointments;

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, Modules, permissions(), IsManager: false));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken)
            => Task.FromResult((capability & Modules) == capability);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken)
            => Task.FromResult((capabilities & Modules) != 0);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(permissions());

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(permissions().Contains(permission.ToString()));
    }

    private sealed class Shop : IAsyncDisposable
    {
        private Shop(SalesTestFixture fixture, Tenant tenant)
        {
            Fixture = fixture;
            Tenant = tenant;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        public SalesDbContext Context => Fixture.Context;
        /// <summary>What the person converting may do; everything an owner may, unless a test says.</summary>
        public IReadOnlyCollection<string> Permissions { get; set; } = Enum.GetNames<TenantPermission>();
        private QuotesHandler Handler => new(Context, Fixture.TenantService, Fixture.CurrentUser, Fixture.Clock);

        public static async Task<Shop> OpenAsync(
            TenantCapability capabilities = TenantCapability.Service | TenantCapability.Appointments, bool pricesIncludeTax = true,
            HomeVisitChargePolicy policy = HomeVisitChargePolicy.Charged)
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Service, capabilities, pricesIncludeTax: pricesIncludeTax, slug: "plomero");
            (await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id)).UpdateHomeVisitRequests(true, null, policy);
            fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 80m, 0m, null, ["72000"])));
            await fixture.Context.SaveChangesAsync();
            return new Shop(fixture, tenant);
        }

        public async Task<Product> ProductAsync(string name, decimal price, bool trackable = false, UnitType unit = UnitType.Service)
        {
            var category = new Category(Tenant.Id, "Servicios", null);
            var product = new Product(Tenant.Id, category.Id, name, null, price, "MXN", unit: unit, isTrackable: trackable);
            Context.AddRange(category, product);
            await Context.SaveChangesAsync();
            return product;
        }

        public async Task<QuoteDto> SentAsync(params QuoteLineRequest[] lines)
        {
            var created = await new CreateQuoteCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, new FolioGenerator(Context), Fixture.Clock)
                .Handle(new CreateQuoteCommand(null, null, "Valeria Ruiz", "5522107765", "valeria@correo.mx"), CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return await SendAsync(created.Value!, [.. lines]);
        }

        public async Task<QuoteDto> AcceptedAsync(params QuoteLineRequest[] lines)
            => await AcceptAsync(await SentAsync(lines));

        /// <summary>An accepted quote for Valeria with the phone as written.</summary>
        public async Task<QuoteDto> AcceptedForAsync(string phone, params QuoteLineRequest[] lines)
        {
            var created = await new CreateQuoteCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, new FolioGenerator(Context), Fixture.Clock)
                .Handle(new CreateQuoteCommand(null, null, "Valeria Ruiz", phone, null), CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return await AcceptAsync(await SendAsync(created.Value!, [.. lines]));
        }

        /// <summary>
        /// A visit «Se descuenta si contratas» charged with its service (300 plus IVA) and the zone's 80 plus IVA, paid 440.80 in
        /// cash, and its quote for 1,000 plus IVA, accepted.
        /// </summary>
        /// <param name="salePhone">The phone the sale of the visit was charged with: the visit's customer's, as the Agenda fills it.</param>
        /// <param name="jobPrice">The price of the job quoted, plus IVA.</param>
        /// <param name="quotePhone">The phone the quote is corrected to before it is sent; the visit's, unless said.</param>
        public async Task<(OrderDto Sale, QuoteDto Quote)> CreditedVisitQuoteAsync(
            string? salePhone = "5522107765", decimal jobPrice = 1000m, string? quotePhone = null)
        {
            var service = await Fixture.AddProductAsync(Tenant, "Visita y diagnóstico", 300m, unit: UnitType.Service);
            var visit = await VisitAsync(service.Id);
            if (!await Context.CashSessions.AnyAsync())
                await Fixture.OpenCashSessionAsync(Tenant);
            // Charged from the Agenda, which fills in the visit's customer.
            var sale = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                OrderType = OrderType.Service,
                CustomerName = "Valeria Ruiz",
                CustomerPhone = salePhone,
                Items = [new CreateOrderItemDto { ProductId = service.Id, Quantity = 1 }],
                AppointmentId = visit.Id,
                Payments = [new CreateOrderPaymentDto { Amount = 440.80m, Method = OrderPaymentMethod.Cash }]
            }, CancellationToken.None);
            Assert.True(sale.IsSuccess, sale.Error);
            Context.ChangeTracker.Clear();

            var created = await new CreateQuoteCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, new FolioGenerator(Context), Fixture.Clock)
                .Handle(new CreateQuoteCommand(visit.Id, null, null, null, null), CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            var draft = created.Value!;
            if (quotePhone is not null)
                draft.CustomerPhone = quotePhone;
            var sent = await SendAsync(draft, [new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = jobPrice }]);
            return (sale.Value!, await AcceptAsync(sent));
        }

        /// <summary>A new draft for Valeria, saved with <paramref name="lines"/>: what the server answered.</summary>
        public async Task<Result<QuoteDto>> SaveDraftAsync(params QuoteLineRequest[] lines)
        {
            var created = await new CreateQuoteCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, new FolioGenerator(Context), Fixture.Clock)
                .Handle(new CreateQuoteCommand(null, null, "Valeria Ruiz", "5522107765", null), CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return await Handler.Handle(new UpdateQuoteDraftCommand(created.Value!.Id, null, "Valeria Ruiz", "5522107765", null, 15, null,
                [.. lines]), CancellationToken.None);
        }

        public Task<Result<QuoteDto>> ConvertAsync(Guid id, DateTimeOffset? promisedAt = null)
        {
            var phones = new PhoneNormalizationService();
            return new ConvertQuoteToOrderCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser,
                    new CurrentAccessContext(Fixture.CurrentUser, Fixture.TenantService, new Access(() => Permissions)),
                    new CustomerLookupService(Context, phones, NullLogger<CustomerLookupService>.Instance), phones,
                    new OrderInventoryService(Context, Fixture.Clock, new FolioGenerator(Context), NullLogger<OrderInventoryService>.Instance),
                    new OrderCashService(Context, Fixture.CurrentUser), new FolioGenerator(Context), Fixture.Clock)
                .Handle(new ConvertQuoteToOrderCommand(id, promisedAt), CancellationToken.None);
        }

        public Task<QuoteDto> GetAsync(Guid id) => Handler.Handle(new GetQuoteQuery(id), CancellationToken.None);

        private async Task<QuoteDto> SendAsync(QuoteDto draft, List<QuoteLineRequest> lines)
        {
            var updated = await Handler.Handle(new UpdateQuoteDraftCommand(draft.Id, draft.CustomerId, draft.CustomerName, draft.CustomerPhone,
                draft.CustomerEmail, 15, null, lines), CancellationToken.None);
            Assert.True(updated.IsSuccess, updated.Error);
            var sent = await Handler.Handle(new SendQuoteCommand(draft.Id), CancellationToken.None);
            Assert.True(sent.IsSuccess, sent.Error);
            return sent.Value!;
        }

        private async Task<QuoteDto> AcceptAsync(QuoteDto sent)
        {
            var accepted = await Handler.Handle(new RecordQuoteReplyCommand(sent.Id, true, QuoteReplyChannel.WhatsApp, "Adelante"), CancellationToken.None);
            Assert.True(accepted.IsSuccess, accepted.Error);
            Context.ChangeTracker.Clear();
            return accepted.Value!;
        }

        /// <summary>A visit the team scheduled at 72000 for Valeria tomorrow, with the service.</summary>
        private async Task<AppointmentDto> VisitAsync(Guid serviceId)
        {
            var startAt = SalesTestFixture.Now.AddDays(1);
            var created = await new CreateAppointmentCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Fixture.Clock,
                    new AppointmentNotices(Context, new PhoneNormalizationService(), Fixture.Clock))
                .Handle(new CreateAppointmentCommand
                {
                    CustomerName = "Valeria Ruiz",
                    CustomerPhone = "5522107765",
                    ServiceProductId = serviceId,
                    StartAt = startAt,
                    EndAt = startAt.AddHours(1),
                    Visit = new AppointmentVisitInput { PostalCode = "72000", AddressLine = "Av. Juárez 123, Col. Centro" }
                }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value!;
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
