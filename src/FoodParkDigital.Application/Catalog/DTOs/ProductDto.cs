using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Fiscal;

namespace FoodParkDigital.Application.Catalog.DTOs;

public class ProductDto
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }

    // Core info
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public int SortOrder { get; set; }
    public string? Tags { get; set; }
    public UnitType? Unit { get; set; }

    // Pricing
    public decimal PriceAmount { get; set; }
    public string PriceCurrency { get; set; } = null!;
    public decimal? CompareAtPrice { get; set; }
    public decimal? TaxRate { get; set; }
    public bool IsTaxExempt { get; set; }
    public bool HasDiscount { get; set; }

    // Invoicing (CFDI 4.0): the product's own codes, null when it uses the generic ones.
    public string? SatProductCode { get; set; }
    public string? SatUnitCode { get; set; }
    public CfdiTaxObject TaxObject { get; set; }

    // Media
    public string? ImageUrl { get; set; }
    public ProductImageUrlsDto? Images { get; set; }

    // Status
    public bool IsAvailable { get; set; }
    public bool IsFeatured { get; set; }

    // Food-specific
    public int? PreparationTimeMinutes { get; set; }
    public int? Calories { get; set; }
    public string? Allergens { get; set; }

    // Inventory
    /// <summary>Quantity captured by hand before stock was controlled in the inventory ledger; it never moved.</summary>
    public int? StockQuantity { get; set; }
    /// <summary>Threshold captured by hand with <see cref="StockQuantity"/>.</summary>
    public int? LowStockThreshold { get; set; }
    public int? MaxQuantityPerOrder { get; set; }
    public TimeSpan? AvailableFromTime { get; set; }
    public TimeSpan? AvailableToTime { get; set; }
    /// <summary>The product controls its own stock in the inventory ledger: each unit sold takes one from it.</summary>
    public bool TracksStock { get; set; }
    /// <summary>The inventory item that holds the product's stock (its movements are in the kardex).</summary>
    public Guid? StockItemId { get; set; }
    /// <summary>On hand in every location, when it controls its stock.</summary>
    public decimal? OnHand { get; set; }
    public decimal? ReorderPoint { get; set; }
    /// <summary>It controls its stock and has no more than its reorder point (sold out included).</summary>
    public bool IsLowStock { get; set; }
    public bool IsTrackable { get; set; }
    public bool NotifyCustomer { get; set; }

    /// <summary>Variants and extras offered with the product.</summary>
    public List<ProductOptionGroupDto> OptionGroups { get; set; } = new();
}

public class ProductOptionGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    /// <summary>0 makes the group optional.</summary>
    public int MinSelections { get; set; }
    /// <summary>1 makes it a single choice.</summary>
    public int MaxSelections { get; set; }
    public List<ProductOptionDto> Options { get; set; } = new();
}

public class ProductOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    /// <summary>Added to the product price (negative for a cheaper variant).</summary>
    public decimal PriceDelta { get; set; }
    public bool IsAvailable { get; set; }
}

public class ProductImageUrlsDto
{
    public string Original { get; set; } = null!;
    public string Large { get; set; } = null!;
    public string Medium { get; set; } = null!;
    public string Small { get; set; } = null!;
    public string Thumbnail { get; set; } = null!;

    public static ProductImageUrlsDto? FromBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return null;

        return new ProductImageUrlsDto
        {
            Original = $"{baseUrl}_original.webp",
            Large = $"{baseUrl}_lg.webp",
            Medium = $"{baseUrl}_md.webp",
            Small = $"{baseUrl}_sm.webp",
            Thumbnail = $"{baseUrl}_xs.webp"
        };
    }
}

public class CategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    /// <summary>False when archived: it leaves the menus and lists, and can be restored.</summary>
    public bool IsActive { get; set; }
    /// <summary>Products and services in it (filled by the categories list).</summary>
    public int ProductCount { get; set; }
}
