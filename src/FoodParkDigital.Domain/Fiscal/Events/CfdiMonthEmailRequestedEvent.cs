using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Fiscal.Events;

/// <summary>
/// The CFDI of a month were asked to go to the accountant (Fase 28O, ADR 0060): it is stored in the outbox with the request,
/// and the file of the month is put together when it goes out, with the CFDI and XML there are then.
/// </summary>
public sealed record CfdiMonthEmailRequestedEvent(Guid CfdiMonthEmailId) : DomainEvent;
