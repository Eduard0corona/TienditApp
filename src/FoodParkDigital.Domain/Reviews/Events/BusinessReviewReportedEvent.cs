using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Reviews.Events;

/// <summary>
/// A business asked the platform to look at one of its reviews (Fase 32F, ADR 0098): from the outbox, the platform's admins are
/// told. Only ids travel: the reason and what the review says stay in the review.
/// </summary>
public sealed record BusinessReviewReportedEvent(Guid ReviewId, Guid TenantId) : DomainEvent;
