using FoodParkDigital.Domain.Catalog.Enums;

namespace FoodParkDigital.Application.Inventory.DTOs;

public class SupplyItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Code { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? CategoryName { get; set; }
    public UnitType Unit { get; set; }
    public decimal DefaultCost { get; set; }
    public decimal LastCost { get; set; }
    public decimal AverageCost { get; set; }
    public decimal MinStock { get; set; }
    public decimal? MaxStock { get; set; }
    public decimal ReorderPoint { get; set; }
    public bool TrackBatches { get; set; }
    public bool TrackExpiration { get; set; }
    public bool IsPerishable { get; set; }
    public decimal? TaxRate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public decimal TotalOnHand { get; set; }
    public decimal TotalReserved { get; set; }
    public decimal TotalAvailable { get; set; }
    public bool IsLowStock { get; set; }
}
