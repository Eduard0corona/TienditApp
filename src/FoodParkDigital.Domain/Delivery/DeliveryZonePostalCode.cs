using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Delivery;

/// <summary>A postal code a delivery zone reaches. It belongs to one zone of the business at most.</summary>
public class DeliveryZonePostalCode : BaseEntity, ITenantScoped
{
    /// <summary>A postal code of Mexico has five digits.</summary>
    public const int Length = 5;

    private DeliveryZonePostalCode() { }

    internal DeliveryZonePostalCode(Guid tenantId, Guid deliveryZoneId, string postalCode)
    {
        TenantId = tenantId;
        DeliveryZoneId = deliveryZoneId;
        PostalCode = postalCode;
    }

    public Guid TenantId { get; private set; }
    public Guid DeliveryZoneId { get; private set; }
    public string PostalCode { get; private set; } = string.Empty;
}
