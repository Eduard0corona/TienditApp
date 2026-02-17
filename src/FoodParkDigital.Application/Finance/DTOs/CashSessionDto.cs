using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Finance.DTOs;

public class CashSessionDto
{
    public Guid Id { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public string OpenedBy { get; set; } = null!;
    public decimal OpeningAmount { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public decimal? ClosingAmount { get; set; }
    public CashSessionStatus Status { get; set; }
    public string? Notes { get; set; }
    public decimal Inflows { get; set; }
    public decimal Outflows { get; set; }
    public decimal ExpectedBalance { get; set; }
}

public class CashTransactionDto
{
    public Guid Id { get; set; }
    public Guid CashSessionId { get; set; }
    public CashTransactionType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public OrderPaymentMethod? PaymentMethod { get; set; }
    public Guid? OrderId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}
