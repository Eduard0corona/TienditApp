namespace FoodParkDigital.Application.Catalog.DTOs;

public class ProductDto
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal PriceAmount { get; set; }
    public string PriceCurrency { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public ProductImageUrlsDto? Images { get; set; }
    public bool IsAvailable { get; set; }
    public int? PreparationTimeMinutes { get; set; }
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
