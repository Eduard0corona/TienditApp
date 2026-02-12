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
    public bool IsAvailable { get; set; }
    public int? PreparationTimeMinutes { get; set; }
}

public class CategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}
