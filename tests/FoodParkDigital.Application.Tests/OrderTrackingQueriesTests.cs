using System.Text.Json;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Following an order (Fase 24C, ADR 0023): an online order carries its link from the receipt, also when it is sent again;
/// the link shows it for its days without anything of the customer, whatever business the request is in; the account sees
/// only its own orders in detail, and its list splits the ones in progress from the ones before.
/// </summary>
public class OrderTrackingQueriesTests
{
    private const string Address = "Av. Reforma 123, Centro";

    [Fact]
    public async Task OnlineOrder_ShouldCarryItsLink_AlsoWhenSentAgain()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        var key = Guid.NewGuid();

        var receipt = await PlaceAsync(fixture, ToAddress(tacos) with { IdempotencyKey = key });
        var again = await PlaceAsync(fixture, ToAddress(tacos) with { IdempotencyKey = key });
        var another = await PlaceAsync(fixture, Pickup(tacos));

        Assert.True(receipt.IsSuccess, receipt.Error);
        Assert.True(Order.IsTrackingToken(receipt.Value.TrackingToken), receipt.Value.TrackingToken);
        Assert.Equal(receipt.Value.TrackingToken, again.Value.TrackingToken);
        Assert.NotEqual(receipt.Value.TrackingToken, another.Value.TrackingToken);
        var stored = await fixture.Context.Orders.SingleAsync(o => o.ClientRequestId == key);
        Assert.Equal(receipt.Value.TrackingToken, stored.TrackingToken);
    }

    [Fact]
    public async Task Link_ShouldShowWhereTheOrderIs_WithoutAnythingOfTheCustomer_FromAnyBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await StoreAsync(fixture);
        var token = (await PlaceAsync(fixture, ToAddress(tacos) with
        {
            Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 2, Notes = "Para Mariana, sin cebolla" }]
        })).Value.TrackingToken!;
        await ChangeAsync(fixture, token, order =>
        {
            order.CreatedAt = fixture.Clock.UtcNow;
            order.TransitionTo(OrderStatus.InPreparation, OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "cocina");
        });
        // The request comes with no business, or another one in context: the link names its own.
        fixture.TenantService.SetTenant(Guid.NewGuid());

        var tracking = await TrackAsync(fixture, token);

        Assert.NotNull(tracking);
        Assert.Equal(("Negocio demo", "demo", OrderTrackingStep.Preparing, "En preparación", true, true),
            (tracking.BusinessName, tracking.BusinessSlug, tracking.Step, tracking.StepLabel, tracking.InProgress, tracking.ToAddress));
        Assert.Equal(["Recibido", "Confirmado", "En preparación", "Listo", "En camino", "Entregado"], tracking.Steps.Select(s => s.Label));
        Assert.Equal(OrderTrackingStepState.Current, tracking.Steps[2].State);
        Assert.Equal(fixture.Clock.UtcNow, tracking.Steps[0].At);
        var item = Assert.Single(tracking.Items);
        Assert.Equal(("Orden de tacos", 2m, 240m, (string?)null), (item.ProductName, item.Quantity, item.Amount, item.Notes));
        Assert.Equal((35m, 275m), (tracking.DeliveryFee, tracking.Total));
        Assert.Equal(tenant.Id, fixture.TenantService.TenantId);

        // Nothing of the customer: neither the name, the phone nor the address.
        var json = JsonSerializer.Serialize(tracking);
        Assert.DoesNotContain("Mariana", json);
        Assert.DoesNotContain("5512345678", json);
        Assert.DoesNotContain("Reforma", json);
        Assert.DoesNotContain("72000", json);
    }

    [Fact]
    public async Task Link_Unknown_Changed_Expired_OrOfABusinessThatStopped_ShouldNotBeFound()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await StoreAsync(fixture);
        var token = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        var now = fixture.Clock.UtcNow;
        await ChangeAsync(fixture, token, order => order.CreatedAt = now.AddDays(-OrderTracking.Days));

        // On its last day it is still followed.
        Assert.NotNull(await TrackAsync(fixture, token));
        Assert.Null(await TrackAsync(fixture, "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE"));
        Assert.Null(await TrackAsync(fixture, SwapCase(token)));
        Assert.Null(await TrackAsync(fixture, token[..^1]));
        Assert.Null(await TrackAsync(fixture, "../../orders"));

        await ChangeAsync(fixture, token, order => order.CreatedAt = now.AddDays(-OrderTracking.Days).AddMinutes(-1));
        Assert.Null(await TrackAsync(fixture, token));

        await ChangeAsync(fixture, token, order => order.CreatedAt = now);
        var business = await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id);
        business.SetStatus(TenantStatus.Suspended);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.Null(await TrackAsync(fixture, token));
    }

    [Fact]
    public async Task MyOrder_ShouldBeOnlyTheAccounts_WithItsAddressAndSteps()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        var beto = await AccountAsync(fixture, "beto@demo.com");
        SignIn(fixture, mariana);
        var token = (await PlaceAsync(fixture, ToAddress(tacos) with { Notes = "Tocar el timbre" })).Value.TrackingToken!;
        await ChangeAsync(fixture, token, order => order.CreatedAt = fixture.Clock.UtcNow);
        var hers = await fixture.Context.Orders.SingleAsync();
        fixture.CurrentUser.UserId = Guid.NewGuid().ToString();
        await PlaceAsync(fixture, Pickup(tacos) with { CustomerPhone = "5587654321" });
        var guests = await fixture.Context.Orders.SingleAsync(o => o.Id != hers.Id);

        SignIn(fixture, mariana);
        var detail = await MyOrderAsync(fixture, hers.Id);

        Assert.NotNull(detail);
        Assert.Equal((hers.OrderNumber, OrderTrackingStep.Confirmed, "Confirmado", true, true),
            (detail.OrderNumber, detail.Step, detail.StepLabel, detail.CanOrderAgain, detail.InProgress));
        Assert.Equal(("72000", Address), (detail.Delivery!.PostalCode, detail.Delivery.AddressLine));
        Assert.Equal(tacos.Id, Assert.Single(detail.Items).ProductId);
        Assert.Contains(detail.Steps, s => s.Step == OrderTrackingStep.OnTheWay);
        // Another account, a guest's order and no session find nothing.
        Assert.Null(await MyOrderAsync(fixture, guests.Id));
        SignIn(fixture, beto);
        Assert.Null(await MyOrderAsync(fixture, hers.Id));
        fixture.CurrentUser.UserId = null;
        Assert.Null(await MyOrderAsync(fixture, hers.Id));
    }

    [Fact]
    public async Task MyOrders_ShouldSplitTheOnesInProgress_FromTheOnesBefore()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);
        var now = fixture.Clock.UtcNow;
        var cooking = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        var delivered = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        var forgotten = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        await ChangeAsync(fixture, cooking, order => order.CreatedAt = now);
        await ChangeAsync(fixture, delivered, order =>
        {
            order.CreatedAt = now.AddMinutes(-30);
            order.TransitionTo(OrderStatus.Delivered, OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "caja");
        });
        // Still open after its days: the business left it behind.
        await ChangeAsync(fixture, forgotten, order => order.CreatedAt = now.AddDays(-OrderTracking.Days - 1));

        var inProgress = await MyOrdersAsync(fixture, MyOrdersScope.InProgress);
        var past = await MyOrdersAsync(fixture, MyOrdersScope.Past);
        var all = await MyOrdersAsync(fixture, MyOrdersScope.All);

        var current = Assert.Single(inProgress.Items);
        Assert.Equal((OrderTrackingStep.Confirmed, "Confirmado"), (current.Step, current.StepLabel));
        Assert.Equal(2, past.TotalCount);
        Assert.Equal(["Entregado", "Confirmado"], past.Items.Select(o => o.StepLabel));
        Assert.Equal(3, all.TotalCount);
    }

    [Fact]
    public void TrackingValidator_ShouldRefuseAnEmptyOrLongLink()
    {
        var validator = new GetPublicOrderTrackingQueryValidator();

        Assert.True(validator.Validate(new GetPublicOrderTrackingQuery("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE")).IsValid);
        Assert.False(validator.Validate(new GetPublicOrderTrackingQuery("")).IsValid);
        Assert.False(validator.Validate(new GetPublicOrderTrackingQuery(new string('a', 101))).IsValid);
        Assert.False(new GetMyOrdersQueryValidator().Validate(new GetMyOrdersQuery { Scope = (MyOrdersScope)9 }).IsValid);
    }

    /// <summary>A kitchen that takes online orders and accepts them as they arrive, picked up or to 72000 (Centro: $35).</summary>
    private static async Task<(Tenant Tenant, Product Tacos)> StoreAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: "demo");
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var zone = await new CreateDeliveryZoneCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new CreateDeliveryZoneCommand("Centro", 35m, 150m, null, ["72000"]), CancellationToken.None);
        Assert.True(zone.IsSuccess, zone.Error);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.Update(settings.Currency, settings.TimeZone, autoAcceptOrders: true, settings.MaxConcurrentOrders);
        await fixture.Context.SaveChangesAsync();
        await fixture.OpenCashSessionAsync(tenant, (await fixture.DefaultBranchAsync()).Id);
        fixture.Context.ChangeTracker.Clear();
        return (tenant, tacos);
    }

    private static async Task<ApplicationUser> AccountAsync(SalesTestFixture fixture, string email)
    {
        var account = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Mariana",
            LastName = "Ortiz",
            Role = UserRole.Customer,
            IsActive = true
        };
        fixture.Context.Users.Add(account);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return account;
    }

    private static void SignIn(SalesTestFixture fixture, ApplicationUser account)
    {
        fixture.CurrentUser.UserId = account.Id.ToString();
        fixture.CurrentUser.Role = "CUSTOMER";
    }

    private static PlacePublicOrderCommand Pickup(Product product) => new()
    {
        Slug = "demo",
        CustomerName = "Mariana Ortiz",
        CustomerPhone = "55 1234 5678",
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
    };

    private static PlacePublicOrderCommand ToAddress(Product product) => Pickup(product) with
    {
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 2 }],
        Delivery = new OrderDeliveryInput { PostalCode = "72000", AddressLine = Address }
    };

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result<PublicOrderReceiptDto>> PlaceAsync(SalesTestFixture fixture, PlacePublicOrderCommand command)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        var result = await new PlacePublicOrderCommandHandler(
                fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance,
                new FolioGenerator(fixture.Context), fixture.CurrentUser)
            .Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    /// <summary>Changes the order of the link as the business would (InMemory sets no creation time of its own).</summary>
    private static async Task ChangeAsync(SalesTestFixture fixture, string token, Action<Order> change)
    {
        var order = await fixture.Context.Orders.IgnoreQueryFilters()
            .Include(o => o.Items)
            .Include(o => o.Delivery)
            .SingleAsync(o => o.TrackingToken == token);
        change(order);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<PublicOrderTrackingDto?> TrackAsync(SalesTestFixture fixture, string token)
    {
        var tracking = await new GetPublicOrderTrackingQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicOrderTrackingQuery(token), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return tracking;
    }

    private static Task<MyOrderDetailDto?> MyOrderAsync(SalesTestFixture fixture, Guid id)
        => new GetMyOrderQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock).Handle(new GetMyOrderQuery(id), CancellationToken.None);

    private static Task<PaginatedList<MyOrderDto>> MyOrdersAsync(SalesTestFixture fixture, MyOrdersScope scope)
        => new GetMyOrdersQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
            .Handle(new GetMyOrdersQuery { Scope = scope, PageSize = 50 }, CancellationToken.None);

    private static string SwapCase(string token)
        => new(token.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
}
