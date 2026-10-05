using System.Linq.Expressions;
using FoodParkDigital.Domain.Catalog.Entities;

namespace FoodParkDigital.Application.Catalog.DTOs;

/// <summary>
/// Products and categories as the API returns them. <see cref="AsDto"/> is <see cref="ToDto(Product)"/> written for SQL
/// (the products list); <c>CatalogMappingTests</c> keeps both equal.
/// </summary>
public static class CatalogMappings
{
    /// <summary>The medium image is the one lists and cards show.</summary>
    private const string MediumImageSuffix = "_md.webp";

    public static ProductDto ToDto(this Product product) => new()
    {
        Id = product.Id,
        CategoryId = product.CategoryId,
        Name = product.Name,
        Description = product.Description,
        Sku = product.Sku,
        Barcode = product.Barcode,
        SortOrder = product.SortOrder,
        Tags = product.Tags,
        Unit = product.Unit,
        PriceAmount = product.PriceAmount,
        PriceCurrency = product.PriceCurrency,
        CompareAtPrice = product.CompareAtPrice,
        TaxRate = product.TaxRate,
        IsTaxExempt = product.IsTaxExempt,
        HasDiscount = product.HasDiscount,
        SatProductCode = product.SatProductCode,
        SatUnitCode = product.SatUnitCode,
        TaxObject = product.TaxObject,
        ImageUrl = string.IsNullOrEmpty(product.ImageUrl) ? null : product.ImageUrl + MediumImageSuffix,
        Images = ProductImageUrlsDto.FromBaseUrl(product.ImageUrl),
        IsAvailable = product.IsAvailable,
        IsFeatured = product.IsFeatured,
        PreparationTimeMinutes = product.PreparationTimeMinutes,
        Calories = product.Calories,
        Allergens = product.Allergens,
        StockQuantity = product.StockQuantity,
        LowStockThreshold = product.LowStockThreshold,
        MaxQuantityPerOrder = product.MaxQuantityPerOrder,
        AvailableFromTime = product.AvailableFromTime,
        AvailableToTime = product.AvailableToTime,
        IsLowStock = product.IsLowStock,
        IsTrackable = product.IsTrackable,
        NotifyCustomer = product.NotifyCustomer,
        OptionGroups = product.OptionGroups
            .OrderBy(g => g.SortOrder)
            .Select(g => new ProductOptionGroupDto
            {
                Id = g.Id,
                Name = g.Name,
                MinSelections = g.MinSelections,
                MaxSelections = g.MaxSelections,
                Options = g.Options
                    .OrderBy(o => o.SortOrder)
                    .Select(o => new ProductOptionDto { Id = o.Id, Name = o.Name, PriceDelta = o.PriceDelta, IsAvailable = o.IsAvailable })
                    .ToList()
            })
            .ToList()
    };

    /// <summary><see cref="CategoryDto.ProductCount"/> is filled by the categories list.</summary>
    public static CategoryDto ToDto(this Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Description = category.Description,
        SortOrder = category.SortOrder,
        IsActive = category.IsActive
    };

    /// <summary>
    /// <see cref="ToDto(Product)"/> for SQL: the discount and low stock rules of the product are written as columns.
    /// </summary>
    public static readonly Expression<Func<Product, ProductDto>> AsDto = product => new ProductDto
    {
        Id = product.Id,
        CategoryId = product.CategoryId,
        Name = product.Name,
        Description = product.Description,
        Sku = product.Sku,
        Barcode = product.Barcode,
        SortOrder = product.SortOrder,
        Tags = product.Tags,
        Unit = product.Unit,
        PriceAmount = product.PriceAmount,
        PriceCurrency = product.PriceCurrency,
        CompareAtPrice = product.CompareAtPrice,
        TaxRate = product.TaxRate,
        IsTaxExempt = product.IsTaxExempt,
        // Same rule as Product.HasDiscount.
        HasDiscount = product.CompareAtPrice.HasValue && product.CompareAtPrice.Value > product.PriceAmount,
        SatProductCode = product.SatProductCode,
        SatUnitCode = product.SatUnitCode,
        TaxObject = product.TaxObject,
        ImageUrl = string.IsNullOrEmpty(product.ImageUrl) ? null : product.ImageUrl + MediumImageSuffix,
        Images = ProductImageUrlsDto.FromBaseUrl(product.ImageUrl),
        IsAvailable = product.IsAvailable,
        IsFeatured = product.IsFeatured,
        PreparationTimeMinutes = product.PreparationTimeMinutes,
        Calories = product.Calories,
        Allergens = product.Allergens,
        StockQuantity = product.StockQuantity,
        LowStockThreshold = product.LowStockThreshold,
        MaxQuantityPerOrder = product.MaxQuantityPerOrder,
        AvailableFromTime = product.AvailableFromTime,
        AvailableToTime = product.AvailableToTime,
        // Same rule as Product.IsLowStock.
        IsLowStock = product.StockQuantity.HasValue
            && product.LowStockThreshold.HasValue
            && product.StockQuantity.Value <= product.LowStockThreshold.Value,
        IsTrackable = product.IsTrackable,
        NotifyCustomer = product.NotifyCustomer,
        OptionGroups = product.OptionGroups
            .OrderBy(g => g.SortOrder)
            .Select(g => new ProductOptionGroupDto
            {
                Id = g.Id,
                Name = g.Name,
                MinSelections = g.MinSelections,
                MaxSelections = g.MaxSelections,
                Options = g.Options
                    .OrderBy(o => o.SortOrder)
                    .Select(o => new ProductOptionDto { Id = o.Id, Name = o.Name, PriceDelta = o.PriceDelta, IsAvailable = o.IsAvailable })
                    .ToList()
            })
            .ToList()
    };
}
