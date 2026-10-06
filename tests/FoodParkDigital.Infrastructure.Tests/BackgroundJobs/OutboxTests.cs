using System.Text.Json.Nodes;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Domain.Identity.Events;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

public class OutboxTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public async Task Saving_ShouldStoreTheEventInTheSameSave_WithTenantAndActor()
    {
        await using var host = OutboxTestHost.Create();

        var order = await host.CreateOrderAsync(TenantA, "cajero@negocio.mx");

        var message = Assert.Single(await host.MessagesAsync());
        Assert.Equal(typeof(OrderCreatedEvent).FullName, message.Type);
        Assert.Equal(TenantA, message.TenantId);
        Assert.Equal("cajero@negocio.mx", message.ActorName);
        Assert.Null(message.ProcessedAt);
        var stored = Assert.IsType<OrderCreatedEvent>(OutboxEventSerializer.Deserialize(message.Type, message.Payload));
        Assert.Equal(order.Id, stored.OrderId);
        Assert.Equal(message.Id, stored.EventId);
    }

    [Fact]
    public async Task RetriedSave_ShouldNotStoreTheSameEventTwice()
    {
        await using var host = OutboxTestHost.Create();

        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<FoodParkDigital.Application.Common.Interfaces.ICurrentTenantService>().SetTenant(TenantA);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var order = new FoodParkDigital.Domain.Orders.Entities.Order(TenantA, null, null, true, "ORD-1",
                BusinessType.Retail, OrderType.Retail, "Público en general", null, null, null);
            order.MarkAsCreated();
            context.Orders.Add(order);

            // A save that fails after the interceptor ran keeps the message tracked; the retry reuses it.
            var interceptor = services.GetRequiredService<OutboxSaveChangesInterceptor>();
            await interceptor.SavingChangesAsync(
                new Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData(null!, (_, _) => string.Empty, context),
                default);
            await context.SaveChangesAsync();
            return true;
        });

        Assert.Single(await host.MessagesAsync());
    }

    [Fact]
    public async Task Dispatch_ShouldRunEveryHandlerInTheTenantAndAsTheActor_AndMarkTheMessageProcessed()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, AuditingHandler>());
        await host.CreateOrderAsync(TenantA, "mesero@negocio.mx");

        Assert.Equal(1, await host.Dispatcher.DispatchDueAsync(CancellationToken.None));

        var run = Assert.Single(host.Log.Runs);
        Assert.Equal(TenantA, run.TenantId);
        Assert.Equal("mesero@negocio.mx", run.Actor);
        Assert.Equal(1, run.Attempt);
        // The handler did not save: the dispatcher saved its audit entry with the consumer record.
        var audit = Assert.Single(await host.AuditLogsAsync());
        Assert.Equal("mesero@negocio.mx", audit.PerformedBy);
        Assert.Single(await host.ConsumersAsync());
        var message = Assert.Single(await host.MessagesAsync());
        Assert.NotNull(message.ProcessedAt);
        Assert.Equal(0, await host.Dispatcher.DispatchDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FailedHandler_ShouldBeRetriedWithBackoff_WithoutRepeatingTheOnesThatSucceeded()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
        {
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, AuditingHandler>();
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, FlakyHandler>();
        });
        host.Log.FlakyFailuresLeft = 1;
        await host.CreateOrderAsync(TenantA);

        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        var failed = Assert.Single(await host.MessagesAsync());
        Assert.Null(failed.ProcessedAt);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal(host.Clock.UtcNow.Add(RetrySchedule.DelayAfter(1)), failed.NextAttemptAt);
        Assert.Contains("FlakyHandler", failed.LastError);
        Assert.Contains("TimeoutException", failed.LastError);

        // Not due yet.
        Assert.Equal(0, await host.Dispatcher.DispatchDueAsync(CancellationToken.None));

        host.Clock.Advance(RetrySchedule.DelayAfter(1));
        Assert.Equal(1, await host.Dispatcher.DispatchDueAsync(CancellationToken.None));

        Assert.Equal(1, host.Log.Runs.Count(r => r.Handler == nameof(AuditingHandler)));
        Assert.Equal([1, 2], host.Log.Runs.Where(r => r.Handler == nameof(FlakyHandler)).Select(r => r.Attempt));
        Assert.Single(await host.AuditLogsAsync());
        Assert.Equal(2, (await host.ConsumersAsync()).Count);
        Assert.NotNull(Assert.Single(await host.MessagesAsync()).ProcessedAt);
    }

    [Fact]
    public async Task MessageThatKeepsFailing_ShouldBeSetAsideAfterMaxAttempts()
    {
        await using var host = OutboxTestHost.Create(maxAttempts: 2, configure: services =>
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, FlakyHandler>());
        host.Log.FlakyFailuresLeft = 10;
        await host.CreateOrderAsync(TenantA);

        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        host.Clock.Advance(TimeSpan.FromHours(1));
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        var message = Assert.Single(await host.MessagesAsync());
        Assert.Equal(2, message.Attempts);
        Assert.NotNull(message.DeadLetteredAt);
        Assert.Null(message.ProcessedAt);
        host.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await host.Dispatcher.DispatchDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ClaimedMessage_ShouldNotBeDispatchedByAnotherWorkerUntilItsLeaseExpires()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, AuditingHandler>());
        await host.CreateOrderAsync(TenantA);

        // Another instance claimed it and is still working on it.
        await host.InScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            var message = await context.OutboxMessages.SingleAsync();
            message.Claim(host.Clock.UtcNow, TimeSpan.FromMinutes(2));
            await context.SaveChangesAsync();
            return true;
        });

        Assert.Equal(0, await host.Dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Empty(host.Log.Runs);

        host.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(1, await host.Dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Single(host.Log.Runs);
    }

    [Fact]
    public async Task EachMessage_ShouldRunInTheTenantOfItsEvent()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, AuditingHandler>());
        await host.CreateOrderAsync(TenantA);
        await host.CreateOrderAsync(TenantB);

        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(new[] { TenantA, TenantB }.Order(), host.Log.Runs.Select(r => r.TenantId).Order());
        var audits = await host.AuditLogsAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, a => a.TenantId == TenantA);
        Assert.Contains(audits, a => a.TenantId == TenantB);
    }

    [Fact]
    public async Task UnknownEventType_ShouldBeSetAsideAtOnce()
    {
        await using var host = OutboxTestHost.Create();
        await host.InScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            context.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), "FoodParkDigital.Domain.Removed.OldEvent", "{}",
                TenantA, null, null, host.Clock.UtcNow, host.Clock.UtcNow));
            await context.SaveChangesAsync();
            return true;
        });

        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        var message = Assert.Single(await host.MessagesAsync());
        Assert.NotNull(message.DeadLetteredAt);
        Assert.Contains("Unknown event type", message.LastError);
    }

    [Fact]
    public async Task Cleanup_ShouldDeleteOnlyMessagesProcessedBeforeTheRetention()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, AuditingHandler>());
        await host.CreateOrderAsync(TenantA);
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        await host.CreateOrderAsync(TenantA);

        host.Clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(1, await host.Dispatcher.DeleteProcessedAsync(CancellationToken.None));

        var remaining = Assert.Single(await host.MessagesAsync());
        Assert.Null(remaining.ProcessedAt);
        Assert.Empty(await host.ConsumersAsync());
    }

    [Fact]
    public async Task EventAddedWithoutAnEntity_ShouldBeStoredWithoutABusiness_WakeTheDispatcher_AndReachItsHandler()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
        {
            services.AddScoped<IOutbox, OutboxWriter>();
            services.AddSingleton<ReceivedCodeRequests>();
            services.AddScoped<IOutboxEventHandler<VerificationCodeRequestedEvent>, CodeRequestRecorder>();
        });
        var requested = new VerificationCodeRequestedEvent(VerificationCodePurpose.PasswordReset, Email: "ana@demo.com");

        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<IOutbox>().Add(requested, tenantId: null);
            return await services.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        });

        var message = Assert.Single(await host.MessagesAsync());
        Assert.Equal(requested.EventId, message.Id);
        Assert.Null(message.TenantId);
        // Committed outside a transaction: the dispatcher is woken at once instead of waiting for its poll.
        Assert.True(await host.Get<OutboxSignal>().WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(1, await host.Dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal([requested], host.Get<ReceivedCodeRequests>().Events);
    }

    [Fact]
    public void EveryDomainEvent_ShouldRoundTripThroughTheOutboxSerializer()
    {
        var events = new FoodParkDigital.Domain.Common.DomainEvent[]
        {
            new OrderCreatedEvent(Guid.NewGuid(), TenantA, "ORD-000001", BusinessType.Food, 123.45m, OrderStatus.Pending),
            new OrderStatusChangedEvent(Guid.NewGuid(), TenantA, OrderStatus.Confirmed, OrderStatus.Ready, NotifyCustomer: false),
            new OrderCancelledEvent(Guid.NewGuid(), TenantA, OrderStatus.Pending, "Cliente canceló"),
            new ServiceOrderPieceStatusChangedEvent(Guid.NewGuid(), Guid.NewGuid(), TenantA,
                ServiceOrderPieceStatus.Accepted, ServiceOrderPieceStatus.InProgress),
            new OrderRefundedEvent(Guid.NewGuid(), TenantA, Guid.NewGuid(), 25.50m, OrderPaymentMethod.Cash, "Producto dañado"),
            new TabRoundSentEvent(Guid.NewGuid(), TenantA, 2),
            new KitchenTicketChangedEvent(Guid.NewGuid(), TenantA, 2, KitchenTicketStatus.Ready),
            new VerificationCodeRequestedEvent(VerificationCodePurpose.PasswordReset, Email: "ana@demo.com"),
            new VerificationCodeRequestedEvent(VerificationCodePurpose.PhoneConfirmation, UserId: Guid.NewGuid())
        };

        foreach (var domainEvent in events)
        {
            var restored = OutboxEventSerializer.Deserialize(
                OutboxEventSerializer.TypeNameOf(domainEvent),
                OutboxEventSerializer.Serialize(domainEvent));
            Assert.Equal(domainEvent, restored);
        }

        // A new event type must be added above, so its payload is proven to deserialize.
        Assert.Equal(events.Select(e => e.GetType()).Distinct().Count(), OutboxEventSerializer.KnownEventTypes.Count);
    }

    [Fact]
    public void OrderCreatedEventStoredBeforeItHadAStatus_ShouldReadAsConfirmed()
    {
        // Messages waiting in the outbox during a deploy were confirmed orders as far as the old handler knew.
        var current = new OrderCreatedEvent(Guid.NewGuid(), TenantA, "ORD-000001", BusinessType.Food, 50m, OrderStatus.Pending);
        var payload = JsonNode.Parse(OutboxEventSerializer.Serialize(current))!.AsObject();
        Assert.True(payload.Remove("status"));

        var restored = Assert.IsType<OrderCreatedEvent>(
            OutboxEventSerializer.Deserialize(OutboxEventSerializer.TypeNameOf(current), payload.ToJsonString()));

        Assert.Equal(OrderStatus.Confirmed, restored.Status);
        Assert.Equal(current.OrderId, restored.OrderId);
    }

    private sealed class ReceivedCodeRequests
    {
        public List<VerificationCodeRequestedEvent> Events { get; } = [];
    }

    private sealed class CodeRequestRecorder(ReceivedCodeRequests received) : IOutboxEventHandler<VerificationCodeRequestedEvent>
    {
        public Task HandleAsync(VerificationCodeRequestedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        {
            received.Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
