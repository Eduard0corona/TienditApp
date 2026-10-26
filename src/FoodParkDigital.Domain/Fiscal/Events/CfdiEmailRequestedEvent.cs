using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Fiscal.Events;

/// <summary>
/// An email of a CFDI was asked for (Fase 28E, ADR 0049): it is stored in the outbox with the change that asked for it (the
/// stamp, or «Enviar por correo») and goes out after it, with the XML and the PDF read then.
/// </summary>
public sealed record CfdiEmailRequestedEvent(Guid CfdiEmailId) : DomainEvent;
