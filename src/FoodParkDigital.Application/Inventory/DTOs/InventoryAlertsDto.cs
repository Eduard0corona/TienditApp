namespace FoodParkDigital.Application.Inventory.DTOs;

public class InventoryAlertsDto
{
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int MissingRecipeProductCount { get; set; }
    public List<InventoryAlertItemDto> Items { get; set; } = [];
}

public class InventoryAlertItemDto
{
    public Guid SupplyItemId { get; set; }
    public string SupplyItemName { get; set; } = null!;
    public Guid InventoryLocationId { get; set; }
    public string InventoryLocationName { get; set; } = null!;
    public decimal OnHandQuantity { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal Deficit { get; set; }
    public bool IsOutOfStock { get; set; }
}
