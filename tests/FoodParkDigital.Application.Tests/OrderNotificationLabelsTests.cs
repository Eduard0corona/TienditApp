using System.Globalization;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
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
        var order = new Order(tenantService.TenantId, Guid.NewGuid(), null, true, "ORD-000001",
            BusinessType.Food, OrderType.Retail, "Karla", "+525511112222", null, null);
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
        var order = new Order(tenantService.TenantId, Guid.NewGuid(), null, true, "ORD-000002",
            BusinessType.Food, OrderType.Retail, "Karla", "+525511112222", null, null);
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
        var order = new Order(tenantService.TenantId, Guid.NewGuid(), null, true, "ORD-000003",
            BusinessType.Food, OrderType.Retail, "Karla", "+525511112222", null, null);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService { NextResult = MessageSendResult.Failed("BadRequest", isTransient: false) };

        await new OrderCustomerNotifications(context, whatsApp, NullLogger<OrderCustomerNotifications>.Instance).HandleAsync(
            new OrderStatusChangedEvent(order.Id, tenantService.TenantId, OrderStatus.Confirmed, OrderStatus.Ready),
            OutboxContext(tenantService.TenantId),
            CancellationToken.None);

        Assert.Single(whatsApp.Templates);
    }

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
        public MessageSendResult NextResult { get; init; } = MessageSendResult.Sent();

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName,
            string languageCode, string[] parameters, CancellationToken cancellationToken = default)
        {
            Templates.Add(parameters);
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
