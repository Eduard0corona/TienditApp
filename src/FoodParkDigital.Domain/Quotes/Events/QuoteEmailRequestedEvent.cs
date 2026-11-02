using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Quotes.Events;

/// <summary>
/// An email of a quote was asked for (Fase 29D, ADR 0053 point 16): it is stored in the outbox with «Mandar por correo» and
/// goes out after it, with the quote as it reads then.
/// </summary>
public sealed record QuoteEmailRequestedEvent(Guid QuoteEmailId) : DomainEvent;
