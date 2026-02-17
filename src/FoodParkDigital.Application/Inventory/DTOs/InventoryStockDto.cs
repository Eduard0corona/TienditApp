using FoodParkDigital.Domain.Catalog.Enums;

namespace FoodParkDigital.Application.Inventory.DTOs;

public class InventoryStockDto
{
    public Guid StockId { get; set; }
    public Guid SupplyItemId { get; set; }
    public string SupplyItemName { get; set; } = null!;
    public string? SupplyCode { get; set; }
    public string? SupplySku { get; set; }
    public string? SupplyBarcode { get; set; }
    public UnitType Unit { get; set; }
    public Guid InventoryLocationId { get; set; }
    public string InventoryLocationName { get; set; } = null!;
    public string InventoryLocationCode { get; set; } = null!;
    public decimal OnHandQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal MinStock { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal? MaxStock { get; set; }
    public decimal LastCost { get; set; }
    public DateTimeOffset? LastMovementAt { get; set; }
    public bool IsSupplyActive { get; set; }
    public bool IsLocationActive { get; set; }
    public bool IsLowStock { get; set; }
}
