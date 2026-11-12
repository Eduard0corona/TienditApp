using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Reviews.Events;

/// <summary>
/// A customer account wrote its first review of a business (Fase 31K, ADR 0091): from the outbox, the members who get the
/// emails of new reviews are told. Only ids travel: what it says stays in the review. Editing it raises nothing.
/// </summary>
public sealed record BusinessReviewWrittenEvent(Guid ReviewId, Guid TenantId) : DomainEvent;
