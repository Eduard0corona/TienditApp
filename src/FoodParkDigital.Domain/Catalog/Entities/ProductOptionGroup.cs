using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

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
