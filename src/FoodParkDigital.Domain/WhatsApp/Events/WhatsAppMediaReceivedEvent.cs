using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.WhatsApp.Events;

/// <summary>
/// A customer sent a photo or an audio by WhatsApp (Fase 31L, ADR 0092): from the outbox it is downloaded from WhatsApp and kept
/// in the business's private storage. Only ids travel: the media id of WhatsApp stays in the message.
/// </summary>
public sealed record WhatsAppMediaReceivedEvent(Guid MessageId, Guid TenantId) : DomainEvent;
