using FoodParkDigital.Domain.Common;

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
        int? preparationTimeMinutes = null)
    {
        TenantId = tenantId;
        CategoryId = categoryId;
        Name = name;
        Description = description;
        PriceAmount = priceAmount;
        PriceCurrency = priceCurrency;
        ImageUrl = imageUrl;
        PreparationTimeMinutes = preparationTimeMinutes;
        IsAvailable = true;
    }

    public Guid TenantId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal PriceAmount { get; private set; }
    public string PriceCurrency { get; private set; } = "USD";
    public string? ImageUrl { get; private set; }
    public bool IsAvailable { get; private set; } = true;
    public int? PreparationTimeMinutes { get; private set; }

    public Category Category { get; private set; } = null!;

    public void Update(Guid categoryId, string name, string? description, decimal priceAmount, string priceCurrency, string? imageUrl, int? preparationTimeMinutes)
    {
        CategoryId = categoryId;
        Name = name;
        Description = description;
        PriceAmount = priceAmount;
        PriceCurrency = priceCurrency;
        ImageUrl = imageUrl;
        PreparationTimeMinutes = preparationTimeMinutes;
    }

    public void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;
}
