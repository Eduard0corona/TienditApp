using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// Where an order goes and what its delivery costs (Fase 22C), created once with the order
/// (<see cref="Order.ArrangeDelivery"/>): the customer's address, a copy of the zone's name and the fee charged, with the
/// IVA rate of the business then. The fee is fixed: it is what the customer agreed to, even if the zone changes later.
/// The business may write down the courier's trip afterwards (<see cref="Order.RegisterCourierShipment"/>).
/// </summary>
public class OrderDelivery : BaseEntity, ITenantScoped
{
    private OrderDelivery() { }

    internal OrderDelivery(Guid tenantId, Guid orderId, DeliveryAddress address, string zoneName, decimal fee, VatRate feeRate)
    {
        TenantId = tenantId;
        OrderId = orderId;
        PostalCode = address.PostalCode;
        AddressLine = address.AddressLine;
        References = address.References;
        ZoneName = zoneName;
        Fee = fee;
        FeeTaxRate = feeRate.Percent;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }

    public string PostalCode { get; private set; } = null!;
    public string AddressLine { get; private set; } = null!;
    public string? References { get; private set; }

    /// <summary>The zone the postal code was in when the order was placed, for the team; the customer is not shown it.</summary>
    public string ZoneName { get; private set; } = null!;
    /// <summary>What the delivery charges, with the order's tax policy like any price of it; 0 when it was free.</summary>
    public decimal Fee { get; private set; }
    /// <summary>The IVA rate of the business when the order was placed (the rate of a delivery service, not of a dish).</summary>
    public decimal FeeTaxRate { get; private set; }
    public VatRate FeeVatRate => VatRate.Of(FeeTaxRate);

    public DeliveryCourier? Courier { get; private set; }
    /// <summary>The courier's folio of the trip, normalized; none until the business writes it down.</summary>
    public string? CourierFolio { get; private set; }
    public string? TrackingUrl { get; private set; }
    public DateTimeOffset? ShipmentRegisteredAt { get; private set; }
    public string? ShipmentRegisteredBy { get; private set; }

    /// <summary>Writes down (or corrects) the courier's trip. False when it is the one already written.</summary>
    internal bool RegisterShipment(CourierShipment shipment, DateTimeOffset registeredAt, string? registeredBy)
    {
        if (Courier == shipment.Courier && CourierFolio == shipment.Folio && TrackingUrl == shipment.TrackingUrl)
            return false;

        Courier = shipment.Courier;
        CourierFolio = shipment.Folio;
        TrackingUrl = shipment.TrackingUrl;
        ShipmentRegisteredAt = registeredAt;
        ShipmentRegisteredBy = registeredBy;
        return true;
    }
}
