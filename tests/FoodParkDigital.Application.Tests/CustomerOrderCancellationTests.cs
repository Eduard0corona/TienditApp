using FoodParkDigital.Application.Common.Events;
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
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        var cancelledByIt = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        await ChangeAsync(fixture, cancelledByIt, order => order.Cancel(
            OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "cajero", "Sin existencias"));
        var expired = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        await ChangeAsync(fixture, expired, order => order.CreatedAt = fixture.Clock.UtcNow.AddDays(-OrderTracking.Days).AddMinutes(-1));
        var inventory = new RecordingInventory();

        Assert.False((await TrackAsync(fixture, token))!.CanCancel);
        var accepted = await Assert.ThrowsAsync<DomainException>(() => CancelByLinkAsync(fixture, token, inventory));
        // The business cancelled it first: the customer is not told they did.
        var byTheBusiness = await Assert.ThrowsAsync<DomainException>(() => CancelByLinkAsync(fixture, cancelledByIt, inventory));
        var unknown = await CancelByLinkAsync(fixture, new string('x', Order.TrackingTokenLength), inventory);
        var tooOld = await CancelByLinkAsync(fixture, expired, inventory);

        Assert.Equal(Order.CustomerCancelAcceptedMessage, accepted.Message);
        Assert.Equal(Order.CustomerCancelByBusinessMessage, byTheBusiness.Message);
        Assert.Equal((OrderTracking.NotFoundMessage, OrderTracking.NotFoundMessage), (unknown.Error, tooOld.Error));
        Assert.Empty(inventory.Reversed);
        Assert.Equal(OrderStatus.Confirmed, (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == token)).Status);
        Assert.Equal(OrderStatus.Pending, (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == expired)).Status);
    }

    [Fact]
    public async Task TheBusinessAcceptingItAtTheSameTime_ShouldWin_AndTheLinkShouldSaySo()
    {
        var race = new AcceptedMeanwhile();
        await using var fixture = SalesTestFixture.Create(null, race);
        var (_, tacos) = await StoreAsync(fixture);
        var token = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        race.Arm(fixture);

        // The customer's save meets the row the business changed: it reads the order again, and the domain says why not.
        var refused = await Assert.ThrowsAsync<DomainException>(() => CancelByLinkAsync(fixture, token, new RecordingInventory()));

        Assert.Equal(Order.CustomerCancelAcceptedMessage, refused.Message);
        Assert.True(race.Raced);
        var stored = await fixture.Context.Orders.IgnoreQueryFilters().Include(o => o.StatusHistory).SingleAsync(o => o.TrackingToken == token);
        Assert.Equal(OrderStatus.Confirmed, stored.Status);
        Assert.DoesNotContain(stored.StatusHistory, history => history.NewStatus == OrderStatus.Cancelled);
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
        // The account's request comes with no business, or another one in context: the order names its own.
        fixture.TenantService.SetTenant(Guid.NewGuid());
        var hers = await CancelMineAsync(fixture, id, inventory);

        Assert.Equal(CustomerOrderCancellation.NotFoundMessage, notTheirs.Error);
        Assert.True(hers.IsSuccess, hers.Error);
        Assert.Equal((OrderStatus.Cancelled, false), (hers.Value!.Status, hers.Value.CanCancel));
        Assert.Equal([id], inventory.Reversed);
    }

    [Fact]
    public async Task TheAccount_ShouldNotCancelAnOrderOfABusinessThatIsNotActive_NorOneOfATable()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await StoreAsync(fixture);
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);
        var suspendedToken = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        var suspended = (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == suspendedToken)).Id;
        var inventory = new RecordingInventory();

        // Ordered with the QR of a table: whoever serves it handles it; the account does not offer it, and refuses it.
        var tableToken = (await PlaceAsync(fixture, Pickup(tacos) with { TableLabel = "Mesa 4" })).Value.TrackingToken!;
        var atATable = (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == tableToken)).Id;
        Assert.False((await MyOrderAsync(fixture, atATable))!.CanCancel);
        var refused = await Assert.ThrowsAsync<DomainException>(() => CancelMineAsync(fixture, atATable, inventory));
        Assert.Equal(Order.CustomerCancelTableMessage, refused.Message);
        Assert.True((await MyOrderAsync(fixture, suspended))!.CanCancel);

        // A business suspended takes no changes: the account does not offer it, and cancelling it is not found, as by its link.
        var business = await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id);
        business.SetStatus(TenantStatus.Suspended);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        Assert.False((await MyOrderAsync(fixture, suspended))!.CanCancel);
        Assert.Equal(CustomerOrderCancellation.NotFoundMessage, (await CancelMineAsync(fixture, suspended, inventory)).Error);
        Assert.Empty(inventory.Reversed);
        Assert.Equal(OrderStatus.Pending, (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == suspended)).Status);
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
        var accounts = new RecordingCustomerOrders();
        await new OrderRealtimeNotifications(fixture.Context, screens, accounts, NullLogger<OrderRealtimeNotifications>.Instance)
            .Handle(cancelled, CancellationToken.None);

        Assert.True(cancelled.ByCustomer);
        Assert.Equal(
            (tenant.Id, cancelled.OrderId, OrderStatus.Pending, Order.CustomerCancellationReason, receipt.OrderNumber, true),
            Assert.Single(screens.Cancelled));
        // Placed without signing in: no account hears it.
        Assert.Empty(accounts.Changed);
    }

    [Fact]
    public async Task TheAccount_ShouldHearOnlyThatItsOwnOrderChanged_ByItsId()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (_, tacos) = await StoreAsync(fixture);
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        var other = await AccountAsync(fixture, "otra@demo.com");
        SignIn(fixture, mariana);
        var token = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        var id = (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == token)).Id;
        var created = Assert.Single(mediator.Published.OfType<OrderCreatedEvent>());
        mediator.Published.Clear();

        // The account's request comes with another business in context: cancelling puts the order's, as every command
        // that changes an order does before saving, and the handler reads the order with it (ADR 0083).
        fixture.TenantService.SetTenant(Guid.NewGuid());
        Assert.True((await CancelMineAsync(fixture, id, new RecordingInventory())).IsSuccess);
        var accounts = new RecordingCustomerOrders();
        var handler = new OrderRealtimeNotifications(
            fixture.Context, new RecordingOrderScreens(), accounts, NullLogger<OrderRealtimeNotifications>.Instance);

        // Each kind of change reaches the account once: created, a change of status, cancelled and gone out.
        await handler.Handle(created, CancellationToken.None);
        Assert.Equal([(mariana.Id, id)], accounts.Changed);
        await handler.Handle(new OrderStatusChangedEvent(id, created.TenantId, OrderStatus.Pending, OrderStatus.Confirmed), CancellationToken.None);
        Assert.Equal(2, accounts.Changed.Count);
        await handler.Handle(Assert.Single(mediator.Published.OfType<OrderCancelledEvent>()), CancellationToken.None);
        Assert.Equal(3, accounts.Changed.Count);
        // The screens of the business fail to hear the trip (this fake refuses it): the account hears it all the same.
        await handler.Handle(new OrderDeliveryUpdatedEvent(id, created.TenantId, WentOut: true), CancellationToken.None);
        Assert.Equal(4, accounts.Changed.Count);

        Assert.All(accounts.Changed, heard => Assert.Equal((mariana.Id, id), heard));
        Assert.DoesNotContain(accounts.Changed, heard => heard.CustomerUserId == other.Id);
        // An event naming another business finds nothing: no account hears it.
        await handler.Handle(new OrderDeliveryUpdatedEvent(id, Guid.NewGuid(), WentOut: true), CancellationToken.None);
        Assert.Equal(4, accounts.Changed.Count);
        // Nor with another business in context: it fails closed, without telling anyone.
        fixture.TenantService.SetTenant(Guid.NewGuid());
        await handler.Handle(new OrderStatusChangedEvent(id, created.TenantId, OrderStatus.Confirmed, OrderStatus.Ready), CancellationToken.None);
        Assert.Equal(4, accounts.Changed.Count);
    }

    [Fact]
    public async Task TheAccountFailingToHear_ShouldNotStopTheBusinessScreens()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, tacos) = await StoreAsync(fixture);
        SignIn(fixture, await AccountAsync(fixture, "mariana@demo.com"));
        var token = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        var id = (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == token)).Id;
        var screens = new RecordingOrderScreens();
        var handler = new OrderRealtimeNotifications(
            fixture.Context, screens, new UnreachableAccounts(), NullLogger<OrderRealtimeNotifications>.Instance);

        await handler.Handle(new OrderCancelledEvent(id, tenant.Id, OrderStatus.Pending, "Sin existencias", ByCustomer: false), CancellationToken.None);

        Assert.Equal(id, Assert.Single(screens.Cancelled).OrderId);
    }

    /// <summary>The hub of the accounts is down.</summary>
    private sealed class UnreachableAccounts : ICustomerOrderNotificationService
    {
        public Task NotifyOrderChanged(Guid customerUserId, Guid orderId) => throw new InvalidOperationException("Sin conexión");
    }

    [Fact]
    public async Task TheAuditLog_ShouldSayTheCustomerCancelledIt_NeverTheirEmail()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, tacos) = await StoreAsync(fixture);
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);
        var mineToken = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        var mine = (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.TrackingToken == mineToken)).Id;
        var theirs = (await PlaceAsync(fixture, Pickup(tacos))).Value.TrackingToken!;
        await ChangeAsync(fixture, theirs, order => order.Cancel(
            OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "cajero@demo.com", "Sin existencias"));
        Assert.True((await CancelMineAsync(fixture, mine, new RecordingInventory())).IsSuccess);
        var events = mediator.Published.OfType<OrderCancelledEvent>().ToList();

        // The outbox runs each one as whoever caused it: the account's email for hers, the cashier for theirs.
        var audit = new OrderAuditHandlers(fixture.Context);
        foreach (var cancelled in events)
            await audit.HandleAsync(cancelled, new OutboxEventContext(
                Guid.NewGuid(), tenant.Id, Guid.NewGuid().ToString(), cancelled.ByCustomer ? "mariana@demo.com" : "cajero@demo.com",
                fixture.Clock.UtcNow, 1), CancellationToken.None);
        await fixture.Context.SaveChangesAsync();

        var entries = await fixture.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.Action == "OrderCancelled").ToListAsync();
        var hers = Assert.Single(entries, entry => entry.EntityId == mine.ToString());
        var cashiers = Assert.Single(entries, entry => entry.EntityId != mine.ToString());
        Assert.Equal((Order.CustomerPerformedBy, "cajero@demo.com"), (hers.PerformedBy, cashiers.PerformedBy));
        Assert.Contains("\"ByCustomer\":true", hers.Metadata!);
        Assert.Contains("\"ByCustomer\":false", cashiers.Metadata!);
        Assert.DoesNotContain(entries, entry => entry.PerformedBy.Contains("mariana"));
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

    /// <summary>The business accepts the order between the customer's read and their save, once.</summary>
    private sealed class AcceptedMeanwhile : SaveChangesInterceptor
    {
        private SalesTestFixture? _fixture;

        public bool Raced { get; private set; }

        public void Arm(SalesTestFixture fixture) => _fixture = fixture;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var cancelling = eventData.Context?.ChangeTracker.Entries<Order>().FirstOrDefault(e => e.Entity.Status == OrderStatus.Cancelled);
            if (_fixture is not { } fixture || cancelling is null)
                return result;
            _fixture = null;

            var options = (DbContextOptions<ApplicationDbContext>)eventData.Context!.GetService<IDbContextOptions>();
            await using var business = new SalesDbContext(options, fixture.TenantService);
            var order = await business.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == cancelling.Entity.Id, cancellationToken);
            order.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "cajero");
            await business.SaveChangesAsync(cancellationToken);
            Raced = true;
            throw new DbUpdateConcurrencyException("The business accepted it first.");
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
            OrderStatus status, OrderChannel channel, string? externalOrderNumber) => Task.CompletedTask;
        public Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus) => Task.CompletedTask;
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
