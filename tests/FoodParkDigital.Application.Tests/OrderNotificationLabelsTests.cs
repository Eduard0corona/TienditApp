using System.Globalization;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>Customers read order states and totals in Spanish, whatever the server culture is.</summary>
public class OrderNotificationLabelsTests
{
    [Fact]
    public void EveryOrderStatus_ShouldHaveASpanishLabel()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
            Assert.NotEqual(status.ToString(), OrderStatusLabels.ToSpanish(status));

        Assert.Equal("En preparación", OrderStatusLabels.ToSpanish(OrderStatus.InPreparation));
    }

    [Fact]
    public void FormatMoney_ShouldNotDependOnTheServerCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("$1,234.50", OrderStatusLabels.FormatMoney(1234.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task StatusChangeTemplate_ShouldCarryTheSpanishStatus()
    {
        var tenantService = new FixedTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new InMemoryContext(options, tenantService);
        var order = CustomerOrder(tenantService.TenantId, "ORD-000001");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService();

        var handler = new OrderCustomerNotifications(context, whatsApp, NullLogger<OrderCustomerNotifications>.Instance);
        await handler.HandleAsync(
            new OrderStatusChangedEvent(order.Id, tenantService.TenantId, OrderStatus.Confirmed, OrderStatus.InPreparation),
            OutboxContext(tenantService.TenantId),
            CancellationToken.None);

        var parameters = Assert.Single(whatsApp.Templates);
        Assert.Equal(["ORD-000001", "En preparación"], parameters);
    }

    [Fact]
    public async Task CounterSaleClosedWhenPaid_ShouldNotMessageTheCustomer()
    {
        var tenantService = new FixedTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new InMemoryContext(options, tenantService);
        var order = new Order(tenantService.TenantId, Guid.NewGuid(), null, true, "ORD-000001",
            BusinessType.Retail, OrderType.Retail, "Karla", "+525511112222", null, null);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService();

        var closed = new OrderStatusChangedEvent(
            order.Id, tenantService.TenantId, OrderStatus.Confirmed, OrderStatus.Delivered, NotifyCustomer: false);
        var outboxContext = OutboxContext(tenantService.TenantId);
        await new OrderCustomerNotifications(context, whatsApp, NullLogger<OrderCustomerNotifications>.Instance)
            .HandleAsync(closed, outboxContext, CancellationToken.None);
        await new OrderAuditHandlers(context).HandleAsync(closed, outboxContext, CancellationToken.None);
        // The outbox dispatcher saves what the handlers added.
        await context.SaveChangesAsync();

        // The change is still audited, by the cashier who caused it; only the customer message is skipped.
        Assert.Empty(whatsApp.Templates);
        var audit = Assert.Single(await context.AuditLogs.ToListAsync());
        Assert.Equal("cajero@negocio.mx", audit.PerformedBy);
        Assert.Equal(outboxContext.OccurredAt, audit.Timestamp);
    }

    [Fact]
    public async Task TransientWhatsAppFailure_ShouldBeRetriedByTheOutbox()
    {
        var tenantService = new FixedTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new InMemoryContext(options, tenantService);
        var order = CustomerOrder(tenantService.TenantId, "ORD-000002");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var handler = new OrderCustomerNotifications(
            context,
            new RecordingWhatsAppService { NextResult = MessageSendResult.Failed("429", isTransient: true) },
            NullLogger<OrderCustomerNotifications>.Instance);

        // Throwing makes the dispatcher retry with backoff.
        await Assert.ThrowsAsync<TransientDeliveryException>(() => handler.HandleAsync(
            new OrderStatusChangedEvent(order.Id, tenantService.TenantId, OrderStatus.Confirmed, OrderStatus.Ready),
            OutboxContext(tenantService.TenantId),
            CancellationToken.None));
    }

    [Fact]
    public async Task RejectedWhatsAppMessage_ShouldNotBeRetried()
    {
        var tenantService = new FixedTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new InMemoryContext(options, tenantService);
        var order = CustomerOrder(tenantService.TenantId, "ORD-000003");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService { NextResult = MessageSendResult.Failed("BadRequest", isTransient: false) };

        await new OrderCustomerNotifications(context, whatsApp, NullLogger<OrderCustomerNotifications>.Instance).HandleAsync(
            new OrderStatusChangedEvent(order.Id, tenantService.TenantId, OrderStatus.Confirmed, OrderStatus.Ready),
            OutboxContext(tenantService.TenantId),
            CancellationToken.None);

        Assert.Single(whatsApp.Templates);
    }

    [Fact]
    public async Task CounterSaleWithAPhone_ShouldNotMessageTheCustomer_ByDefault()
    {
        var tenantService = new FixedTenantService();
        await using var context = NewContext(tenantService);
        var order = CounterOrder(tenantService.TenantId, BusinessType.Retail, OrderType.Retail);
        var events = await CreateAsync(context, order);
        order.TransitionTo(OrderStatus.Accepted, new RetailOrderStateMachine(), "cajero");
        order.Cancel(new RetailOrderStateMachine(), "cajero", "Se arrepintió");
        var whatsApp = new RecordingWhatsAppService();

        await DispatchAsync(context, whatsApp, tenantService.TenantId, [.. events, .. order.DomainEvents]);

        // The customer is at the counter and leaves with the ticket: every template would be a paid message.
        Assert.Empty(whatsApp.Templates);
    }

    [Fact]
    public async Task CounterSale_ShouldBeConfirmedOnce_WhenTheBusinessMessagesEveryOrder()
    {
        var tenantService = new FixedTenantService();
        await using var context = NewContext(tenantService);
        await UsePolicyAsync(context, tenantService.TenantId, OrderNotificationPolicy.AllOrders);
        var order = CounterOrder(tenantService.TenantId, BusinessType.Retail, OrderType.Retail);
        var events = await CreateAsync(context, order);
        var whatsApp = new RecordingWhatsAppService();

        await DispatchAsync(context, whatsApp, tenantService.TenantId, events);

        // The order starts confirmed: one confirmation, not also a "status changed to confirmed".
        Assert.Equal(["order_confirmed"], whatsApp.TemplateNames);
        // $50 with IVA added on top, the default for businesses without a tax policy.
        Assert.Equal(["ORD-000010", "$58.00"], Assert.Single(whatsApp.Templates));
    }

    [Fact]
    public async Task OrderWaitingForTheBusiness_ShouldBeConfirmedOnlyOnceTheBusinessAcceptsIt()
    {
        var tenantService = new FixedTenantService();
        await using var context = NewContext(tenantService);
        var order = new Order(tenantService.TenantId, null, Guid.NewGuid(), false, "ORD-000011",
            BusinessType.Food, OrderType.Retail, "Karla", "+525511112222", null, null, channel: OrderChannel.Online);
        order.AddItem(Guid.NewGuid(), "Taco", 2, 25m);
        order.MarkAsCreated();
        var created = order.DomainEvents.ToList();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService();

        await DispatchAsync(context, whatsApp, tenantService.TenantId, created);
        Assert.Empty(whatsApp.Templates);

        order.TransitionTo(OrderStatus.Confirmed, new FoodOrderStateMachine(), "cajero");
        await DispatchAsync(context, whatsApp, tenantService.TenantId, order.DomainEvents);

        Assert.Equal(["order_status_update"], whatsApp.TemplateNames);
        Assert.Equal(["ORD-000011", "Confirmada"], Assert.Single(whatsApp.Templates));
    }

    [Fact]
    public async Task ServiceTakenAtTheCounter_ShouldMessageTheCustomer_ByDefault()
    {
        // A repair or laundry: the customer leaves and comes back when it is ready.
        var tenantService = new FixedTenantService();
        await using var context = NewContext(tenantService);
        var order = CounterOrder(tenantService.TenantId, BusinessType.Service, OrderType.Service);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService();

        await DispatchAsync(context, whatsApp, tenantService.TenantId,
            [new OrderStatusChangedEvent(order.Id, tenantService.TenantId, OrderStatus.InProgress, OrderStatus.Ready)]);

        Assert.Equal(["order_status_update"], whatsApp.TemplateNames);
    }

    [Fact]
    public async Task BusinessWithMessagesOff_ShouldNotMessageEvenRemoteCustomers()
    {
        var tenantService = new FixedTenantService();
        await using var context = NewContext(tenantService);
        await UsePolicyAsync(context, tenantService.TenantId, OrderNotificationPolicy.Off);
        var order = CustomerOrder(tenantService.TenantId, "ORD-000012");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService();

        await DispatchAsync(context, whatsApp, tenantService.TenantId,
        [
            new OrderCreatedEvent(order.Id, tenantService.TenantId, order.OrderNumber, order.BusinessType, 50m),
            new OrderStatusChangedEvent(order.Id, tenantService.TenantId, OrderStatus.Confirmed, OrderStatus.Ready),
            new OrderCancelledEvent(order.Id, tenantService.TenantId, OrderStatus.Ready, "Sin existencias")
        ]);

        Assert.Empty(whatsApp.Templates);
    }

    [Fact]
    public async Task PolicyOfAnotherBusiness_ShouldNotApply()
    {
        var tenantService = new FixedTenantService();
        await using var context = NewContext(tenantService);
        // Another business turned messages off; this one keeps the default.
        await UsePolicyAsync(context, Guid.NewGuid(), OrderNotificationPolicy.Off);
        var order = CustomerOrder(tenantService.TenantId, "ORD-000013");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService();

        await DispatchAsync(context, whatsApp, tenantService.TenantId,
            [new OrderStatusChangedEvent(order.Id, tenantService.TenantId, OrderStatus.Confirmed, OrderStatus.Ready)]);

        Assert.Single(whatsApp.Templates);
    }

    /// <summary>Placed by the customer from a distance (their account): the default policy messages it.</summary>
    private static Order CustomerOrder(Guid tenantId, string orderNumber) =>
        new(tenantId, Guid.NewGuid(), null, false, orderNumber,
            BusinessType.Food, OrderType.Retail, "Karla", "+525511112222", null, null);

    /// <summary>Taken by the cashier at the counter, with the customer's phone.</summary>
    private static Order CounterOrder(Guid tenantId, BusinessType businessType, OrderType orderType)
    {
        var order = new Order(tenantId, null, null, true, "ORD-000010",
            businessType, orderType, "Karla", "+525511112222", null, null);
        order.AddItem(Guid.NewGuid(), "Producto", 1, 50m);
        return order;
    }

    /// <summary>What the sale commands do: the status the order starts in, then its creation, in one save.</summary>
    private static async Task<List<DomainEvent>> CreateAsync(InMemoryContext context, Order order)
    {
        order.AdvanceOnCreation(OrderStatus.Confirmed, OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "cajero");
        order.MarkAsCreated();
        var events = order.DomainEvents.ToList();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return events;
    }

    private static async Task UsePolicyAsync(InMemoryContext context, Guid tenantId, OrderNotificationPolicy policy)
    {
        var settings = new TenantSettings(tenantId);
        settings.SetOrderNotificationPolicy(policy);
        context.TenantSettings.Add(settings);
        await context.SaveChangesAsync();
    }

    /// <summary>Delivers the events to the handler as the outbox dispatcher does, one by one after the commit.</summary>
    private static async Task DispatchAsync(
        InMemoryContext context, RecordingWhatsAppService whatsApp, Guid tenantId, IEnumerable<DomainEvent> events)
    {
        var handler = new OrderCustomerNotifications(context, whatsApp, NullLogger<OrderCustomerNotifications>.Instance);
        foreach (var domainEvent in events.ToList())
        {
            var outboxContext = OutboxContext(tenantId);
            switch (domainEvent)
            {
                case OrderCreatedEvent created:
                    await handler.HandleAsync(created, outboxContext, CancellationToken.None);
                    break;
                case OrderStatusChangedEvent changed:
                    await handler.HandleAsync(changed, outboxContext, CancellationToken.None);
                    break;
                case OrderCancelledEvent cancelled:
                    await handler.HandleAsync(cancelled, outboxContext, CancellationToken.None);
                    break;
            }
        }
    }

    private static InMemoryContext NewContext(FixedTenantService tenantService) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenantService);

    private static OutboxEventContext OutboxContext(Guid tenantId)
        => new(Guid.NewGuid(), tenantId, "user-1", "cajero@negocio.mx", new DateTimeOffset(2026, 10, 4, 18, 0, 0, TimeSpan.Zero), 1);

    private sealed class InMemoryContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenantService)
        : ApplicationDbContext(options, tenantService, new NoOpMediator())
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // The in-memory provider does not generate SQL Server row versions.
            modelBuilder.Entity<Order>().Property(o => o.Version).IsRequired(false).ValueGeneratedNever();
        }
    }

    private sealed class RecordingWhatsAppService : IWhatsAppService
    {
        public List<string[]> Templates { get; } = [];
        public List<string> TemplateNames { get; } = [];
        public MessageSendResult NextResult { get; init; } = MessageSendResult.Sent();

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName,
            string languageCode, string[] parameters, CancellationToken cancellationToken = default)
        {
            Templates.Add(parameters);
            TemplateNames.Add(templateName);
            return Task.FromResult(NextResult);
        }

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message,
            CancellationToken cancellationToken = default) => Task.FromResult(MessageSendResult.Sent());
    }

    private sealed class FixedTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NoOpMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
    }
}
