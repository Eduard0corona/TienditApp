namespace FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;

/// <summary>
/// A domain event committed with the change that raised it, waiting for its durable handlers
/// (<c>IOutboxEventHandler</c>). The id is the event id, so the same event is never stored twice.
/// </summary>
/// <remarks>
/// Infrastructure only: the dispatcher reads it across tenants by design and no API exposes it. Each
/// message keeps its <see cref="TenantId"/>, which becomes the tenant of the scope that runs its handlers.
/// </remarks>
public sealed class OutboxMessage : QueueItem
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(
        Guid eventId,
        string type,
        string payload,
        Guid? tenantId,
        string? actorUserId,
        string? actorName,
        DateTimeOffset occurredAt,
        DateTimeOffset createdAt)
    {
        Id = eventId;
        Type = type;
        Payload = payload;
        TenantId = tenantId;
        ActorUserId = actorUserId;
        ActorName = actorName;
        OccurredAt = occurredAt;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
    }

    /// <summary>Full name of the event type (<c>FoodParkDigital.Domain.Orders.Events.OrderCreatedEvent</c>).</summary>
    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;
    public Guid? TenantId { get; private set; }
    public string? ActorUserId { get; private set; }
    public string? ActorName { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>A handler that already ran for a message; retries skip it.</summary>
public sealed class OutboxMessageConsumer
{
    private OutboxMessageConsumer()
    {
    }

    public OutboxMessageConsumer(Guid messageId, string handlerName, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        HandlerName = handlerName;
        ProcessedAt = processedAt;
    }

    public Guid MessageId { get; private set; }
    public string HandlerName { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }
}
