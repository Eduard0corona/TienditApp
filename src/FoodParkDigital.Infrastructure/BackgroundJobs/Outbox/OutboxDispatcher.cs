using System.Collections.Concurrent;
using FoodParkDigital.Application.Common.Diagnostics;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;

/// <summary>
/// Runs the durable handlers of committed domain events. Each message is claimed with a lease, renewed while the batch is
/// worked on (<see cref="LeaseKeeper{TItem}"/>, Fase 28K), every handler runs in its own scope with the tenant and actor of
/// the event, and a handler that succeeded is recorded so a retry skips it. Failures wait according to
/// <see cref="RetrySchedule"/> and are set aside after <see cref="BackgroundJobOptions.QueueOptions.MaxAttempts"/>.
/// </summary>
public sealed class OutboxDispatcher
{
    private static readonly ConcurrentDictionary<Type, IHandlerInvoker> Invokers = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly BackgroundJobOptions.QueueOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IOptions<BackgroundJobOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value.Outbox;
        _logger = logger;
    }

    /// <summary>Processes one batch of due messages.</summary>
    /// <returns>How many messages were claimed; zero when nothing was due.</returns>
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimDueAsync(cancellationToken);
        await using var leases = new LeaseKeeper<OutboxMessage>(
            _scopeFactory, _clock, TimeSpan.FromSeconds(_options.LeaseSeconds), claimed,
            (message, now, lease) => message.RenewLease(now, lease), message => message.Id, _logger, cancellationToken);
        foreach (var message in claimed)
            await DispatchAsync(message, leases, cancellationToken);

        return claimed.Count;
    }

    /// <summary>Deletes messages processed more than <c>RetentionDays</c> ago, with their consumer records.</summary>
    public async Task<int> DeleteProcessedAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = _clock.UtcNow.AddDays(-_options.RetentionDays);

        var expired = await context.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .OrderBy(m => m.ProcessedAt)
            .Take(500)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
            return 0;

        var ids = expired.Select(m => m.Id).ToList();
        var consumers = await context.OutboxMessageConsumers
            .Where(c => ids.Contains(c.MessageId))
            .ToListAsync(cancellationToken);
        context.OutboxMessageConsumers.RemoveRange(consumers);
        context.OutboxMessages.RemoveRange(expired);
        await context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<List<OutboxMessage>> ClaimDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = _clock.UtcNow;

        var candidates = await context.OutboxMessages
            .Where(m => m.ProcessedAt == null
                && m.DeadLetteredAt == null
                && m.NextAttemptAt <= now
                && (m.LockedUntil == null || m.LockedUntil < now))
            .OrderBy(m => m.CreatedAt)
            .Take(Math.Max(1, _options.BatchSize))
            .ToListAsync(cancellationToken);

        var claimed = new List<OutboxMessage>(candidates.Count);
        foreach (var message in candidates)
        {
            // The renewed LockToken is a concurrency token: if another instance claimed the message
            // first, this update matches no row and the message is skipped.
            message.Claim(now, TimeSpan.FromSeconds(_options.LeaseSeconds));
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                claimed.Add(message);
            }
            catch (DbUpdateConcurrencyException)
            {
                context.Entry(message).State = EntityState.Detached;
            }
        }

        return claimed;
    }

    private async Task DispatchAsync(OutboxMessage message, LeaseKeeper<OutboxMessage> leases, CancellationToken cancellationToken)
    {
        string? failure = null;
        var permanent = false;

        var domainEvent = Deserialize(message, ref failure);
        if (domainEvent is null)
        {
            permanent = true;
        }
        else
        {
            try
            {
                failure = await RunHandlersAsync(message, domainEvent, leases.TokenFor(message));
            }
            catch (OperationCanceledException) when (leases.IsLost(message) && !cancellationToken.IsCancellationRequested)
            {
                // Another instance holds it now and finishes it: the handlers that succeeded here are recorded, and it
                // skips them.
                return;
            }
        }

        if (!await leases.CompleteAsync(message, () => CompleteAsync(message, failure, permanent, cancellationToken), cancellationToken))
            _logger.LogWarning("Outbox message {MessageId} was claimed by another worker before it finished", message.Id);
    }

    private DomainEvent? Deserialize(OutboxMessage message, ref string? failure)
    {
        try
        {
            var domainEvent = OutboxEventSerializer.Deserialize(message.Type, message.Payload);
            if (domainEvent is null)
                failure = $"Unknown event type {message.Type}.";
            return domainEvent;
        }
        catch (Exception ex)
        {
            failure = FailureDescription.Describe(ex);
            return null;
        }
    }

    /// <returns>The first failure, or null when every handler succeeded (or had already).</returns>
    private async Task<string?> RunHandlersAsync(
        OutboxMessage message,
        DomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var handlerType = typeof(IOutboxEventHandler<>).MakeGenericType(domainEvent.GetType());
        var invoker = Invokers.GetOrAdd(
            domainEvent.GetType(),
            type => (IHandlerInvoker)Activator.CreateInstance(typeof(HandlerInvoker<>).MakeGenericType(type))!);

        List<string> handlerNames;
        HashSet<string> completed;
        await using (var lookupScope = _scopeFactory.CreateAsyncScope())
        {
            handlerNames = lookupScope.ServiceProvider.GetServices(handlerType)
                .Select(handler => HandlerName(handler!))
                .ToList();
            var context = lookupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            completed = (await context.OutboxMessageConsumers
                    .AsNoTracking()
                    .Where(c => c.MessageId == message.Id)
                    .Select(c => c.HandlerName)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
        }

        string? firstFailure = null;
        foreach (var handlerName in handlerNames.Where(name => !completed.Contains(name)))
        {
            // Each handler gets its own scope (and DbContext): a failed one leaves nothing half-tracked
            // for the next.
            await using var scope = _scopeFactory.CreateTenantScope(message.TenantId ?? Guid.Empty, message.ActorName, message.ActorUserId);
            var services = scope.ServiceProvider;

            var handler = services.GetServices(handlerType).First(h => HandlerName(h!) == handlerName)!;
            var context = services.GetRequiredService<ApplicationDbContext>();
            var eventContext = new OutboxEventContext(
                message.Id,
                message.TenantId,
                message.ActorUserId,
                message.ActorName,
                message.OccurredAt,
                message.Attempts + 1,
                Math.Max(1, _options.MaxAttempts));

            try
            {
                await invoker.InvokeAsync(handler, domainEvent, eventContext, cancellationToken);
                context.OutboxMessageConsumers.Add(new OutboxMessageConsumer(message.Id, handlerName, _clock.UtcNow));
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    LogRedactionFor(ex),
                    "Outbox handler {Handler} failed for message {MessageId} ({EventType}), attempt {Attempt}",
                    handlerName,
                    message.Id,
                    message.Type,
                    message.Attempts + 1);
                firstFailure ??= $"{handlerName}: {FailureDescription.Describe(ex)}";
            }
        }

        return firstFailure;
    }

    private async Task CompleteAsync(
        OutboxMessage message,
        string? failure,
        bool permanent,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Attach(message);
        var now = _clock.UtcNow;

        var eventType = message.Type[(message.Type.LastIndexOf('.') + 1)..];
        if (failure is null)
        {
            message.MarkProcessed(now);
            TienditAppMetrics.RecordOutboxMessage(MetricOutcomes.Processed, eventType, message.TenantId);
        }
        else
        {
            var deadLettered = message.MarkFailed(now, failure, permanent ? 1 : _options.MaxAttempts);
            TienditAppMetrics.RecordOutboxMessage(
                deadLettered ? MetricOutcomes.DeadLettered : MetricOutcomes.Failed, eventType, message.TenantId);
            if (deadLettered)
            {
                _logger.LogError(
                    "Outbox message {MessageId} ({EventType}) was set aside after {Attempts} attempts: {Failure}",
                    message.Id,
                    message.Type,
                    message.Attempts,
                    failure);
            }
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The lease expired and another instance claimed the message; it will finish it.
            _logger.LogWarning("Outbox message {MessageId} was claimed by another worker before it finished", message.Id);
        }
    }

    private Exception LogRedactionFor(Exception exception)
        => Services.LogRedaction.RedactDatabaseMessages(exception);

    private static string HandlerName(object handler) => handler.GetType().FullName!;

    private interface IHandlerInvoker
    {
        Task InvokeAsync(object handler, DomainEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken);
    }

    private sealed class HandlerInvoker<TEvent> : IHandlerInvoker
        where TEvent : DomainEvent
    {
        public Task InvokeAsync(object handler, DomainEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
            => ((IOutboxEventHandler<TEvent>)handler).HandleAsync((TEvent)domainEvent, context, cancellationToken);
    }
}
