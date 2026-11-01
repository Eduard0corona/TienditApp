using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;

namespace FoodParkDigital.Domain.Quotes;

/// <summary>
/// Who the quote is for (ADR 0053 point 14): a customer of the business or just their name and phone, and the email where
/// they want it, when they gave one.
/// </summary>
public sealed record QuoteCustomer
{
    public QuoteCustomer(Guid? customerId, string name, string phone, string? email)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("The customer is required when given.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(QuoteRules.CustomerNameMessage);
        if (name.Trim().Length > QuoteRules.MaxCustomerNameLength)
            throw new DomainException(QuoteRules.CustomerNameTooLongMessage);
        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length > QuoteRules.MaxCustomerPhoneLength)
            throw new DomainException(QuoteRules.CustomerPhoneMessage);

        CustomerId = customerId;
        Name = name.Trim();
        Phone = phone.Trim();
        Email = InvoiceEmailAddress.Normalize(email);
    }

    public Guid? CustomerId { get; }
    public string Name { get; }
    public string Phone { get; }
    public string? Email { get; }
}

/// <summary>
/// What the customer paid for the visit that the quote takes off (ADR 0053 point 14, «Se descuenta si contratas»): the server
/// works it out from the sale of the visit, and it stops applying once that sale is cancelled.
/// </summary>
public sealed record QuoteVisitCredit
{
    public QuoteVisitCredit(Guid saleOrderId, decimal amount)
    {
        if (saleOrderId == Guid.Empty)
            throw new ArgumentException("The sale of the visit is required.", nameof(saleOrderId));
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount), "What was paid for the visit is positive, in cents.");
        SaleOrderId = saleOrderId;
        Amount = amount;
    }

    public Guid SaleOrderId { get; }
    public decimal Amount { get; }
}
