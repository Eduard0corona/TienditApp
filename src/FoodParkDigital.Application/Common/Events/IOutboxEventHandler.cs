using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Application.Common.Events;

/// <summary>
/// Durable side effect of a domain event: audit, customer messages, read models. The event is stored in
/// the outbox in the same transaction as the change that raised it, and a background dispatcher runs every
/// handler after the commit, retrying with backoff until it succeeds.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Changes made through <c>IApplicationDbContext</c> without calling <c>SaveChangesAsync</c> are saved
/// by the dispatcher together with the record that this handler ran, so they apply once.</item>
/// <item>Calls to external services (WhatsApp, SMS) repeat if the process stops between the call and that
/// save: they are at-least-once.</item>
/// <item>Throw to retry. Return normally when retrying cannot help (WhatsApp not configured, a rejected
/// number) after logging why.</item>
/// <item>The scope runs with the tenant of the event and, as current user, whoever caused it.</item>
/// </list>
/// Realtime notifications (SignalR) stay as MediatR <c>INotificationHandler</c>s: they run right after the
/// commit, best-effort, because a late screen refresh is worthless.
/// </remarks>
public interface IOutboxEventHandler<in TEvent>
    where TEvent : DomainEvent
{
    Task HandleAsync(TEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken);
}

/// <summary>Where and by whom the event was raised, as recorded when it was committed.</summary>
/// <param name="Attempt">1 the first time the handler runs.</param>
/// <param name="MaxAttempts">The attempt after which a failing message is set aside and never run again.</param>
public sealed record OutboxEventContext(
    Guid MessageId,
    Guid? TenantId,
    string? ActorUserId,
    string? ActorName,
    DateTimeOffset OccurredAt,
    int Attempt,
    int MaxAttempts = int.MaxValue)
{
    /// <summary>A failure now sets the message aside: what the handler leaves pending stays so.</summary>
    public bool IsLastAttempt => Attempt >= MaxAttempts;
}
