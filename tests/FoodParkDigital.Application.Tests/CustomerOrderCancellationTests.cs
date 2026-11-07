using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The customer cancels their order of the online menu (Fase 29R, ADR 0072), with the link of its receipt or from their
/// account, while the business has not accepted it and nothing was paid: what it took from the inventory goes back, and the
/// link and the account say they may cancel it while they may.
/// </summary>
public class CustomerOrderCancellationTests
{
    [Fact]
    public async Task TheLink_ShouldCancelAnOrderNotAcceptedYet_ReturningItsInventory_FromAnyBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await StoreAsync(fixture);
        var token = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        Assert.True((await TrackAsync(fixture, token))!.CanCancel);
        var inventory = new RecordingInventory();
        // The request comes with no business, or another one in context: the link names its own.
        fixture.TenantService.SetTenant(Guid.NewGuid());

        var cancelled = await CancelByLinkAsync(fixture, token, inventory);

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal((OrderTrackingStep.Cancelled, false, false, Order.CustomerCancellationReason),
            (cancelled.Value!.Step, cancelled.Value.CanCancel, cancelled.Value.InProgress, cancelled.Value.CancellationReason));
        var stored = await fixture.Context.Orders.IgnoreQueryFilters().Include(o => o.StatusHistory).SingleAsync(o => o.TrackingToken == token);
        Assert.Equal((OrderStatus.Cancelled, Order.CustomerPerformedBy), (stored.Status, stored.StatusHistory.Last().ChangedBy));
        Assert.Equal([stored.Id], inventory.Reversed);
        Assert.Equal(tenant.Id, fixture.TenantService.TenantId);

        // Tapped twice, or sent again after its answer was lost: the same answer, without a second cancellation.
        var again = await CancelByLinkAsync(fixture, token, inventory);
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(OrderTrackingStep.Cancelled, again.Value!.Step);
        Assert.Equal([stored.Id], inventory.Reversed);
        Assert.Single(
            (await fixture.Context.Orders.IgnoreQueryFilters().Include(o => o.StatusHistory).SingleAsync(o => o.TrackingToken == token)).StatusHistory,
            history => history.NewStatus == OrderStatus.Cancelled);
    }

    [Fact]
    public async Task TheLink_ShouldNotCancelAnOrderTheBusinessAccepted_OrAnUnknownLink()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        var token = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        await ChangeAsync(fixture, token, order => order.TransitionTo(
            OrderStatus.Confirmed, OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "cajero"));
        var inventory = new RecordingInventory();

        Assert.False((await TrackAsync(fixture, token))!.CanCancel);
        var accepted = await Assert.ThrowsAsync<DomainException>(() => CancelByLinkAsync(fixture, token, inventory));
        var unknown = await CancelByLinkAsync(fixture, new string('x', Order.TrackingTokenLength), inventory);

        Assert.Equal(Order.CustomerCancelAcceptedMessage, accepted.Message);
        Assert.Equal(CustomerOrderCancellation.NotFoundMessage, unknown.Error);
        Assert.Empty(inventory.Reversed);
        Assert.Equal(OrderStatus.Confirmed, (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == token)).Status);
    }

    [Fact]
    public async Task TheAccount_ShouldCancelOnlyItsOwnOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        var other = await AccountAsync(fixture, "otra@demo.com");
        SignIn(fixture, mariana);
        var token = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        var id = (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == token)).Id;
        Assert.True((await MyOrderAsync(fixture, id))!.CanCancel);
        var inventory = new RecordingInventory();

        SignIn(fixture, other);
        var notTheirs = await CancelMineAsync(fixture, id, inventory);
        SignIn(fixture, mariana);
        var hers = await CancelMineAsync(fixture, id, inventory);

        Assert.Equal(CustomerOrderCancellation.NotFoundMessage, notTheirs.Error);
        Assert.True(hers.IsSuccess, hers.Error);
        Assert.Equal((OrderStatus.Cancelled, false), (hers.Value!.Status, hers.Value.CanCancel));
        Assert.Equal([id], inventory.Reversed);
    }

    [Fact]
    public async Task TheOrdersOnScreen_ShouldHearThatTheCustomerCancelledIt_WithItsNumber()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, tacos) = await StoreAsync(fixture);
        var receipt = (await PlaceAsync(fixture, Pickup(tacos))).Value;
        mediator.Published.Clear();

        Assert.True((await CancelByLinkAsync(fixture, receipt.TrackingToken!, new RecordingInventory())).IsSuccess);
        var cancelled = Assert.Single(mediator.Published.OfType<OrderCancelledEvent>());
        var screens = new RecordingOrderScreens();
        await new OrderRealtimeNotifications(fixture.Context, screens, NullLogger<OrderRealtimeNotifications>.Instance)
            .Handle(cancelled, CancellationToken.None);

        Assert.True(cancelled.ByCustomer);
        Assert.Equal(
            (tenant.Id, cancelled.OrderId, OrderStatus.Pending, Order.CustomerCancellationReason, receipt.OrderNumber, true),
            Assert.Single(screens.Cancelled));
    }

    /// <summary>A kitchen that takes online orders and accepts each one itself: they arrive waiting.</summary>
    private static async Task<(Tenant Tenant, Product Tacos)> StoreAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: "demo");
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.Update(settings.Currency, settings.TimeZone, autoAcceptOrders: false, settings.MaxConcurrentOrders);
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

    private static async Task<Result<PublicOrderReceiptDto>> PlaceAsync(SalesTestFixture fixture, PlacePublicOrderCommand command)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        var result = await new PlacePublicOrderCommandHandler(
                fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance,
                new FolioGenerator(fixture.Context), fixture.CurrentUser)
            .Handle(command, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        fixture.Context.ChangeTracker.Clear();
        // The fixture has no auditing interceptor: the order was placed now, as the database would stamp it.
        await ChangeAsync(fixture, result.Value.TrackingToken!, order => order.CreatedAt = fixture.Clock.UtcNow);
        return result;
    }

    private static async Task ChangeAsync(SalesTestFixture fixture, string token, Action<Order> change)
    {
        var order = await fixture.Context.Orders.IgnoreQueryFilters()
            .Include(o => o.StatusHistory)
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

    private static async Task<Result<PublicOrderTrackingDto>> CancelByLinkAsync(SalesTestFixture fixture, string token, IOrderInventoryService inventory)
    {
        try
        {
            return await new CancelPublicOrderCommandHandler(fixture.Context, fixture.TenantService, inventory, fixture.Clock)
                .Handle(new CancelPublicOrderCommand(token), CancellationToken.None);
        }
        finally
        {
            fixture.Context.ChangeTracker.Clear();
        }
    }

    private static async Task<Result<MyOrderDetailDto>> CancelMineAsync(SalesTestFixture fixture, Guid id, IOrderInventoryService inventory)
    {
        try
        {
            return await new CancelMyOrderCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, inventory, fixture.Clock)
                .Handle(new CancelMyOrderCommand(id), CancellationToken.None);
        }
        finally
        {
            fixture.Context.ChangeTracker.Clear();
        }
    }

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

    /// <summary>What the live screens of the orders heard of each cancellation.</summary>
    private sealed class RecordingOrderScreens : IOrderNotificationService
    {
        public List<(Guid TenantId, Guid OrderId, OrderStatus PreviousStatus, string? Reason, string OrderNumber, bool ByCustomer)> Cancelled { get; } = [];

        public Task NotifyOrderCancelled(Guid tenantId, Guid orderId, OrderStatus previousStatus, string? reason, string orderNumber, bool byCustomer)
        {
            Cancelled.Add((tenantId, orderId, previousStatus, reason, orderNumber, byCustomer));
            return Task.CompletedTask;
        }

        public Task NotifyOrderUpdated(Guid tenantId, Guid orderId) => throw new NotSupportedException();
        public Task NotifyOrderCreated(Guid tenantId, Guid orderId, string orderNumber, BusinessType businessType, decimal total,
            OrderStatus status, OrderChannel channel, string? externalOrderNumber) => throw new NotSupportedException();
        public Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus) => throw new NotSupportedException();
        public Task NotifyKitchenTicketsChanged(Guid tenantId, Guid orderId, int round) => throw new NotSupportedException();
        public Task NotifyServiceBoardPieceUpdated(Guid tenantId, Guid orderId, Guid pieceId, ServiceOrderPieceStatus status, Guid? assignedToUserId)
            => throw new NotSupportedException();
    }

    /// <summary>Consumes nothing, and records each order whose consumption goes back.</summary>
    private sealed class RecordingInventory : IOrderInventoryService
    {
        private readonly IOrderInventoryService _inner = NoOpOrderInventoryService.Instance;

        public List<Guid> Reversed { get; } = [];

        public Task<IReadOnlyList<StockShortage>> ApplyConsumptionForOrderAsync(
            Order order, CancellationToken cancellationToken, StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy)
            => _inner.ApplyConsumptionForOrderAsync(order, cancellationToken, shortageHandling);

        public Task<IReadOnlyList<StockShortage>> ApplyConsumptionForTabRoundAsync(
            Order order, int round, CancellationToken cancellationToken, StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy)
            => _inner.ApplyConsumptionForTabRoundAsync(order, round, cancellationToken, shortageHandling);

        public Task<IReadOnlyList<StockShortage>> ApplyConsumptionForServicePieceAsync(
            Order order, ServiceOrderPiece piece, CancellationToken cancellationToken, StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy)
            => _inner.ApplyConsumptionForServicePieceAsync(order, piece, cancellationToken, shortageHandling);

        public Task ReverseConsumptionForOrderAsync(Order order, string? reason, CancellationToken cancellationToken)
        {
            Reversed.Add(order.Id);
            return Task.CompletedTask;
        }

        public Task ReturnItemsToInventoryAsync(Order order, OrderRefund refund, CancellationToken cancellationToken)
            => _inner.ReturnItemsToInventoryAsync(order, refund, cancellationToken);

        public Task ReturnCancelledTabItemAsync(Order order, OrderItemCancellation cancellation, CancellationToken cancellationToken)
            => _inner.ReturnCancelledTabItemAsync(order, cancellation, cancellationToken);
    }
}
