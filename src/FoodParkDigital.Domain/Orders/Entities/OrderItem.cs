using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;

namespace FoodParkDigital.Domain.Orders.Entities;

public class OrderItem : BaseEntity, ITenantScoped
{
    private readonly List<OrderItemOption> _options = new();

    private OrderItem() { }

    /// <param name="unitPrice">Price of one unit with the chosen options.</param>
    /// <param name="fiscal">Unit and invoicing codes of the product; a generic piece when omitted.</param>
    /// <param name="options">Variants and extras chosen for the line, with the price each one added.</param>
    /// <param name="round">Round of an open tab the line belongs to (1 for any other sale).</param>
    public OrderItem(
        Guid orderId,
        Guid productId,
        string productName,
        decimal quantity,
        decimal unitPrice,
        string? notes,
        VatRate vatRate,
        OrderLineFiscalData? fiscal = null,
        IReadOnlyList<OrderLineOption>? options = null,
        Guid tenantId = default,
        int round = 1)
    {
        fiscal ??= OrderLineFiscalData.Default;
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (!UnitTypeRules.IsValidQuantity(quantity, fiscal.Unit))
            throw new DomainException(UnitTypeRules.AllowsFractions(fiscal.Unit)
                ? $"La cantidad de '{productName}' admite como máximo {UnitTypeRules.QuantityDecimals} decimales."
                : $"'{productName}' se vende por pieza: la cantidad debe ser un número entero.");
        if (unitPrice < 0)
            throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));

        TenantId = tenantId;
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        // Weighed or measured lines can fall between cents: rounded once, with the same banker's rounding as IVA.
        TotalPrice = decimal.Round(quantity * unitPrice, 2, MidpointRounding.ToEven);
        Notes = notes;
        TaxRate = vatRate.Percent;
        IsTaxExempt = vatRate.IsExempt;
        Unit = fiscal.Unit;
        SatProductCode = fiscal.SatProductCode;
        SatUnitCode = fiscal.SatUnitCode;
        TaxObject = fiscal.TaxObject;
        Round = round >= 1 ? round : throw new ArgumentException("The round starts at 1.", nameof(round));
        foreach (var option in options ?? [])
            _options.Add(new OrderItemOption(tenantId, Id, option));
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = null!;
    /// <summary>Up to three decimals for weight, volume, length and time; whole for everything else.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Quantity given back with refunds; the sold quantity is never edited.</summary>
    public decimal ReturnedQuantity { get; private set; }

    /// <summary>
    /// Units of a dish of an open tab cancelled after it went to the kitchen (see <see cref="OrderItemCancellation"/>):
    /// the tab no longer charges them, and what was ordered stays as it was.
    /// </summary>
    public decimal CancelledQuantity { get; private set; }

    /// <summary>What the cancelled units took off the line, at the line's prices (before IVA is added, as <see cref="TotalPrice"/>).</summary>
    public decimal CancelledAmount { get; private set; }

    /// <summary>Units the order still charges and that left with the customer.</summary>
    public decimal ActiveQuantity => Quantity - CancelledQuantity;

    /// <summary>What the line charges: <see cref="TotalPrice"/> less its cancelled units.</summary>
    public decimal ChargedPrice => TotalPrice - CancelledAmount;

    public decimal ReturnableQuantity => Quantity - ReturnedQuantity - CancelledQuantity;
    public decimal UnitPrice { get; private set; }
    public decimal TotalPrice { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>IVA rate in percent applied to the line (0 when exempt).</summary>
    public decimal TaxRate { get; private set; }
    public bool IsTaxExempt { get; private set; }

    /// <summary>Unit the line was sold in.</summary>
    public UnitType Unit { get; private set; }
    /// <summary>CFDI ClaveProdServ at the time of the sale.</summary>
    public string SatProductCode { get; private set; } = SatCatalog.GenericProductCode;
    /// <summary>CFDI ClaveUnidad at the time of the sale.</summary>
    public string SatUnitCode { get; private set; } = "H87";
    public CfdiTaxObject TaxObject { get; private set; } = CfdiTaxObject.Subject;

    public VatRate VatRate => new(TaxRate, IsTaxExempt);

    /// <summary>Round of an open tab the line was ordered in; 1 for every other sale.</summary>
    public int Round { get; private set; } = 1;

    /// <summary>Variants and extras chosen, already included in <see cref="UnitPrice"/>.</summary>
    public IReadOnlyCollection<OrderItemOption> Options => _options.AsReadOnly();

    /// <summary>"Latte (Grande, Leche de almendra)": the name with its options, for tickets and invoices.</summary>
    public string Describe() => _options.Count == 0
        ? ProductName
        : $"{ProductName} ({string.Join(", ", _options.Select(option => option.OptionName))})";

    internal void RegisterReturn(decimal quantity)
    {
        if (quantity <= 0)
            throw new DomainException("La cantidad devuelta debe ser mayor a cero.");
        if (!UnitTypeRules.IsValidQuantity(quantity, Unit))
            throw new DomainException($"La cantidad devuelta de '{ProductName}' no es válida para su unidad.");
        if (quantity > ReturnableQuantity)
            throw new DomainException($"De '{ProductName}' solo quedan {ReturnableQuantity:0.###} por devolver.");

        ReturnedQuantity += quantity;
    }

    /// <summary>
    /// What cancelling <paramref name="quantity"/> more units takes off the line: the line then charges the units left,
    /// rounded once to cents as when it was sold, and all of them cancelled leave it at zero.
    /// </summary>
    internal decimal CancellationAmount(decimal quantity)
    {
        if (quantity <= 0)
            throw new DomainException("La cantidad a cancelar debe ser mayor a cero.");
        if (!UnitTypeRules.IsValidQuantity(quantity, Unit))
            throw new DomainException($"La cantidad a cancelar de '{ProductName}' no es válida para su unidad.");
        if (quantity > ReturnableQuantity)
            throw new DomainException($"De '{ProductName}' solo quedan {ReturnableQuantity:0.###} por cancelar.");

        var left = ActiveQuantity - quantity;
        var charged = left == 0 ? 0m : decimal.Round(left * UnitPrice, 2, MidpointRounding.ToEven);
        return ChargedPrice - charged;
    }

    internal void RegisterCancellation(decimal quantity, decimal amount)
    {
        CancelledQuantity += quantity;
        CancelledAmount += amount;
    }
}
