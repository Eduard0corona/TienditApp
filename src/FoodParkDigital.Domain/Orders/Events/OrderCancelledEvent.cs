using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <param name="ByCustomer">
/// The customer cancelled it from their link or their account (Fase 29R, ADR 0072): nobody tells them by WhatsApp what they
/// did, and the business hears it was them. Missing in the events stored before, which read as the business's.
/// </param>
/// <param name="AtCustomerRequest">
/// The business cancelled it because its customer asked, by phone or by WhatsApp (Fase 31B, ADR 0084): whoever gets the
/// emails of the orders their customers cancel hears of it too. Said by whoever cancelled it. Missing in the events stored
/// before, which read as not asked.
/// </param>
public sealed record OrderCancelledEvent(
    Guid OrderId,
    Guid TenantId,
    OrderStatus PreviousStatus,
    string? Reason,
    bool ByCustomer = false,
    bool AtCustomerRequest = false) : DomainEvent;
