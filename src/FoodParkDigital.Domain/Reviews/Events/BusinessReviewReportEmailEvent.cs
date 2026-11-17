using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Reviews.Events;

/// <summary>
/// The email of a reported review, to one admin of the platform (Fase 32F, ADR 0098). Each admin's is its own message in the
/// outbox, retried on its own: one that fails does not send the others' again.
/// </summary>
public sealed record BusinessReviewReportEmailEvent(Guid ReviewId, Guid UserId) : DomainEvent;
