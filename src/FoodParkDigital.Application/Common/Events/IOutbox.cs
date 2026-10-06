using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Application.Common.Events;

/// <summary>
/// Adds a domain event to the outbox without an entity that raises it: work a request asks for but must not do, or
/// wait for, itself. The event is stored with the next <c>SaveChangesAsync</c> of the request's context, in its
/// transaction, and its <see cref="IOutboxEventHandler{TEvent}"/>s run after the commit.
/// </summary>
public interface IOutbox
{
    /// <param name="tenantId">The business the work belongs to (its handlers run in it), or null for work of an
    /// account, before or across businesses.</param>
    void Add(DomainEvent domainEvent, Guid? tenantId);
}
