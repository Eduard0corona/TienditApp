using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>
/// The delivery of an order changed (Fase 22D): the courier's trip was written down or corrected, or the order went out with
/// the courier. The screens that show it read it again; the customer hears of it only when it went out.
/// </summary>
/// <param name="WentOut">True the first time the order goes out with the courier («En camino»).</param>
public sealed record OrderDeliveryUpdatedEvent(Guid OrderId, Guid TenantId, bool WentOut) : DomainEvent;
