using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

public sealed record OrderRefundedEvent(
    Guid OrderId,
    Guid TenantId,
    Guid RefundId,
    decimal Amount,
    OrderPaymentMethod Method,
    string Reason) : DomainEvent;
