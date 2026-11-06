using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Quotes.Events;

/// <summary>
/// The email of a customer's answer to a quote, to one member of its business (Fase 29O, ADR 0069). Each member's is its own
/// message in the outbox, retried on its own: one that fails does not send the others' again.
/// </summary>
public sealed record QuoteReplyEmailEvent(Guid QuoteId, Guid UserId) : DomainEvent;
