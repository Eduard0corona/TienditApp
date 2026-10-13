using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Catalog.Entities;

/// <summary>
/// A choice offered with a product. A required single choice works as a variant (size, type of milk) and an
/// optional multiple choice as extras (cheese, bacon). Each chosen option adds its price to the product's.
/// </summary>
public class ProductOptionGroup : BaseEntity, ITenantScoped
{
    public const int MaxNameLength = 80;
    public const int MaxOptions = 30;

    private readonly List<ProductOption> _options = new();

    private ProductOptionGroup() { }

    internal ProductOptionGroup(Guid tenantId, Guid productId)
    {
        TenantId = tenantId;
        ProductId = productId;
    }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = null!;
    /// <summary>Options that must be chosen: 0 makes the group optional.</summary>
    public int MinSelections { get; private set; }
    /// <summary>Options that can be chosen at most: 1 makes it a single choice.</summary>
    public int MaxSelections { get; private set; }
    public int SortOrder { get; private set; }
    public IReadOnlyCollection<ProductOption> Options => _options.AsReadOnly();

    public bool IsRequired => MinSelections > 0;

    /// <summary>Applies a definition, keeping the ids of the options that still exist.</summary>
    internal void Apply(ProductOptionGroupDefinition definition, int sortOrder)
    {
        var name = definition.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            throw new DomainException($"Cada grupo de opciones necesita un nombre de máximo {MaxNameLength} caracteres.");
        if (definition.Options.Count == 0)
            throw new DomainException($"El grupo '{name}' necesita al menos una opción.");
        if (definition.Options.Count > MaxOptions)
            throw new DomainException($"El grupo '{name}' admite como máximo {MaxOptions} opciones.");
        if (definition.MinSelections < 0 || definition.MaxSelections < 1 || definition.MinSelections > definition.MaxSelections)
            throw new DomainException($"En '{name}' el mínimo debe ser de 0 en adelante y no mayor al máximo, que es de 1 en adelante.");
        if (definition.MaxSelections > definition.Options.Count)
            throw new DomainException($"En '{name}' no se pueden elegir más opciones de las que hay.");

        var duplicated = definition.Options
            .GroupBy(option => option.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicated is not null)
            throw new DomainException($"La opción '{duplicated.Key}' está repetida en '{name}'.");

        Name = name;
        MinSelections = definition.MinSelections;
        MaxSelections = definition.MaxSelections;
        SortOrder = sortOrder;

        var kept = new List<ProductOption>();
        for (var index = 0; index < definition.Options.Count; index++)
        {
            var optionDefinition = definition.Options[index];
            var option = optionDefinition.Id is { } id ? _options.FirstOrDefault(existing => existing.Id == id) : null;
            if (option is null)
            {
                option = new ProductOption(TenantId, Id);
                _options.Add(option);
            }

            option.Apply(optionDefinition, index);
            kept.Add(option);
        }

        _options.RemoveAll(option => !kept.Contains(option));
    }
}

/// <summary>One choice of a group, with what it adds to the price (negative for a cheaper variant).</summary>
public class ProductOption : BaseEntity, ITenantScoped
{
    public const int MaxNameLength = 80;
    public const decimal MaxPriceDelta = 100_000m;

    private readonly List<ProductOptionPlatformPrice> _platformPrices = new();

    private ProductOption() { }

    internal ProductOption(Guid tenantId, Guid groupId)
    {
        TenantId = tenantId;
        GroupId = groupId;
    }

    public Guid TenantId { get; private set; }
    public Guid GroupId { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal PriceDelta { get; private set; }
    public bool IsAvailable { get; private set; } = true;
    public int SortOrder { get; private set; }

    /// <summary>What the option adds in the app of each delivery platform where the business set it apart from the menu.</summary>
    public IReadOnlyCollection<ProductOptionPlatformPrice> PlatformPrices => _platformPrices.AsReadOnly();

    /// <summary>
    /// What the option adds in a sales channel: in the app of a delivery platform, what the business set for it there (what
    /// it adds in the menu when it set nothing); everywhere else, what it adds in the menu.
    /// </summary>
    public decimal PriceDeltaIn(OrderChannel channel) => OrderChannelRules.IsDeliveryPlatform(channel)
        ? _platformPrices.FirstOrDefault(price => price.Channel == channel)?.PriceDelta ?? PriceDelta
        : PriceDelta;

    /// <summary>What the option adds in the app of a delivery platform; null goes back to what it adds in the menu.</summary>
    internal void SetPlatformPriceDelta(OrderChannel channel, decimal? priceDelta)
    {
        var current = _platformPrices.FirstOrDefault(price => price.Channel == channel);
        if (priceDelta is not { } delta)
        {
            if (current is not null)
                _platformPrices.Remove(current);
            return;
        }

        if (!ProductOptionPlatformPrice.IsValid(delta))
        {
            throw new DomainException(
                $"El precio de '{Name}' en {OrderChannelRules.DisplayName(channel)} admite dos decimales y no puede pasar de {MaxPriceDelta:N0}.");
        }

        if (current is null)
            _platformPrices.Add(new ProductOptionPlatformPrice(TenantId, Id, channel, delta));
        else
            current.Change(delta);
    }

    internal void Apply(ProductOptionDefinition definition, int sortOrder)
    {
        var name = definition.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            throw new DomainException($"Cada opción necesita un nombre de máximo {MaxNameLength} caracteres.");
        if (Math.Abs(definition.PriceDelta) > MaxPriceDelta || decimal.Round(definition.PriceDelta, 2) != definition.PriceDelta)
            throw new DomainException($"El precio de '{name}' admite dos decimales y no puede pasar de {MaxPriceDelta:N0}.");

        Name = name;
        PriceDelta = definition.PriceDelta;
        IsAvailable = definition.IsAvailable;
        SortOrder = sortOrder;
    }
}

/// <summary>What the catalog editor sends for an option; <see cref="Id"/> keeps an existing one.</summary>
public sealed record ProductOptionDefinition(Guid? Id, string Name, decimal PriceDelta, bool IsAvailable = true);

/// <summary>What the catalog editor sends for a group; <see cref="Id"/> keeps an existing one.</summary>
public sealed record ProductOptionGroupDefinition(
    Guid? Id,
    string Name,
    int MinSelections,
    int MaxSelections,
    IReadOnlyList<ProductOptionDefinition> Options);
