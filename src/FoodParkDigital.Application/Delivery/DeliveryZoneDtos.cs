using System.Linq.Expressions;
using FoodParkDigital.Domain.Delivery;

namespace FoodParkDigital.Application.Delivery;

/// <summary>A delivery zone as Configuración › Zonas de entrega shows it: what it charges and the postal codes it reaches.</summary>
public class DeliveryZoneDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>What the customer pays for the delivery, tax included; 0 when it is free.</summary>
    public decimal Fee { get; set; }

    /// <summary>The smallest order, in products, it delivers here; 0 when there is none.</summary>
    public decimal MinimumOrder { get; set; }

    /// <summary>From this order on, in products, the delivery is free; null when it never is.</summary>
    public decimal? FreeDeliveryFrom { get; set; }

    public bool IsActive { get; set; }

    /// <summary>The revision a change sends back; an older one is refused.</summary>
    public int Revision { get; set; }

    /// <summary>In numeric order.</summary>
    public List<string> PostalCodes { get; set; } = [];

    internal static readonly Expression<Func<DeliveryZone, DeliveryZoneDto>> Projection = zone => new DeliveryZoneDto
    {
        Id = zone.Id,
        Name = zone.Name,
        Fee = zone.Fee,
        MinimumOrder = zone.MinimumOrder,
        FreeDeliveryFrom = zone.FreeDeliveryFrom,
        IsActive = zone.IsActive,
        Revision = zone.Revision,
        PostalCodes = zone.PostalCodes.OrderBy(code => code.PostalCode).Select(code => code.PostalCode).ToList()
    };
}
