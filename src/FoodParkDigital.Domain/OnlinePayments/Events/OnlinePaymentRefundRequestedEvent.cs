using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.OnlinePayments.Events;

/// <summary>Money paid online has to go back to the customer: a handler asks Mercado Pago for it.</summary>
public sealed record OnlinePaymentRefundRequestedEvent(Guid RefundId, Guid TenantId) : DomainEvent;
