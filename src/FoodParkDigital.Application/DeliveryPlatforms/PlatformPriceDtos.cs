using FoodParkDigital.Domain.Catalog.Enums;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>A product of the price list of a delivery platform: what it costs in the menu and in the platform's app.</summary>
public class PlatformPriceItemDto
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = null!;
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = null!;
    public UnitType? Unit { get; set; }
    /// <summary>A product the business turned off is listed too: its price is ready for when it sells again.</summary>
    public bool IsAvailable { get; set; }
    public decimal MenuPrice { get; set; }
    /// <summary>What it costs in the app; null sells it there at the menu's price.</summary>
    public decimal? PlatformPrice { get; set; }
    /// <summary>Its variants and extras, in the order they are shown.</summary>
    public List<PlatformPriceOptionGroupDto> OptionGroups { get; set; } = new();
}

public class PlatformPriceOptionGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public List<PlatformPriceOptionDto> Options { get; set; } = new();
}

public class PlatformPriceOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsAvailable { get; set; }
    /// <summary>What it adds in the menu (negative for a cheaper variant).</summary>
    public decimal MenuPriceDelta { get; set; }
    /// <summary>What it adds in the app; null adds there what it adds in the menu.</summary>
    public decimal? PlatformPriceDelta { get; set; }
}
