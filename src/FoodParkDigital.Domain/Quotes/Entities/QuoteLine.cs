using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Quotes.Entities;

/// <summary>
/// What a line of a quote proposes (ADR 0053 point 14): a service or product of the catalog, or something written by hand,
/// with its quantity and the price the business gives it. Its amount is rounded to cents, as a line of an order.
/// </summary>
public class QuoteLine : BaseEntity, ITenantScoped
{
    private QuoteLine() { }

    internal QuoteLine(Guid tenantId, Guid quoteId, int position, QuoteLineInput input)
    {
        TenantId = tenantId;
        QuoteId = quoteId;
        Position = position;
        ProductId = input.ProductId;
        Description = input.Description.Trim();
        Quantity = input.Quantity;
        UnitPrice = input.UnitPrice;
        TaxRate = input.Rate.Percent;
        IsTaxExempt = input.Rate.IsExempt;
        Total = decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);
    }

    public Guid TenantId { get; private set; }
    public Guid QuoteId { get; private set; }
    /// <summary>Its place in the quote, from 1.</summary>
    public int Position { get; private set; }
    /// <summary>The service or product of the catalog it came from; null when it was written by hand.</summary>
    public Guid? ProductId { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    /// <summary>With or without IVA, as the prices of the quote (<see cref="Quote.PricesIncludeTax"/>).</summary>
    public decimal UnitPrice { get; private set; }
    public decimal TaxRate { get; private set; }
    public bool IsTaxExempt { get; private set; }
    public decimal Total { get; private set; }

    public VatRate VatRate => new(TaxRate, IsTaxExempt);
}

/// <summary>A line as the business writes it. The rate is the product's, or the business's for a line written by hand.</summary>
public sealed record QuoteLineInput
{
    public QuoteLineInput(Guid? productId, string description, decimal quantity, decimal unitPrice, VatRate rate)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("The product is required when given.", nameof(productId));
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException(QuoteRules.DescriptionMessage);
        if (description.Trim().Length > QuoteRules.MaxDescriptionLength)
            throw new DomainException(QuoteRules.DescriptionTooLongMessage);
        if (quantity <= 0 || quantity > QuoteRules.MaxQuantity || decimal.Round(quantity, 3) != quantity)
            throw new DomainException(QuoteRules.QuantityMessage);
        if (unitPrice < 0 || unitPrice > QuoteRules.MaxUnitPrice || decimal.Round(unitPrice, 2) != unitPrice)
            throw new DomainException(QuoteRules.UnitPriceMessage);

        ProductId = productId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Rate = rate;
    }

    public Guid? ProductId { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public VatRate Rate { get; }
}
