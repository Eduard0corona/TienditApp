using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Catalog.Entities;

public class Product : AuditableEntity, ITenantScoped
{
    public const int MaxOptionGroups = 10;

    private readonly List<ProductOptionGroup> _optionGroups = new();
    private readonly List<ProductPlatformPrice> _platformPrices = new();

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
        int? maxQuantityPerOrder = null,
        UnitType? unit = null,
        TimeSpan? availableFromTime = null,
        TimeSpan? availableToTime = null,
        bool isTrackable = false,
        bool notifyCustomer = false,
        bool isTaxExempt = false)
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
        SetTax(taxRate, isTaxExempt);
        Calories = calories;
        Allergens = allergens;
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
    public string PriceCurrency { get; private set; } = "MXN";
    public decimal? CompareAtPrice { get; private set; }
    /// <summary>IVA rate in percent; null uses the business default rate.</summary>
    public decimal? TaxRate { get; private set; }
    /// <summary>Exempt from IVA ("exento"), which an invoice tells apart from a 0 % rate.</summary>
    public bool IsTaxExempt { get; private set; }

    // Invoicing (CFDI 4.0). Without codes of its own the product is invoiced with the generic product code and
    // the unit code of its unit.
    public string? SatProductCode { get; private set; }
    public string? SatUnitCode { get; private set; }
    public CfdiTaxObject TaxObject { get; private set; } = CfdiTaxObject.Subject;

    // Media
    public string? ImageUrl { get; private set; }

    // Status
    public bool IsAvailable { get; private set; } = true;
    public bool IsFeatured { get; private set; }

    // Food-specific
    public int? PreparationTimeMinutes { get; private set; }
    public int? Calories { get; private set; }
    public string? Allergens { get; private set; }

    // Retail
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
        int? maxQuantityPerOrder,
        UnitType? unit,
        TimeSpan? availableFromTime,
        TimeSpan? availableToTime,
        bool isTrackable,
        bool notifyCustomer,
        bool isTaxExempt)
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
        SetTax(taxRate, isTaxExempt);
        Calories = calories;
        Allergens = allergens;
        MaxQuantityPerOrder = maxQuantityPerOrder;
        Unit = unit;
        IsTrackable = isTrackable;
        NotifyCustomer = notifyCustomer;
        SetAvailabilitySchedule(availableFromTime, availableToTime);
    }

    private void SetTax(decimal? taxRate, bool isTaxExempt)
    {
        if (isTaxExempt && taxRate.HasValue)
            throw new DomainException("Un producto exento de IVA no lleva tasa.");

        TaxRate = taxRate.HasValue ? VatRate.Of(taxRate.Value).Percent : null;
        IsTaxExempt = isTaxExempt;
    }

    public void SetFiscalData(string? satProductCode, string? satUnitCode, CfdiTaxObject taxObject)
    {
        if (!Enum.IsDefined(taxObject))
            throw new DomainException("El objeto de impuesto no es válido.");
        if (taxObject == CfdiTaxObject.NotSubject && (TaxRate is > 0 || (TaxRate is null && !IsTaxExempt)))
        {
            // Not subject to tax means no IVA at all: the product must be exempt or at 0 %.
            throw new DomainException("Un producto que no es objeto de impuesto debe ser exento o con tasa 0 %.");
        }

        SatProductCode = SatCatalog.NormalizeProductCode(satProductCode);
        SatUnitCode = SatCatalog.NormalizeUnitCode(satUnitCode);
        TaxObject = taxObject;
    }

    /// <summary>Variants and extras offered with the product, in the order they are shown.</summary>
    public IReadOnlyCollection<ProductOptionGroup> OptionGroups => _optionGroups.AsReadOnly();

    /// <summary>
    /// Replaces the option groups. Groups and options sent with their id keep it, so carts and lines that refer
    /// to them stay valid; the ones left out are removed (sold lines keep their own copy).
    /// </summary>
    public void SetOptionGroups(IReadOnlyList<ProductOptionGroupDefinition> definitions)
    {
        if (definitions.Count > MaxOptionGroups)
            throw new DomainException($"Un producto admite como máximo {MaxOptionGroups} grupos de opciones.");

        var duplicated = definitions
            .GroupBy(group => group.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicated is not null)
            throw new DomainException($"El grupo de opciones '{duplicated.Key}' está repetido.");

        var kept = new List<ProductOptionGroup>();
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            var group = definition.Id is { } id ? _optionGroups.FirstOrDefault(existing => existing.Id == id) : null;
            if (group is null)
            {
                group = new ProductOptionGroup(TenantId, Id);
                _optionGroups.Add(group);
            }

            group.Apply(definition, index);
            kept.Add(group);
        }

        _optionGroups.RemoveAll(group => !kept.Contains(group));
    }

    /// <summary>What the product costs in the app of each delivery platform where the business set a price of its own.</summary>
    public IReadOnlyCollection<ProductPlatformPrice> PlatformPrices => _platformPrices.AsReadOnly();

    /// <summary>
    /// The price of one unit in a sales channel: in the app of a delivery platform, the price the business set for it there
    /// (the menu's when it set none); everywhere else, the menu's.
    /// </summary>
    public decimal PriceIn(OrderChannel channel) => OrderChannelRules.IsDeliveryPlatform(channel)
        ? _platformPrices.FirstOrDefault(price => price.Channel == channel)?.Price ?? PriceAmount
        : PriceAmount;

    /// <summary>
    /// Sets what the product costs in the app of a delivery platform; null goes back to the menu's price there. Orders
    /// already registered keep the price they copied.
    /// </summary>
    public void SetPlatformPrice(OrderChannel channel, decimal? price)
    {
        EnsureDeliveryPlatform(channel);
        var current = _platformPrices.FirstOrDefault(existing => existing.Channel == channel);
        if (price is not { } amount)
        {
            if (current is not null)
                _platformPrices.Remove(current);
            return;
        }

        if (!ProductPlatformPrice.IsValid(amount))
        {
            throw new DomainException(
                $"El precio de '{Name}' en {OrderChannelRules.DisplayName(channel)} debe ser mayor a 0 y de hasta {ProductPlatformPrice.MaxPrice:N0}, con dos decimales como máximo.");
        }

        if (current is null)
            _platformPrices.Add(new ProductPlatformPrice(TenantId, Id, channel, amount));
        else
            current.Change(amount);
    }

    /// <summary>
    /// Sets what one of the product's options adds in the app of a delivery platform; null goes back to what it adds in
    /// the menu there.
    /// </summary>
    public void SetOptionPlatformPrice(Guid optionId, OrderChannel channel, decimal? priceDelta)
    {
        EnsureDeliveryPlatform(channel);
        var option = _optionGroups.SelectMany(group => group.Options).FirstOrDefault(existing => existing.Id == optionId)
            ?? throw new DomainException($"Una de las opciones de '{Name}' ya no existe. Recarga e intenta de nuevo.");
        option.SetPlatformPriceDelta(channel, priceDelta);
    }

    private static void EnsureDeliveryPlatform(OrderChannel channel)
    {
        if (!OrderChannelRules.IsDeliveryPlatform(channel))
            throw new DomainException(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
    }

    /// <summary>
    /// The options chosen for a sale, checked against the catalog: they belong to the product, are available,
    /// each one is chosen once and every group gets between its minimum and maximum. Each one carries what it adds in
    /// <paramref name="channel"/> (in the menu, unless it is a delivery platform with a price of its own).
    /// </summary>
    public IReadOnlyList<OrderLineOption> ResolveOptions(IReadOnlyCollection<Guid> optionIds, OrderChannel channel = OrderChannel.Counter)
    {
        if (optionIds.Count != optionIds.Distinct().Count())
            throw new DomainException($"Una opción de '{Name}' está repetida.");

        var chosen = new List<OrderLineOption>();
        var remaining = optionIds.ToHashSet();
        foreach (var group in _optionGroups.OrderBy(group => group.SortOrder))
        {
            var picked = group.Options
                .Where(option => remaining.Contains(option.Id))
                .OrderBy(option => option.SortOrder)
                .ToList();
            remaining.ExceptWith(picked.Select(option => option.Id));

            var unavailable = picked.FirstOrDefault(option => !option.IsAvailable);
            if (unavailable is not null)
                throw new DomainException($"'{unavailable.Name}' de '{Name}' no está disponible.");
            if (picked.Count < group.MinSelections)
            {
                throw new DomainException(group.MinSelections == 1
                    ? $"Elige una opción de '{group.Name}' para '{Name}'."
                    : $"Elige al menos {group.MinSelections} opciones de '{group.Name}' para '{Name}'.");
            }
            if (picked.Count > group.MaxSelections)
            {
                throw new DomainException(group.MaxSelections == 1
                    ? $"Elige solo una opción de '{group.Name}' para '{Name}'."
                    : $"Elige como máximo {group.MaxSelections} opciones de '{group.Name}' para '{Name}'.");
            }

            chosen.AddRange(picked.Select(option => new OrderLineOption(option.Id, group.Name, option.Name, option.PriceDeltaIn(channel))));
        }

        if (remaining.Count > 0)
            throw new DomainException($"Una de las opciones elegidas ya no existe en '{Name}'. Actualiza el catálogo e intenta de nuevo.");

        return chosen;
    }

    /// <summary>
    /// Price of one unit in <paramref name="channel"/> with the options chosen (resolved for the same channel); never below
    /// zero.
    /// </summary>
    public decimal PriceWith(IReadOnlyCollection<OrderLineOption> options, OrderChannel channel = OrderChannel.Counter)
    {
        var price = PriceIn(channel) + options.Sum(option => option.PriceDelta);
        return price >= 0
            ? price
            : throw new DomainException($"Las opciones elegidas dejan '{Name}' con precio negativo.");
    }

    public string GetSatProductCode() => SatProductCode ?? SatCatalog.GenericProductCode;

    public string GetSatUnitCode() => SatUnitCode ?? UnitTypeRules.DefaultSatUnitCode(Unit);

    /// <summary>IVA of the product when sold by a business whose default rate is <paramref name="defaultTaxRate"/>.</summary>
    public VatRate GetVatRate(decimal defaultTaxRate) => VatRate.ForProduct(TaxRate, IsTaxExempt, defaultTaxRate);

    /// <summary>Moves the product to another category of the business (for example, before archiving its own).</summary>
    public void MoveToCategory(Guid categoryId)
    {
        if (categoryId == Guid.Empty)
            throw new DomainException("La categoría es obligatoria.");
        CategoryId = categoryId;
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

    public bool HasDiscount => CompareAtPrice.HasValue
        && CompareAtPrice.Value > PriceAmount;
}
