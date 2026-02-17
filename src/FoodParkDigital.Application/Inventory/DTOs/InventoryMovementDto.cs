using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Application.Inventory.DTOs;

public class InventoryMovementDto
{
    public Guid Id { get; set; }
    public string MovementNumber { get; set; } = null!;
    public InventoryMovementType MovementType { get; set; }
    public InventoryMovementReason MovementReason { get; set; }
    public DateTimeOffset MovementDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ExternalReference { get; set; }
    public string? CounterpartyName { get; set; }
    public string? DocumentNumber { get; set; }
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<InventoryMovementLineDto> Lines { get; set; } = [];
}

public class InventoryMovementLineDto
{
    public Guid Id { get; set; }
    public Guid SupplyItemId { get; set; }
    public string SupplyItemName { get; set; } = null!;
    public Guid InventoryLocationId { get; set; }
    public string InventoryLocationName { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public decimal QuantityBefore { get; set; }
    public decimal QuantityAfter { get; set; }
    public string? LotNumber { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? Notes { get; set; }
}
