using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// The charge for going to the customer's address in the sale of an appointment at their home (Fase 22E), added once with
/// the sale (<see cref="Order.ChargeVisit"/>): what the visit was agreed for, a copy of the zone's name and the IVA rate of
/// the business when it was charged. It is one more concept of the total, like the delivery of an order.
/// </summary>
public class OrderVisitCharge : BaseEntity, ITenantScoped
{
    private OrderVisitCharge() { }

    internal OrderVisitCharge(Guid tenantId, Guid orderId, Guid appointmentId, string zoneName, decimal fee, VatRate feeRate)
    {
        TenantId = tenantId;
        OrderId = orderId;
        AppointmentId = appointmentId;
        ZoneName = zoneName;
        Fee = fee;
        FeeTaxRate = feeRate.Percent;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    /// <summary>The appointment it charges the visit of.</summary>
    public Guid AppointmentId { get; private set; }

    /// <summary>The zone of the visit, for the team.</summary>
    public string ZoneName { get; private set; } = null!;
    /// <summary>What the visit charges, with the order's tax policy like any price of it.</summary>
    public decimal Fee { get; private set; }
    /// <summary>The IVA rate of the business when the sale was made (the rate of a service, not of a product).</summary>
    public decimal FeeTaxRate { get; private set; }
    public VatRate FeeVatRate => VatRate.Of(FeeTaxRate);
}
