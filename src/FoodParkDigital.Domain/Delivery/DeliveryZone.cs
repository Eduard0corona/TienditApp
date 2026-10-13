using System.Globalization;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Delivery;

/// <summary>
/// Where a business delivers and what it charges for it: a name, the postal codes it reaches, its delivery fee, the
/// smallest order it takes there and, optionally, from what order on the delivery is free. Amounts are what the customer
/// pays, tax included, and the minimum and the free delivery compare against the products of the order as the customer
/// sees them. A postal code belongs to one zone of the business at most (the database keeps it so too).
/// </summary>
public class DeliveryZone : AuditableEntity, ITenantScoped, IAggregateRoot
{
    public const int MaxNameLength = 60;
    /// <summary>The highest delivery fee: a guard against a figure typed wrong.</summary>
    public const decimal MaxFee = 10_000m;
    /// <summary>The highest minimum order or free delivery threshold: a guard against a figure typed wrong.</summary>
    public const decimal MaxOrderAmount = 100_000m;
    /// <summary>The postal codes of one zone: a whole city of Mexico rarely has more.</summary>
    public const int MaxPostalCodes = 300;
    /// <summary>The zones of one business.</summary>
    public const int MaxZonesPerBusiness = 30;

    public const string TenantRequiredMessage = "La zona necesita un negocio.";
    public const string NameRequiredMessage = "Escribe el nombre de la zona.";
    public const string NameTooLongMessage = "El nombre de la zona admite hasta 60 caracteres.";
    public const string FeeMessage = "El costo de envío va de $0 a $10,000, con dos decimales.";
    public const string MinimumOrderMessage = "El pedido mínimo va de $0 a $100,000, con dos decimales.";
    public const string FreeDeliveryFromMessage = "«Envío gratis desde» va de $0.01 a $100,000, con dos decimales.";
    public const string FreeDeliveryWithoutFeeMessage = "El envío ya es gratis: deja vacío «Envío gratis desde».";
    public const string PostalCodesRequiredMessage = "Agrega al menos un código postal.";
    public const string TooManyPostalCodesMessage = "Una zona lleva hasta 300 códigos postales.";
    public const string TooManyZonesMessage = "Un negocio tiene hasta 30 zonas de entrega.";

    private readonly List<DeliveryZonePostalCode> _postalCodes = new();

    private DeliveryZone() { }

    public DeliveryZone(Guid tenantId, DeliveryZoneFields fields)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException(TenantRequiredMessage);

        TenantId = tenantId;
        Apply(Normalize(fields));
        Revision = 1;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>What the customer pays for the delivery, tax included; 0 when it is free.</summary>
    public decimal Fee { get; private set; }

    /// <summary>The smallest order, in products, it delivers here; 0 when there is none.</summary>
    public decimal MinimumOrder { get; private set; }

    /// <summary>From this order on, in products, the delivery is free; null when it never is (or always is).</summary>
    public decimal? FreeDeliveryFrom { get; private set; }

    /// <summary>An inactive zone keeps its postal codes but reaches none of them.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Starts at 1 and goes up by one with every change; a change made over an older one is refused.</summary>
    public int Revision { get; private set; }

    /// <summary>In numeric order, whatever order the database read them in.</summary>
    public IReadOnlyCollection<DeliveryZonePostalCode> PostalCodes
        => _postalCodes.OrderBy(code => code.PostalCode, StringComparer.Ordinal).ToList().AsReadOnly();

    /// <summary>Takes <paramref name="fields"/>; false, and nothing changes, when they are what the zone already has.</summary>
    public bool Update(DeliveryZoneFields fields)
    {
        var zone = Normalize(fields);
        if (zone.Name == Name && zone.Fee == Fee && zone.MinimumOrder == MinimumOrder
            && zone.FreeDeliveryFrom == FreeDeliveryFrom && zone.IsActive == IsActive
            && zone.PostalCodes.SetEquals(_postalCodes.Select(existing => existing.PostalCode)))
            return false;

        Apply(zone);
        Revision++;
        return true;
    }

    /// <summary>Whether it delivers to <paramref name="postalCode"/> now: active and with that postal code.</summary>
    public bool Covers(string? postalCode)
        => IsActive && NormalizePostalCode(postalCode) is { } code && _postalCodes.Any(existing => existing.PostalCode == code);

    /// <summary>What the delivery of an order with <paramref name="productsTotal"/> in products costs here.</summary>
    public decimal FeeFor(decimal productsTotal)
        => FreeDeliveryFrom is { } freeFrom && productsTotal >= freeFrom ? 0m : Fee;

    /// <summary>Whether an order with <paramref name="productsTotal"/> in products reaches its minimum.</summary>
    public bool MeetsMinimum(decimal productsTotal) => productsTotal >= MinimumOrder;

    /// <summary>The postal code as it is stored (without spaces around), or null when it is blank.</summary>
    public static string? NormalizePostalCode(string? postalCode)
        => string.IsNullOrWhiteSpace(postalCode) ? null : postalCode.Trim();

    /// <summary>A postal code of Mexico: five digits.</summary>
    public static bool IsPostalCode(string? postalCode)
        => postalCode is { Length: DeliveryZonePostalCode.Length } && postalCode.All(char.IsAsciiDigit);

    /// <summary>The message for something that is not a postal code, with what was written.</summary>
    public static string NotAPostalCodeMessage(string written)
        => string.Create(CultureInfo.InvariantCulture, $"«{written}» no es un código postal: lleva 5 dígitos.");

    private static Normalized Normalize(DeliveryZoneFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var name = fields.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new DomainException(NameRequiredMessage);
        if (name.Length > MaxNameLength)
            throw new DomainException(NameTooLongMessage);
        if (!IsAmount(fields.Fee, MaxFee, allowZero: true))
            throw new DomainException(FeeMessage);
        if (!IsAmount(fields.MinimumOrder, MaxOrderAmount, allowZero: true))
            throw new DomainException(MinimumOrderMessage);
        if (fields.FreeDeliveryFrom is { } freeFrom && !IsAmount(freeFrom, MaxOrderAmount, allowZero: false))
            throw new DomainException(FreeDeliveryFromMessage);
        if (fields.FreeDeliveryFrom is not null && fields.Fee == 0)
            throw new DomainException(FreeDeliveryWithoutFeeMessage);

        var postalCodes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var written in fields.PostalCodes ?? [])
        {
            var code = NormalizePostalCode(written);
            if (code is null)
                continue;
            if (!IsPostalCode(code))
                throw new DomainException(NotAPostalCodeMessage(code));
            postalCodes.Add(code);
        }
        if (postalCodes.Count == 0)
            throw new DomainException(PostalCodesRequiredMessage);
        if (postalCodes.Count > MaxPostalCodes)
            throw new DomainException(TooManyPostalCodesMessage);

        return new Normalized(name, fields.Fee, fields.MinimumOrder, fields.FreeDeliveryFrom, fields.IsActive, postalCodes);
    }

    private void Apply(Normalized zone)
    {
        Name = zone.Name;
        Fee = zone.Fee;
        MinimumOrder = zone.MinimumOrder;
        FreeDeliveryFrom = zone.FreeDeliveryFrom;
        IsActive = zone.IsActive;

        // The ones it keeps stay as they are; only the ones taken out go and only the new ones come in.
        _postalCodes.RemoveAll(existing => !zone.PostalCodes.Contains(existing.PostalCode));
        foreach (var code in zone.PostalCodes.Where(code => _postalCodes.All(existing => existing.PostalCode != code)))
            _postalCodes.Add(new DeliveryZonePostalCode(TenantId, Id, code));
    }

    private static bool IsAmount(decimal amount, decimal max, bool allowZero)
        => (allowZero ? amount >= 0 : amount > 0) && amount <= max && decimal.Round(amount, 2) == amount;

    private sealed record Normalized(
        string Name, decimal Fee, decimal MinimumOrder, decimal? FreeDeliveryFrom, bool IsActive, SortedSet<string> PostalCodes);
}

/// <summary>What a zone is made of, as the business writes it: the postal codes in any order, repeated or with spaces.</summary>
public sealed record DeliveryZoneFields(
    string Name,
    decimal Fee,
    decimal MinimumOrder,
    decimal? FreeDeliveryFrom,
    IEnumerable<string> PostalCodes,
    bool IsActive = true);
