namespace FoodParkDigital.Application.Inventory.DTOs;

public class ProductRecipeDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public bool ProductIsAvailable { get; set; }
    public List<ProductRecipeLineDto> Lines { get; set; } = [];
}

public class ProductRecipeLineDto
{
    public Guid RequirementId { get; set; }
    public Guid SupplyItemId { get; set; }
    public string SupplyItemName { get; set; } = null!;
    public string? SupplyCode { get; set; }
    public Guid? InventoryLocationId { get; set; }
    public string? InventoryLocationName { get; set; }
    public decimal Quantity { get; set; }
    public decimal WastePercent { get; set; }
    public decimal EffectiveQuantity { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}
