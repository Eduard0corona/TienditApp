using System.Text.Json.Nodes;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Domain.Fiscal.Events;
using FoodParkDigital.Domain.Identity.Events;
using FoodParkDigital.Domain.OnlinePayments.Events;
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

    /// <summary>How long a test waits for work that runs in the background.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

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
        // The handler knows which attempt is the last one (a CFDI email ends there instead of staying on its way).
        Assert.Equal([false, true], host.Log.FlakyLastAttempts);
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
    public async Task SlowHandler_ShouldKeepTheLeaseOfItsBatch_SoAnotherWorkerRunsNothingTwice()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
        {
            services.Configure<BackgroundJobOptions>(options => options.Outbox.LeaseSeconds = 1);
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, SlowHandler>();
        });
        await host.CreateOrderAsync(TenantA);
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        // Claimed with the first one, it waits its turn in the batch.
        await host.CreateOrderAsync(TenantB);

        var first = host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        await host.Log.SlowStarted.Task.WaitAsync(Patience);
        // Far past the lease they were claimed with: only renewing it keeps another worker away.
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        await EventuallyAsync(async () => (await host.MessagesAsync()).All(m => m.LockedUntil > host.Clock.UtcNow));

        Assert.Equal(0, await host.Dispatcher.DispatchDueAsync(CancellationToken.None).WaitAsync(Patience));
        host.Log.SlowRelease.SetResult();
        Assert.Equal(2, await first.WaitAsync(Patience));

        Assert.Equal([TenantA, TenantB], host.Log.Runs.Select(r => r.TenantId));
        Assert.All(await host.MessagesAsync(), m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task MessageAnotherWorkerTook_ShouldStopItsHandler_AndBeLeftToThatWorker()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
        {
            services.Configure<BackgroundJobOptions>(options => options.Outbox.LeaseSeconds = 1);
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, SlowHandler>();
        });
        await host.CreateOrderAsync(TenantA);

        var first = host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        await host.Log.SlowStarted.Task.WaitAsync(Patience);
        // This instance stalled past its lease, and another one claimed the message.
        var takenUntil = await TakeAsAnotherWorkerAsync(host);

        Assert.Equal(1, await first.WaitAsync(Patience));
        Assert.True(host.Log.SlowCancelled);
        // Neither processed nor failed here: the worker that holds it finishes it.
        var message = Assert.Single(await host.MessagesAsync());
        Assert.Equal(((DateTimeOffset?)null, 0, (DateTimeOffset?)takenUntil), (message.ProcessedAt, message.Attempts, message.LockedUntil));
        Assert.Empty(await host.ConsumersAsync());
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
            new OrderCreatedEvent(Guid.NewGuid(), TenantA, "ORD-000002", BusinessType.Food, 350m, OrderStatus.Confirmed,
                OrderChannel.Rappi, "BC953", PlatformCollectedBy.Platform, ExternalOrderId: "1029384756102938475"),
            new OrderPlatformIdChangedEvent(Guid.NewGuid(), TenantA, OrderChannel.UberEats, "BC953", null,
                "3F9C2B1E-8D4A-4C6F-9B2E-1A7D5E0C3B84"),
            new OrderStatusChangedEvent(Guid.NewGuid(), TenantA, OrderStatus.Confirmed, OrderStatus.Ready, NotifyCustomer: false),
            new OrderCancelledEvent(Guid.NewGuid(), TenantA, OrderStatus.Pending, "Cliente canceló"),
            new ServiceOrderPieceStatusChangedEvent(Guid.NewGuid(), Guid.NewGuid(), TenantA,
                ServiceOrderPieceStatus.Accepted, ServiceOrderPieceStatus.InProgress),
            new OrderRefundedEvent(Guid.NewGuid(), TenantA, Guid.NewGuid(), 25.50m, OrderPaymentMethod.Cash, "Producto dañado"),
            new TabRoundSentEvent(Guid.NewGuid(), TenantA, 2),
            new KitchenTicketChangedEvent(Guid.NewGuid(), TenantA, 2, KitchenTicketStatus.Ready),
            new TabItemCancelledEvent(Guid.NewGuid(), TenantA, Guid.NewGuid(), Guid.NewGuid(), 2, "Arrachera (Término medio)", 0.255m, 99.42m,
                "Se pasó de cocción", ReturnedToStock: false),
            new OrderDeliveryUpdatedEvent(Guid.NewGuid(), TenantA, WentOut: true),
            new ServiceOrderPhotoAddedEvent(Guid.NewGuid(), TenantA, Guid.NewGuid(), ServicePhotoStage.After,
                $"/files/orders/{TenantA}/service-intake/202610/{Guid.NewGuid()}.webp"),
            new ServiceOrderPhotoRemovedEvent(Guid.NewGuid(), TenantA, Guid.NewGuid(), ServicePhotoStage.Before,
                $"/files/orders/{TenantA}/service-intake/202610/{Guid.NewGuid()}.webp",
                new DateTimeOffset(2026, 10, 17, 18, 0, 0, TimeSpan.FromHours(-6)), "Ana"),
            new VerificationCodeRequestedEvent(VerificationCodePurpose.PasswordReset, Email: "ana@demo.com"),
            new VerificationCodeRequestedEvent(VerificationCodePurpose.PhoneConfirmation, UserId: Guid.NewGuid()),
            new AccountEmailRequestedEvent(AccountEmailPurpose.EmailConfirmation, UserId: Guid.NewGuid()),
            new AccountEmailRequestedEvent(AccountEmailPurpose.PasswordReset, Email: "ana@demo.com"),
            new AccountEmailRequestedEvent(AccountEmailPurpose.SignUpWithExistingEmail, Email: "ana@demo.com"),
            new AccountEmailRequestedEvent(AccountEmailPurpose.TeamInvitation, UserId: Guid.NewGuid()),
            new OnlinePaymentRefundRequestedEvent(Guid.NewGuid(), TenantA),
            new CfdiEmailRequestedEvent(Guid.NewGuid())
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

    [Fact]
    public void OrderCreatedEventStoredBeforeItHadAChannel_ShouldReadAsACounterOrder()
    {
        // Messages waiting in the outbox during the deploy of the delivery platforms came from the channels before them.
        var current = new OrderCreatedEvent(Guid.NewGuid(), TenantA, "ORD-000001", BusinessType.Food, 50m, OrderStatus.Pending,
            OrderChannel.UberEats, "BC953", PlatformCollectedBy.Business);
        var payload = JsonNode.Parse(OutboxEventSerializer.Serialize(current))!.AsObject();
        Assert.True(payload.Remove("channel"));
        Assert.True(payload.Remove("externalOrderNumber"));
        Assert.True(payload.Remove("collectedBy"));

        var restored = Assert.IsType<OrderCreatedEvent>(
            OutboxEventSerializer.Deserialize(OutboxEventSerializer.TypeNameOf(current), payload.ToJsonString()));

        Assert.Equal(OrderChannel.Counter, restored.Channel);
        Assert.Null(restored.ExternalOrderNumber);
        Assert.Null(restored.CollectedBy);
        Assert.Equal(OrderStatus.Pending, restored.Status);
        Assert.Equal(current.OrderId, restored.OrderId);
    }

    [Fact]
    public void OrderCreatedEventStoredBeforeItHadALongId_ShouldReadWithoutOne()
    {
        // Messages waiting in the outbox during the deploy of Fase 22I: their orders had no long id.
        var current = new OrderCreatedEvent(Guid.NewGuid(), TenantA, "ORD-000001", BusinessType.Food, 50m, OrderStatus.Pending,
            OrderChannel.DidiFood, "4821", PlatformCollectedBy.Platform, ExternalOrderId: "1029384756102938475");
        var payload = JsonNode.Parse(OutboxEventSerializer.Serialize(current))!.AsObject();
        Assert.True(payload.Remove("externalOrderId"));

        var restored = Assert.IsType<OrderCreatedEvent>(
            OutboxEventSerializer.Deserialize(OutboxEventSerializer.TypeNameOf(current), payload.ToJsonString()));

        Assert.Null(restored.ExternalOrderId);
        Assert.Equal("4821", restored.ExternalOrderNumber);
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

    /// <summary>Waits, a little at a time, until the condition holds.</summary>
    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition never held.");
            await Task.Delay(50);
        }
    }

    /// <summary>Claims the only message as another instance would, retrying while this one renews it.</summary>
    private static async Task<DateTimeOffset> TakeAsAnotherWorkerAsync(OutboxTestHost host)
    {
        while (true)
        {
            try
            {
                return await host.InScopeAsync(async services =>
                {
                    var context = services.GetRequiredService<ApplicationDbContext>();
                    var message = await context.OutboxMessages.SingleAsync();
                    message.Claim(host.Clock.UtcNow, TimeSpan.FromMinutes(2));
                    await context.SaveChangesAsync();
                    return message.LockedUntil!.Value;
                });
            }
            catch (DbUpdateConcurrencyException)
            {
            }
        }
    }
}
