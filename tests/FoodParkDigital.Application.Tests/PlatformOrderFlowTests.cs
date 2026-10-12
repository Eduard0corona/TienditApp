using System.Text.Json;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Registering orders of Rappi, Uber Eats and DiDi Food from Vender (Fase 22F): with the folio of the app, the terms of
/// the platform copied into the order and, when the platform charged, its «Plataforma» payment outside every drawer.
/// </summary>
public class PlatformOrderFlowTests
{
    [Fact]
    public async Task OrderThePlatformCharged_ShouldBePaid_WithoutADrawerOrChargeOrders_AndKeepItsAmounts()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi, commissionPercent: 30m);
        var branchLocation = await fixture.Context.InventoryLocations.Select(l => l.Id).SingleAsync();
        var command = SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "  #bc 953 ", pack.Id, bagName: " Juan ") with
        {
            ExpectedTotal = 1000m
        };

        // Nobody charges it: the server records the platform's payment, so taking orders is enough.
        Assert.Empty(command.RequiredPermissions());
        var result = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var order = result.Value;
        Assert.Equal(OrderChannel.Rappi, order.Channel);
        Assert.Equal("BC953", order.PlatformOrder!.ExternalOrderNumber);
        Assert.Equal(PlatformCollectedBy.Platform, order.PlatformOrder.CollectedBy);
        Assert.Equal("Juan", order.CustomerName);
        Assert.Null(order.CustomerPhone);
        Assert.Null(order.TableNumber);
        Assert.Null(order.CustomerUserId);
        Assert.Null(order.TemporaryCustomerId);
        Assert.Equal((1000m, 1000m, 0m), (order.Total, order.PaidAmount, order.Balance));
        // Paid and without a kitchen screen: handed over to the courier, as a paid counter sale closes.
        Assert.Equal(OrderStatus.Delivered, order.Status);
        var payment = Assert.Single(order.Payments);
        Assert.Equal(OrderPaymentMethod.Platform, payment.Method);
        Assert.Equal(1000m, payment.Amount);
        Assert.Equal("BC953", payment.Reference);
        Assert.Equal(SalesTestFixture.Now, payment.PaidAt);
        Assert.Equal(0m, payment.Tip);
        Assert.Equal(branchLocation, payment.InventoryLocationId);
        Assert.Empty(await fixture.Context.CashTransactions.ToListAsync());

        // The terms of Rappi then and what they give: DiDi's published example ($1,000 with IVA included).
        var detail = await fixture.Context.PlatformOrderDetails.AsNoTracking().SingleAsync();
        Assert.Equal(order.Id, detail.OrderId);
        Assert.Equal(tenant.Id, detail.TenantId);
        Assert.Equal((OrderChannel.Rappi, "BC953", 1), (detail.Channel, detail.ExternalOrderNumber, detail.SettingsRevision));
        Assert.Equal((30m, PlatformCommissionBase.TotalWithTax, 16m, 2.5m, 50m),
            (detail.CommissionPercent, detail.CommissionBase, detail.CommissionVatPercent, detail.IsrWithholdingPercent, detail.VatWithholdingPercent));
        Assert.Equal((1000m, 300m, 48m, 21.55m, 68.97m, 561.48m),
            (detail.OrderTotal, detail.Commission, detail.CommissionTax, detail.IsrWithheld, detail.VatWithheld, detail.ExpectedSettlement));
        Assert.Equal(SalesTestFixture.Now, detail.RegisteredAt);
    }

    [Fact]
    public async Task OrderTheBusinessCollects_ShouldBeChargedThroughTheDrawer_AndNothingWithheld()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood, commissionPercent: 30m);
        var command = SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", tacos.Id, collectedBy: PlatformCollectedBy.Business) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 245m, Method = OrderPaymentMethod.Cash, CashReceived = 300m }]
        };

        // Money the courier hands over is the business's: it needs a drawer, like any other charge.
        var withoutDrawer = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(OrderPaymentRegistrar.NoOpenDrawerMessage, withoutDrawer.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
        Assert.Empty(await fixture.Context.PlatformOrderDetails.ToListAsync());

        var drawer = await fixture.OpenCashSessionAsync(tenant);
        var result = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PlatformCollectedBy.Business, result.Value.PlatformOrder!.CollectedBy);
        Assert.Equal(OrderStatus.Delivered, result.Value.Status);
        var payment = Assert.Single(result.Value.Payments);
        Assert.Equal((OrderPaymentMethod.Cash, 245m, 55m), (payment.Method, payment.Amount, payment.Change));
        var movement = Assert.Single(await fixture.Context.CashTransactions.Where(t => t.CashSessionId == drawer.Id).ToListAsync());
        Assert.Equal((CashTransactionType.SalePayment, 245m), (movement.TransactionType, movement.Amount));
        // The platform withholds nothing of what it did not charge; it will discount its commission and IVA.
        var detail = await fixture.Context.PlatformOrderDetails.AsNoTracking().SingleAsync();
        Assert.Equal((73.50m, 11.76m, 0m, 0m, -85.26m),
            (detail.Commission, detail.CommissionTax, detail.IsrWithheld, detail.VatWithheld, detail.ExpectedSettlement));
    }

    [Fact]
    public async Task OrderTheBusinessCollects_SavedUnpaid_ShouldBeChargedLater_WithoutATip()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);

        var saved = await fixture.CreateOrderHandler().Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", tacos.Id, collectedBy: PlatformCollectedBy.Business),
            CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal((OrderStatus.Confirmed, 245m), (saved.Value.Status, saved.Value.Balance));
        Assert.Empty(saved.Value.Payments);

        // The courier pays on pickup: the cashier charges it from Pedidos (correction 1: the charge never needs the
        // platform's detail loaded). The tip in the app is the courier's.
        await fixture.OpenCashSessionAsync(tenant);
        var withTip = new AddOrderPaymentCommand { OrderId = saved.Value.Id, Amount = 245m, Method = OrderPaymentMethod.Cash, Tip = 20m };
        var tip = await Assert.ThrowsAsync<DomainException>(() => fixture.AddPaymentHandler().Handle(withTip, CancellationToken.None));
        fixture.Context.ChangeTracker.Clear();
        var charged = await fixture.AddPaymentHandler().Handle(withTip with { Tip = 0m }, CancellationToken.None);

        Assert.Equal(PlatformOrderRules.NoTipMessage, tip.Message);
        Assert.True(charged.IsSuccess, charged.Error);
        Assert.Equal((OrderStatus.Delivered, 0m), (charged.Value.Status, charged.Value.Balance));
        // The answer keeps the folio (the reload includes the detail).
        Assert.Equal("12", charged.Value.PlatformOrder!.ExternalOrderNumber);
        Assert.Equal(PlatformCollectedBy.Business, charged.Value.PlatformOrder.CollectedBy);
    }

    [Fact]
    public async Task OrderTheBusinessCollects_CancelledUnpaid_ShouldKeepWhatWasCooked_UnlessAskedToGiveItBack()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, tacos) = await ShopAsync(fixture, TenantCapability.Food | TenantCapability.Inventory);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        var tortilla = new SupplyItem(tenant.Id, "Tortilla", UnitType.Unit, defaultCost: 1m);
        var location = await fixture.Context.InventoryLocations.SingleAsync();
        var stock = new InventoryStock(tenant.Id, tortilla.Id, location.Id);
        stock.ApplyMovement(20m, 1m, SalesTestFixture.Now);
        fixture.Context.SupplyItems.Add(tortilla);
        fixture.Context.InventoryStocks.Add(stock);
        fixture.Context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.Id, tacos.Id, tortilla.Id, 2m));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context), NullLogger<OrderInventoryService>.Instance);
        async Task<decimal> OnHand() => (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity;
        async Task<Guid> RegisterUnpaid(string folio)
        {
            var saved = await fixture.CreateOrderHandler(inventory).Handle(
                SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, folio, tacos.Id, quantity: 3, collectedBy: PlatformCollectedBy.Business),
                CancellationToken.None);
            fixture.Context.ChangeTracker.Clear();
            Assert.True(saved.IsSuccess, saved.Error);
            Assert.Empty(saved.Value.Payments);
            return saved.Value.Id;
        }
        Task<Result> Cancel(Guid orderId, bool? restock) => new CancelOrderCommandHandler(fixture.Context, fixture.CurrentUser, inventory)
            .Handle(restock is { } value
                ? new CancelOrderCommand { OrderId = orderId, Reason = "DiDi lo canceló", RestockItems = value }
                : new CancelOrderCommand { OrderId = orderId, Reason = "DiDi lo canceló" }, CancellationToken.None);

        // Three tacos take six tortillas as the order is registered.
        var cooked = await RegisterUnpaid("12");
        Assert.Equal(14m, await OnHand());

        // DiDi cancelled it once the dishes were cooked: the tortillas used stay used.
        Assert.True((await Cancel(cooked, restock: false)).IsSuccess);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(14m, await OnHand());
        Assert.Equal(OrderStatus.Cancelled, (await fixture.Context.Orders.AsNoTracking().SingleAsync(o => o.Id == cooked)).Status);

        // Left out, the supplies go back as they always did.
        var notCooked = await RegisterUnpaid("13");
        Assert.Equal(8m, await OnHand());
        Assert.True((await Cancel(notCooked, restock: null)).IsSuccess);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(14m, await OnHand());
    }

    [Fact]
    public async Task PlatformNotActiveInTheBusiness_ShouldBeRefused_WithoutSavingAnything()
    {
        await using var fixture = SalesTestFixture.Create();
        // Another business takes Uber Eats orders: that says nothing about this one.
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        await fixture.AddDeliveryPlatformAsync(other, OrderChannel.UberEats);
        var (tenant, pack, _) = await ShopAsync(fixture, slug: "propio");
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi, isActive: false);

        var never = await fixture.CreateOrderHandler().Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id), CancellationToken.None);
        var off = await fixture.CreateOrderHandler().Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id), CancellationToken.None);

        Assert.Equal(
            "Uber Eats no está activa en tu negocio: actívala en Configuración › Negocio › Plataformas de reparto.",
            never.Error);
        Assert.Equal(PlatformOrderErrors.NotActive(OrderChannel.Rappi), off.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
        Assert.Empty(await fixture.Context.FolioSequences.ToListAsync());
    }

    public static TheoryData<string, string> WhatAnOrderOfAPlatformCannotCarry => new()
    {
        { "teléfono", CreateOrderCommandValidator.PlatformNoPhoneMessage },
        { "cliente", PlatformOrderRules.NoCustomerMessage },
        { "mesa", CreateOrderCommandValidator.PlatformNoTableMessage },
        { "cuenta abierta", CreateOrderCommandValidator.PlatformNoTableMessage },
        { "mesa de la cuenta", CreateOrderCommandValidator.PlatformNoTableMessage },
        { "servicio", PlatformOrderRules.OnlyProductsMessage },
        { "detalle de servicio", PlatformOrderRules.OnlyProductsMessage },
        { "pedido del cliente", PlatformOrderRules.RegisteredByBusinessMessage },
        { "sin conexión", CreateOrderCommandValidator.PlatformWithConnectionMessage },
        { "pagos, sin decir quién cobró", CreateOrderCommandValidator.PlatformAlreadyChargedMessage },
        { "pagos, si cobró la plataforma", CreateOrderCommandValidator.PlatformAlreadyChargedMessage },
        { "propina", PlatformOrderRules.NoTipMessage },
        { "pago Plataforma", CreateOrderCommandValidator.PlatformPaymentMessage },
        { "sin folio", PlatformOrderNumber.RequiredMessage },
        { "folio con acento", PlatformOrderNumber.InvalidMessage },
        { "folio de 31", PlatformOrderNumber.InvalidMessage },
        { "más de 60 caracteres", PlatformOrderNumber.InvalidMessage }
    };

    [Theory, MemberData(nameof(WhatAnOrderOfAPlatformCannotCarry))]
    public void Validator_ShouldRefuse_WhatAnOrderOfAPlatformCannotCarry(string what, string message)
    {
        var rappi = SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", Guid.NewGuid());
        var business = rappi with { CollectedBy = PlatformCollectedBy.Business };
        var command = what switch
        {
            "teléfono" => rappi with { CustomerPhone = "55 1234 5678" },
            "cliente" => rappi with { CustomerId = Guid.NewGuid() },
            "mesa" => rappi with { TableNumber = "Mesa 4" },
            "cuenta abierta" => rappi with { OpenTab = true, CustomerName = "Juan" },
            "mesa de la cuenta" => rappi with { OpenTab = true, DiningTableId = Guid.NewGuid() },
            "servicio" => rappi with { OrderType = OrderType.Service },
            "detalle de servicio" => rappi with { ServiceDetail = new ServiceOrderDetailInput() },
            "pedido del cliente" => rappi with { CreatedByBusiness = false },
            "sin conexión" => rappi with { IdempotencyKey = Guid.NewGuid(), CapturedOfflineAt = SalesTestFixture.Now },
            "pagos, sin decir quién cobró" => rappi with { Payments = [Cash(100m)] },
            "pagos, si cobró la plataforma" => rappi with
            {
                CollectedBy = PlatformCollectedBy.Platform,
                Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Card }]
            },
            "propina" => business with { Payments = [Cash(100m) with { Tip = 10m }] },
            "pago Plataforma" => business with { Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Platform }] },
            "sin folio" => rappi with { ExternalOrderNumber = "   " },
            "folio con acento" => rappi with { ExternalOrderNumber = "ÁB1" },
            "folio de 31" => rappi with { ExternalOrderNumber = new string('7', 31) },
            // 61 characters typed, a valid folio once its spaces go: only the limit on what is typed refuses it.
            "más de 60 caracteres" => rappi with { ExternalOrderNumber = "BC953" + new string(' ', 56) },
            _ => throw new ArgumentOutOfRangeException(nameof(what), what, null)
        };

        var result = new CreateOrderCommandValidator().Validate(command);

        Assert.Contains(result.Errors, error => error.ErrorMessage == message);
    }

    [Fact]
    public void Validator_ShouldAcceptOrdersOfPlatforms_AsVenderSendsThem()
    {
        var validator = new CreateOrderCommandValidator();
        var rappi = SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "#152390", Guid.NewGuid(), bagName: "Juan") with
        {
            ExpectedTotal = 350m,
            IdempotencyKey = Guid.NewGuid(),
            CustomerNotes = "Sin cebolla"
        };
        var didi = SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", Guid.NewGuid(), collectedBy: PlatformCollectedBy.Business) with
        {
            Payments = [Cash(245m) with { CashReceived = 300m }]
        };
        var confirmed = rappi with { ConfirmPossibleDuplicate = true, CollectedBy = PlatformCollectedBy.Platform };
        // 60 characters typed is still within the limit.
        var padded = rappi with { ExternalOrderNumber = "BC953" + new string(' ', 55) };

        Assert.All(new[] { rappi, didi, confirmed, padded }, command =>
        {
            var result = validator.Validate(command);
            Assert.True(result.IsValid, string.Join(" ", result.Errors));
        });
        Assert.Contains(validator.Validate(rappi with { CollectedBy = (PlatformCollectedBy)7 }).Errors,
            error => error.PropertyName == nameof(CreateOrderCommand.CollectedBy));
    }

    [Theory]
    [InlineData(OrderChannel.Counter, "folio")]
    [InlineData(OrderChannel.Counter, "quién cobró")]
    [InlineData(OrderChannel.Counter, "confirmación")]
    [InlineData(OrderChannel.WhatsApp, "folio")]
    [InlineData(OrderChannel.WhatsApp, "quién cobró")]
    [InlineData(OrderChannel.WhatsApp, "confirmación")]
    public void Validator_ShouldKeepTheFolioAndWhoCharged_ForOrdersOfPlatforms(OrderChannel channel, string field)
    {
        var sale = new CreateOrderCommand { Channel = channel, Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }] };
        var command = field switch
        {
            "folio" => sale with { ExternalOrderNumber = "BC953" },
            "quién cobró" => sale with { CollectedBy = PlatformCollectedBy.Business },
            _ => sale with { ConfirmPossibleDuplicate = true }
        };

        var result = new CreateOrderCommandValidator().Validate(command);

        Assert.True(new CreateOrderCommandValidator().Validate(sale).IsValid);
        Assert.Equal(CreateOrderCommandValidator.PlatformFieldsOnlyForPlatformsMessage, Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public async Task ProductOutOfItsHours_ShouldBeTaken_ButOneTheBusinessTurnedOffShouldNot()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        // 17:00 UTC is 11:00 in Mexico City: the dinner pack is sold from 18:00 at the counter.
        pack.SetAvailabilitySchedule(new TimeSpan(18, 0, 0), new TimeSpan(23, 0, 0));
        tacos.SetAvailability(false);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var platform = await fixture.CreateOrderHandler().Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var counter = await Assert.ThrowsAsync<DomainException>(() => fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = pack.Id, Quantity = 1 }] }, CancellationToken.None));
        fixture.Context.ChangeTracker.Clear();
        var turnedOff = await Assert.ThrowsAsync<DomainException>(() => fixture.CreateOrderHandler().Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC954", tacos.Id), CancellationToken.None));

        Assert.True(platform.IsSuccess, platform.Error);
        Assert.Equal("El producto 'Paquete familiar' no está disponible en este horario.", counter.Message);
        Assert.Equal("El producto 'Orden de tacos' no está disponible.", turnedOff.Message);
    }

    [Fact]
    public async Task ServiceOfTheBusiness_ShouldNotGoInAnOrderOfAPlatform()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var catering = await fixture.AddProductAsync(tenant, "Servicio de banquete", 500m, unit: UnitType.Service);

        var result = await fixture.CreateOrderHandler().Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id) with
            {
                Items = [new CreateOrderItemDto { ProductId = pack.Id, Quantity = 1 }, new CreateOrderItemDto { ProductId = catering.Id, Quantity = 1 }]
            },
            CancellationToken.None);

        Assert.Equal(PlatformOrderRules.OnlyProductsMessage, result.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task BlockPolicy_ShouldNotRefuseAnOrderTheAppAlreadyTook_AndWarnInstead()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, tacos) = await ShopAsync(fixture, TenantCapability.Food | TenantCapability.Inventory);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var tortilla = new SupplyItem(tenant.Id, "Tortilla", UnitType.Unit, defaultCost: 1m);
        var location = await fixture.Context.InventoryLocations.SingleAsync();
        var stock = new InventoryStock(tenant.Id, tortilla.Id, location.Id);
        stock.ApplyMovement(4m, 1m, SalesTestFixture.Now);
        fixture.Context.SupplyItems.Add(tortilla);
        fixture.Context.InventoryStocks.Add(stock);
        fixture.Context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.Id, tacos.Id, tortilla.Id, 2m));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context), NullLogger<OrderInventoryService>.Instance);

        // The business blocks sales without supplies (the default), but the courier is already on the way.
        var result = await fixture.CreateOrderHandler(inventory).Handle(
            SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", tacos.Id, quantity: 3), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["Tortilla quedó en -2"], result.Value.StockWarnings);
        Assert.Equal(-2m, (await fixture.Context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public async Task SameFolioOfThePlatformToday_ShouldBeAskedFirst_AndRegisteredOnceConfirmed()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats, commissionPercent: 25m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var secondKey = Guid.NewGuid();

        var first = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id) with { IdempotencyKey = Guid.NewGuid() });
        // Typed another way, it is the same folio; Uber Eats' 5 characters repeat, so the cashier is asked.
        var again = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "#bc 953", pack.Id) with { IdempotencyKey = secondKey });
        // The same folio in another platform is another order.
        var rappi = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", pack.Id));
        // Vender sends the same order again, with the same key, confirmed.
        var confirmed = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "#bc 953", pack.Id) with
        {
            IdempotencyKey = secondKey,
            ConfirmPossibleDuplicate = true
        });

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(PlatformOrderErrors.PossibleDuplicate, again.Error);
        Assert.True(rappi.IsSuccess, rappi.Error);
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        // The warning came before the folio: none was skipped.
        Assert.Equal(["ORD-000001", "ORD-000002", "ORD-000003"], [first.Value.OrderNumber, rappi.Value.OrderNumber, confirmed.Value.OrderNumber]);
        Assert.Equal(2, await fixture.Context.PlatformOrderDetails.CountAsync(d => d.Channel == OrderChannel.UberEats && d.ExternalOrderNumber == "BC953"));
    }

    [Fact]
    public async Task SameFolio_ShouldPassOnAnotherDayOfTheBusiness_ButNotEarlierTheSameDay()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        DateTimeOffset At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

        // 21:00 of October 4 in Mexico City (UTC-6) is already October 5 in UTC.
        fixture.Clock.UtcNow = At(5, 3, 0);
        var evening = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", pack.Id));
        // 23:30 of the same day of the business: DiDi's pickup number repeats through the day.
        fixture.Clock.UtcNow = At(5, 5, 30);
        var sameDay = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", pack.Id));
        // 00:30 of October 5: another day of the business.
        fixture.Clock.UtcNow = At(5, 6, 30);
        var nextDay = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", pack.Id));

        Assert.True(evening.IsSuccess, evening.Error);
        Assert.Equal(PlatformOrderErrors.PossibleDuplicate, sameDay.Error);
        Assert.True(nextDay.IsSuccess, nextDay.Error);
    }

    [Fact]
    public async Task SameFolio_ShouldPass_WhenTheOtherOrderWasCancelledOrFullyRefunded_ButNotWhenPartlyRefunded()
    {
        await using var fixture = SalesTestFixture.Create();
        // With a kitchen screen, a paid order waits in Confirmed until it is ready: a void cancels it.
        var (kitchen, kitchenPack, _) = await ShopAsync(fixture, TenantCapability.Food | TenantCapability.Kitchen, slug: "cocina");
        await fixture.AddDeliveryPlatformAsync(kitchen, OrderChannel.Rappi);
        var cancelled = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", kitchenPack.Id));
        Assert.Equal(OrderStatus.Confirmed, cancelled.Value.Status);
        Assert.True((await VoidAsync(fixture, cancelled.Value.Id)).IsSuccess);
        var afterCancelled = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", kitchenPack.Id));

        // Without a kitchen screen it was handed over: a void refunds it in full and it stays delivered.
        var (counter, pack, _) = await ShopAsync(fixture, slug: "mostrador");
        await fixture.AddDeliveryPlatformAsync(counter, OrderChannel.Rappi);
        var refunded = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id));
        var voided = await VoidAsync(fixture, refunded.Value.Id);
        Assert.Equal((OrderStatus.Delivered, 0m), (voided.Value.Status, voided.Value.NetPaidAmount));
        var afterRefunded = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id));
        // Part of it given back: the order still counts.
        var refund = await RefundHandler(fixture).Handle(new RefundOrderCommand
        {
            OrderId = afterRefunded.Value.Id,
            Amount = 100m,
            Method = OrderPaymentMethod.Platform,
            Reason = "Faltó una bebida"
        }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var afterPartial = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id));

        Assert.True(afterCancelled.IsSuccess, afterCancelled.Error);
        Assert.True(afterRefunded.IsSuccess, afterRefunded.Error);
        Assert.True(refund.IsSuccess, refund.Error);
        Assert.Equal(PlatformOrderErrors.PossibleDuplicate, afterPartial.Error);
    }

    [Fact]
    public async Task RetryWithTheSameKey_ShouldAnswerTheSameOrder_WithItsFolio()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        var command = SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id) with { IdempotencyKey = Guid.NewGuid() };

        var first = await SellAsync(fixture, command);
        // The answer was lost: the same request comes again, not as a repeated folio (correction 2: it keeps the folio).
        var retry = await SellAsync(fixture, command);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal("BC953", retry.Value.PlatformOrder!.ExternalOrderNumber);
        Assert.Equal(PlatformCollectedBy.Platform, retry.Value.PlatformOrder.CollectedBy);
        Assert.Single(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task DetailAndKitchenBoard_ShouldShowTheFolioOfTheApp()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture, TenantCapability.Food | TenantCapability.Kitchen);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var sale = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id, bagName: "Juan"));

        // Both read the order again: each includes the detail (correction 2).
        var detail = await new Orders.Queries.GetOrderByIdQueryHandler(fixture.Context)
            .Handle(new Orders.Queries.GetOrderByIdQuery(sale.Value.Id), CancellationToken.None);
        var board = await new Orders.Queries.GetKitchenOrdersQueryHandler(fixture.Context)
            .Handle(new Orders.Queries.GetKitchenOrdersQuery(), CancellationToken.None);

        Assert.Equal(("152390", PlatformCollectedBy.Platform), (detail.PlatformOrder!.ExternalOrderNumber, detail.PlatformOrder.CollectedBy));
        var ticket = Assert.Single(board);
        Assert.Equal((sale.Value.Id, OrderChannel.Rappi, "152390", "Juan"), (ticket.Id, ticket.Channel, ticket.ExternalOrderNumber, ticket.CustomerName));
    }

    [Fact]
    public async Task CreatedOrder_ShouldTellTheChannelTheFolioAndWhoCharged_AndSoShouldTheActivityLog()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);

        var result = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "#bc 953", pack.Id));
        var created = Assert.Single(mediator.Published.OfType<OrderCreatedEvent>());
        await new OrderAuditHandlers(fixture.Context).HandleAsync(created, OutboxContext(tenant.Id), CancellationToken.None);
        await fixture.Context.SaveChangesAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal((result.Value.Id, OrderChannel.Rappi, "BC953", PlatformCollectedBy.Platform),
            (created.OrderId, created.Channel, created.ExternalOrderNumber, created.CollectedBy));
        var log = await fixture.Context.AuditLogs.SingleAsync(a => a.Action == "OrderCreated");
        var metadata = JsonDocument.Parse(log.Metadata!).RootElement;
        Assert.Equal((int)OrderChannel.Rappi, metadata.GetProperty("Channel").GetInt32());
        Assert.Equal("BC953", metadata.GetProperty("ExternalOrderNumber").GetString());
        Assert.Equal((int)PlatformCollectedBy.Platform, metadata.GetProperty("CollectedBy").GetInt32());
        Assert.Equal(result.Value.OrderNumber, metadata.GetProperty("OrderNumber").GetString());
    }

    [Fact]
    public async Task CustomerOfAPlatform_ShouldNeverBeMessaged_EvenWhenTheBusinessMessagesEveryOrder()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        (await fixture.Context.TenantSettings.SingleAsync()).SetOrderNotificationPolicy(OrderNotificationPolicy.AllOrders);
        await fixture.Context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsApp();
        var notifications = new OrderCustomerNotifications(fixture.Context, whatsApp, NullLogger<OrderCustomerNotifications>.Instance);

        var rappi = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id));
        // Even if the order had a phone (it never keeps one), the platform's customer is the platform's.
        var stored = await fixture.Context.Orders.SingleAsync(o => o.Id == rappi.Value.Id);
        EntityFiller.Set(stored, nameof(Order.CustomerPhone), "+525511112222");
        await fixture.Context.SaveChangesAsync();
        var context = OutboxContext(tenant.Id);
        await notifications.HandleAsync(Assert.Single(mediator.Published.OfType<OrderCreatedEvent>()), context, CancellationToken.None);
        await notifications.HandleAsync(new OrderStatusChangedEvent(stored.Id, tenant.Id, OrderStatus.Confirmed, OrderStatus.Ready), context, CancellationToken.None);
        await notifications.HandleAsync(new OrderCancelledEvent(stored.Id, tenant.Id, OrderStatus.Ready, "Rappi lo canceló"), context, CancellationToken.None);
        Assert.Empty(whatsApp.Templates);

        // A counter sale with the customer's phone is confirmed under the same policy.
        mediator.Published.Clear();
        var counter = await SellAsync(fixture, new CreateOrderCommand
        {
            CustomerName = "Ana",
            CustomerPhone = "55 2222 3333",
            Items = [new CreateOrderItemDto { ProductId = pack.Id, Quantity = 1 }]
        });
        Assert.True(counter.IsSuccess, counter.Error);
        await notifications.HandleAsync(Assert.Single(mediator.Published.OfType<OrderCreatedEvent>()), context, CancellationToken.None);
        Assert.Equal(["order_confirmed"], whatsApp.Templates);
    }

    [Fact]
    public async Task Campaigns_ShouldLeaveOutOrdersOfPlatforms_EvenWithAPhone()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        var platform = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id, bagName: "Luis"));
        var counter = await SellAsync(fixture, new CreateOrderCommand
        {
            CustomerName = "Ana",
            CustomerPhone = "55 2222 3333",
            Items = [new CreateOrderItemDto { ProductId = pack.Id, Quantity = 1 }]
        });
        await fixture.OpenCashSessionAsync(tenant);
        Assert.True((await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = counter.Value.Id, Amount = 1000m, Method = OrderPaymentMethod.Cash }, CancellationToken.None)).IsSuccess);
        // Defense in depth: a phone that reached an order of a platform still never gets a campaign.
        var stored = await fixture.Context.Orders.SingleAsync(o => o.Id == platform.Value.Id);
        EntityFiller.Set(stored, nameof(Order.CustomerPhone), "+525511112222");
        await fixture.Context.SaveChangesAsync();
        var resolver = new CampaignAudienceResolver(fixture.Context, new PhoneNormalizationService());

        var everyone = await resolver.ResolveAsync(CampaignAudienceKind.AllCustomers, 1, null, SalesTestFixture.Now, CancellationToken.None);
        var buyers = await resolver.ResolveAsync(CampaignAudienceKind.Buyers, 1, null, SalesTestFixture.Now, CancellationToken.None);
        var lapsed = await resolver.ResolveAsync(CampaignAudienceKind.Lapsed, 1, 30, SalesTestFixture.Now.AddDays(60), CancellationToken.None);

        Assert.All(new[] { everyone, buyers, lapsed }, audience =>
            Assert.Equal(["+525522223333"], audience.Recipients.Select(r => r.Phone)));
    }

    [Fact]
    public async Task CustomerCard_ShouldNotShowOrdersOfPlatforms()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        // The same name for the bag as a customer of the business: the order of the platform keeps no phone to join them.
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", pack.Id, bagName: "Ana"));
        var counter = await SellAsync(fixture, new CreateOrderCommand
        {
            CustomerName = "Ana",
            CustomerPhone = "55 2222 3333",
            Items = [new CreateOrderItemDto { ProductId = pack.Id, Quantity = 1 }]
        });

        var card = await new GetCustomerCardQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.TenantService, fixture.Clock)
            .Handle(new GetCustomerCardQuery { Phone = "55 2222 3333" }, CancellationToken.None);

        Assert.True(card.IsSuccess, card.Error);
        Assert.Equal(1, card.Value.OrdersCount);
        Assert.Equal(counter.Value.Id, Assert.Single(card.Value.RecentOrders).Id);
    }

    /// <summary>A kitchen selling with IVA included: a $1,000 family pack and $245 tacos.</summary>
    private static async Task<(Tenant Tenant, Product Pack, Product Tacos)> ShopAsync(
        SalesTestFixture fixture,
        TenantCapability capabilities = TenantCapability.Food,
        string slug = "demo")
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, capabilities, pricesIncludeTax: true, slug: slug);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 1000m);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 245m);
        return (tenant, pack, tacos);
    }

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result<OrderDto>> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var result = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result<OrderDto>> VoidAsync(SalesTestFixture fixture, Guid orderId)
    {
        var result = await new VoidOrderCommandHandler(
                fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser),
                fixture.Clock, NoOpOrderInventoryService.Instance)
            .Handle(new VoidOrderCommand { OrderId = orderId, Reason = "Rappi lo canceló", RestockItems = false }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static RefundOrderCommandHandler RefundHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser),
            fixture.Clock, NoOpOrderInventoryService.Instance);

    private static CreateOrderPaymentDto Cash(decimal amount) => new() { Amount = amount, Method = OrderPaymentMethod.Cash };

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
