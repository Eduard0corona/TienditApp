using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Application.Inventory.DTOs;

public class InventoryKardexEntryDto
{
    public Guid MovementLineId { get; set; }
    public Guid MovementId { get; set; }
    public string MovementNumber { get; set; } = null!;
    public DateTimeOffset MovementDate { get; set; }
    public InventoryMovementType MovementType { get; set; }
    public InventoryMovementReason MovementReason { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ExternalReference { get; set; }
    public Guid SupplyItemId { get; set; }
    public string SupplyItemName { get; set; } = null!;
    public Guid InventoryLocationId { get; set; }
    public string InventoryLocationName { get; set; } = null!;
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal SignedQuantity { get; set; }
    public decimal QuantityBefore { get; set; }
    public decimal QuantityAfter { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
}
