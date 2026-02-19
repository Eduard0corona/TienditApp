using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Inventory.Entities;

public class SupplyItem : AuditableEntity, ITenantScoped
{
    private SupplyItem() { }

    public SupplyItem(
        Guid tenantId,
        string name,
        UnitType unit,
        string? code = null,
        string? sku = null,
        string? barcode = null,
        string? description = null,
        string? brand = null,
        string? model = null,
        string? categoryName = null,
        decimal defaultCost = 0,
        decimal minStock = 0,
        decimal? maxStock = null,
        decimal reorderPoint = 0,
        bool trackBatches = false,
        bool trackExpiration = false,
        bool isPerishable = false,
        bool isCritical = false,
        decimal? taxRate = null,
        string? notes = null)
    {
        TenantId = tenantId;
        Name = name;
        Unit = unit;
        Code = code;
        Sku = sku;
        Barcode = barcode;
        Description = description;
        Brand = brand;
        Model = model;
        CategoryName = categoryName;
        DefaultCost = defaultCost;
        LastCost = defaultCost;
        AverageCost = defaultCost;
        MinStock = minStock;
        MaxStock = maxStock;
        ReorderPoint = reorderPoint;
        TrackBatches = trackBatches;
        TrackExpiration = trackExpiration;
        IsPerishable = isPerishable;
        IsCritical = isCritical;
        TaxRate = taxRate;
        Notes = notes;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Code { get; private set; }
    public string? Sku { get; private set; }
    public string? Barcode { get; private set; }
    public string? Description { get; private set; }
    public string? Brand { get; private set; }
    public string? Model { get; private set; }
    public string? CategoryName { get; private set; }
    public UnitType Unit { get; private set; }
    public decimal DefaultCost { get; private set; }
    public decimal LastCost { get; private set; }
    public decimal AverageCost { get; private set; }
    public decimal MinStock { get; private set; }
    public decimal? MaxStock { get; private set; }
    public decimal ReorderPoint { get; private set; }
    public bool TrackBatches { get; private set; }
    public bool TrackExpiration { get; private set; }
    public bool IsPerishable { get; private set; }
    public bool IsCritical { get; private set; }
    public decimal? TaxRate { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(
        string name,
        UnitType unit,
        string? code = null,
        string? sku = null,
        string? barcode = null,
        string? description = null,
        string? brand = null,
        string? model = null,
        string? categoryName = null,
        decimal defaultCost = 0,
        decimal minStock = 0,
        decimal? maxStock = null,
        decimal reorderPoint = 0,
        bool trackBatches = false,
        bool trackExpiration = false,
        bool isPerishable = false,
        bool isCritical = false,
        decimal? taxRate = null,
        string? notes = null)
    {
        Name = name;
        Unit = unit;
        Code = code;
        Sku = sku;
        Barcode = barcode;
        Description = description;
        Brand = brand;
        Model = model;
        CategoryName = categoryName;
        DefaultCost = defaultCost;
        MinStock = minStock;
        MaxStock = maxStock;
        ReorderPoint = reorderPoint;
        TrackBatches = trackBatches;
        TrackExpiration = trackExpiration;
        IsPerishable = isPerishable;
        IsCritical = isCritical;
        TaxRate = taxRate;
        Notes = notes;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void ApplyInboundCost(decimal unitCost, decimal currentOnHand, decimal quantityIn)
    {
        LastCost = unitCost;

        var denominator = currentOnHand + quantityIn;
        if (denominator <= 0)
        {
            AverageCost = unitCost;
            return;
        }

        var weightedTotal = (AverageCost * currentOnHand) + (unitCost * quantityIn);
        AverageCost = weightedTotal / denominator;
    }
}
