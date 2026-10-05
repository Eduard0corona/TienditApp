using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

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
    /// <summary>
    /// The product this item is the stock of, when the product controls its own stock: each unit sold takes one unit of
    /// it (the product's recipe has it with quantity 1).
    /// </summary>
    public Guid? ProductId { get; private set; }

    public const string ProductStockMessage =
        "Este artículo es la existencia de un producto: deja de controlar sus existencias desde el producto.";

    /// <summary>The stock of a product that starts controlling its own: its name and unit, valued at its cost.</summary>
    public static SupplyItem ForProduct(
        Guid tenantId,
        Guid productId,
        string name,
        UnitType unit,
        string? barcode,
        decimal defaultCost,
        decimal reorderPoint)
    {
        if (unit.IsService())
            throw new DomainException("Un servicio no tiene existencias.");

        return new SupplyItem(tenantId, name, unit, barcode: barcode, defaultCost: defaultCost, reorderPoint: reorderPoint)
        {
            ProductId = productId
        };
    }

    public void LinkToProduct(Guid productId)
    {
        if (ProductId.HasValue && ProductId != productId)
            throw new DomainException("Este artículo ya es la existencia de otro producto.");
        if (!IsActive)
            throw new DomainException("El artículo está inactivo.");

        ProductId = productId;
    }

    /// <summary>The product stops controlling its stock: the item, its stock and its history stay as a supply.</summary>
    public void UnlinkFromProduct() => ProductId = null;

    /// <summary>The stock of a product goes by the product's name.</summary>
    public void RenameForProduct(string name)
    {
        if (!ProductId.HasValue)
            throw new DomainException("El artículo no es la existencia de un producto.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre es obligatorio.");

        Name = name.Trim();
    }

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
        if (ProductId.HasValue && unit != Unit)
            throw new DomainException("La unidad de la existencia de un producto es la del producto.");

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

    public void SetActive(bool isActive)
    {
        if (!isActive && ProductId.HasValue)
            throw new DomainException(ProductStockMessage);

        IsActive = isActive;
    }

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
