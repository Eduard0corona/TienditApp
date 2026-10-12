using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Catalog.Entities;

/// <summary>
/// What a product costs in the app of a delivery platform (Rappi, Uber Eats or DiDi Food), when the business charges
/// there something else than in its menu. An order of that platform is priced with it, and without one with the menu's
/// price. Each order copies its prices, so changing this one never rewrites the orders already registered.
/// </summary>
public class ProductPlatformPrice : BaseEntity, ITenantScoped
{
    /// <summary>The highest price of a product in an app: a guard against a figure typed wrong, far above any menu.</summary>
    public const decimal MaxPrice = 1_000_000m;

    private ProductPlatformPrice() { }

    internal ProductPlatformPrice(Guid tenantId, Guid productId, OrderChannel channel, decimal price)
    {
        TenantId = tenantId;
        ProductId = productId;
        Channel = channel;
        Price = price;
    }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    /// <summary>The delivery platform whose app charges this price.</summary>
    public OrderChannel Channel { get; private set; }
    public decimal Price { get; private set; }

    /// <summary>More than zero, two decimals at most and no more than <see cref="MaxPrice"/>.</summary>
    internal static bool IsValid(decimal price) => price > 0 && price <= MaxPrice && decimal.Round(price, 2) == price;

    internal void Change(decimal price) => Price = price;
}

/// <summary>
/// What an option of a product (a variant or an extra) adds in the app of a delivery platform, when it is not what it adds
/// in the menu. Negative for a variant cheaper than the product, as in the menu.
/// </summary>
public class ProductOptionPlatformPrice : BaseEntity, ITenantScoped
{
    private ProductOptionPlatformPrice() { }

    internal ProductOptionPlatformPrice(Guid tenantId, Guid optionId, OrderChannel channel, decimal priceDelta)
    {
        TenantId = tenantId;
        OptionId = optionId;
        Channel = channel;
        PriceDelta = priceDelta;
    }

    public Guid TenantId { get; private set; }
    public Guid OptionId { get; private set; }
    /// <summary>The delivery platform whose app charges this.</summary>
    public OrderChannel Channel { get; private set; }
    public decimal PriceDelta { get; private set; }

    /// <summary>The same limits as what the option adds in the menu: two decimals and up to <see cref="ProductOption.MaxPriceDelta"/> either way.</summary>
    internal static bool IsValid(decimal priceDelta)
        => Math.Abs(priceDelta) <= ProductOption.MaxPriceDelta && decimal.Round(priceDelta, 2) == priceDelta;

    internal void Change(decimal priceDelta) => PriceDelta = priceDelta;
}
