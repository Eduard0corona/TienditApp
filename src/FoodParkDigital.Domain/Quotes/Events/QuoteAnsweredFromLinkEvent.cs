using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Quotes.Events;

/// <summary>
/// The customer accepted or rejected a quote from its link (Fase 29O, ADR 0069): the business hears of it by email, from the
/// outbox, to the members who get those. Only ids and the answer travel: what the customer wrote stays in the quote.
/// </summary>
public sealed record QuoteAnsweredFromLinkEvent(Guid QuoteId, Guid TenantId, bool Accepted) : DomainEvent;
