using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid TenantId,
    string OrderNumber,
    BusinessType BusinessType,
    decimal Total) : DomainEvent;
