namespace FoodParkDigital.Application.Inventory.DTOs;

/// <summary>The stock of a product in the inventory ledger, by location.</summary>
public class ProductStockDto
{
    public Guid ProductId { get; set; }
    public bool TracksStock { get; set; }
    /// <summary>The inventory item that holds the product's stock.</summary>
    public Guid? StockItemId { get; set; }
    public decimal OnHand { get; set; }
    public decimal ReorderPoint { get; set; }
    public bool IsLowStock { get; set; }
    public List<ProductStockLocationDto> Locations { get; set; } = [];
}

public class ProductStockLocationDto
{
    public Guid InventoryLocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public decimal OnHand { get; set; }
}
