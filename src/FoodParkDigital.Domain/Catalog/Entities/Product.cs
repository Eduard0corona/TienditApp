using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Catalog.Entities;

public class Product : AuditableEntity, ITenantScoped
{
    private Product() { }

    public Product(
        Guid tenantId,
        Guid categoryId,
        string name,
        string? description,
        decimal priceAmount,
        string priceCurrency,
        string? imageUrl = null,
        int? preparationTimeMinutes = null,
        string? sku = null,
        string? barcode = null,
        int sortOrder = 0,
        bool isFeatured = false,
        string? tags = null,
        decimal? compareAtPrice = null,
        decimal? taxRate = null,
        int? calories = null,
        string? allergens = null,
        int? stockQuantity = null,
        int? lowStockThreshold = null,
        int? maxQuantityPerOrder = null,
        UnitType? unit = null,
        TimeSpan? availableFromTime = null,
        TimeSpan? availableToTime = null,
        bool isTrackable = false,
        bool notifyCustomer = false)
    {
        TenantId = tenantId;
        CategoryId = categoryId;
        Name = name;
        Description = description;
        PriceAmount = priceAmount;
        PriceCurrency = priceCurrency;
        ImageUrl = imageUrl;
        PreparationTimeMinutes = preparationTimeMinutes;
        Sku = sku;
        Barcode = barcode;
        SortOrder = sortOrder;
        IsFeatured = isFeatured;
        Tags = tags;
        CompareAtPrice = compareAtPrice;
        TaxRate = taxRate;
        Calories = calories;
        Allergens = allergens;
        StockQuantity = stockQuantity;
        LowStockThreshold = lowStockThreshold;
        MaxQuantityPerOrder = maxQuantityPerOrder;
        Unit = unit;
        IsTrackable = isTrackable;
        NotifyCustomer = notifyCustomer;
        IsAvailable = true;
        SetAvailabilitySchedule(availableFromTime, availableToTime);
    }

    // Identity & Tenant
    public Guid TenantId { get; private set; }
    public Guid CategoryId { get; private set; }

    // Core info
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Sku { get; private set; }
    public string? Barcode { get; private set; }
    public int SortOrder { get; private set; }
    public string? Tags { get; private set; }
    public UnitType? Unit { get; private set; }

    // Pricing
    public decimal PriceAmount { get; private set; }
    public string PriceCurrency { get; private set; } = "USD";
    public decimal? CompareAtPrice { get; private set; }
    public decimal? TaxRate { get; private set; }

    // Media
    public string? ImageUrl { get; private set; }

    // Status
    public bool IsAvailable { get; private set; } = true;
    public bool IsFeatured { get; private set; }

    // Food-specific
    public int? PreparationTimeMinutes { get; private set; }
    public int? Calories { get; private set; }
    public string? Allergens { get; private set; }

    // Inventory (Retail)
    public int? StockQuantity { get; private set; }
    public int? LowStockThreshold { get; private set; }
    public int? MaxQuantityPerOrder { get; private set; }
    public TimeSpan? AvailableFromTime { get; private set; }
    public TimeSpan? AvailableToTime { get; private set; }
    public bool IsTrackable { get; private set; }
    public bool NotifyCustomer { get; private set; }

    // Navigation
    public Category Category { get; private set; } = null!;

    // All parameters are required on purpose: defaults let partial calls silently reset
    // fields. The image is not part of this update; it changes only through SetImage.
    public void Update(
        Guid categoryId,
        string name,
        string? description,
        decimal priceAmount,
        string priceCurrency,
        int? preparationTimeMinutes,
        string? sku,
        string? barcode,
        int sortOrder,
        bool isFeatured,
        string? tags,
        decimal? compareAtPrice,
        decimal? taxRate,
        int? calories,
        string? allergens,
        int? stockQuantity,
        int? lowStockThreshold,
        int? maxQuantityPerOrder,
        UnitType? unit,
        TimeSpan? availableFromTime,
        TimeSpan? availableToTime,
        bool isTrackable,
        bool notifyCustomer)
    {
        CategoryId = categoryId;
        Name = name;
        Description = description;
        PriceAmount = priceAmount;
        PriceCurrency = priceCurrency;
        PreparationTimeMinutes = preparationTimeMinutes;
        Sku = sku;
        Barcode = barcode;
        SortOrder = sortOrder;
        IsFeatured = isFeatured;
        Tags = tags;
        CompareAtPrice = compareAtPrice;
        TaxRate = taxRate;
        Calories = calories;
        Allergens = allergens;
        StockQuantity = stockQuantity;
        LowStockThreshold = lowStockThreshold;
        MaxQuantityPerOrder = maxQuantityPerOrder;
        Unit = unit;
        IsTrackable = isTrackable;
        NotifyCustomer = notifyCustomer;
        SetAvailabilitySchedule(availableFromTime, availableToTime);
    }

    public void SetImage(string? imageUrl) => ImageUrl = imageUrl;

    public void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;

    public void SetAvailabilitySchedule(TimeSpan? from, TimeSpan? to)
    {
        if (from.HasValue != to.HasValue)
            throw new DomainException("Debes especificar hora de inicio y fin para el horario de disponibilidad.");

        if (from.HasValue && from.Value == to!.Value)
            throw new DomainException("La hora de inicio y fin no pueden ser iguales.");

        AvailableFromTime = from;
        AvailableToTime = to;
    }

    public bool IsAvailableAt(TimeSpan localTimeOfDay)
    {
        if (!IsAvailable)
            return false;

        if (!AvailableFromTime.HasValue || !AvailableToTime.HasValue)
            return true;

        var from = AvailableFromTime.Value;
        var to = AvailableToTime.Value;

        if (from < to)
            return localTimeOfDay >= from && localTimeOfDay <= to;

        // Overnight windows (e.g. 22:00 -> 04:00)
        return localTimeOfDay >= from || localTimeOfDay <= to;
    }

    public bool IsLowStock => StockQuantity.HasValue
        && LowStockThreshold.HasValue
        && StockQuantity.Value <= LowStockThreshold.Value;

    public bool HasDiscount => CompareAtPrice.HasValue
        && CompareAtPrice.Value > PriceAmount;
}
