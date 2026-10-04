using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;

namespace FoodParkDigital.Domain.Orders.Entities;

public class OrderItem : BaseEntity
{
    private OrderItem() { }

    /// <param name="fiscal">Unit and invoicing codes of the product; a generic piece when omitted.</param>
    public OrderItem(
        Guid orderId,
        Guid productId,
        string productName,
        decimal quantity,
        decimal unitPrice,
        string? notes,
        VatRate vatRate,
        OrderLineFiscalData? fiscal = null)
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
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = null!;
    /// <summary>Up to three decimals for weight, volume, length and time; whole for everything else.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Quantity given back with refunds; the sold quantity is never edited.</summary>
    public decimal ReturnedQuantity { get; private set; }

    public decimal ReturnableQuantity => Quantity - ReturnedQuantity;
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
}
