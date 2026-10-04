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
    public int? StockQuantity { get; set; }
    public int? LowStockThreshold { get; set; }
    public int? MaxQuantityPerOrder { get; set; }
    public TimeSpan? AvailableFromTime { get; set; }
    public TimeSpan? AvailableToTime { get; set; }
    public bool IsLowStock { get; set; }
    public bool IsTrackable { get; set; }
    public bool NotifyCustomer { get; set; }
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
    public bool IsActive { get; set; }
}
