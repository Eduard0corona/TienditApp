using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;

namespace FoodParkDigital.Domain.Orders.Entities;

public class OrderItem : BaseEntity
{
    private OrderItem() { }

    public OrderItem(
        Guid orderId,
        Guid productId,
        string productName,
        int quantity,
        decimal unitPrice,
        string? notes,
        VatRate vatRate)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (unitPrice < 0)
            throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));

        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TotalPrice = quantity * unitPrice;
        Notes = notes;
        TaxRate = vatRate.Percent;
        IsTaxExempt = vatRate.IsExempt;
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal TotalPrice { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>IVA rate in percent applied to the line (0 when exempt).</summary>
    public decimal TaxRate { get; private set; }
    public bool IsTaxExempt { get; private set; }

    public VatRate VatRate => new(TaxRate, IsTaxExempt);
}
