using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>A round of an open tab went to the kitchen (the board refreshes).</summary>
public sealed record TabRoundSentEvent(Guid OrderId, Guid TenantId, int Round) : DomainEvent;
