using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Finance.Entities;

public class CashTransaction : AuditableEntity, ITenantScoped
{
    private CashTransaction() { }

    public CashTransaction(
        Guid tenantId,
        Guid cashSessionId,
        CashTransactionType transactionType,
        decimal amount,
        DateTimeOffset occurredAt,
        OrderPaymentMethod? paymentMethod = null,
        Guid? orderId = null,
        string? reference = null,
        string? notes = null)
    {
        TenantId = tenantId;
        CashSessionId = cashSessionId;
        TransactionType = transactionType;
        Amount = amount;
        OccurredAt = occurredAt;
        PaymentMethod = paymentMethod;
        OrderId = orderId;
        Reference = reference?.Trim();
        Notes = notes?.Trim();
    }

    public Guid TenantId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public CashTransactionType TransactionType { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public OrderPaymentMethod? PaymentMethod { get; private set; }
    public Guid? OrderId { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }

    public CashSession CashSession { get; private set; } = null!;
}
