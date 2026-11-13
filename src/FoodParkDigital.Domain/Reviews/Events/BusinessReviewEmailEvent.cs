using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Reviews.Events;

/// <summary>
/// The email of a new review, to one member of its business (Fase 31K, ADR 0091). Each member's is its own message in the
/// outbox, retried on its own: one that fails does not send the others' again.
/// </summary>
public sealed record BusinessReviewEmailEvent(Guid ReviewId, Guid UserId) : DomainEvent;
