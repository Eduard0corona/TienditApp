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
    /// <summary>Cash that entered the drawer (cash payments and manual deposits).</summary>
    public decimal Inflows { get; set; }
    /// <summary>Cash that left the drawer (expenses, withdrawals, cash refunds).</summary>
    public decimal Outflows { get; set; }
    /// <summary>Card, transfer, wallet and other non-cash payments; not part of the drawer.</summary>
    public decimal NonCashInflows { get; set; }
    /// <summary>Opening amount plus cash inflows minus cash outflows.</summary>
    public decimal ExpectedBalance { get; set; }
    public Guid? OpenedByUserId { get; set; }
    /// <summary>True when the drawer belongs to the user asking (their payments enter it).</summary>
    public bool IsMine { get; set; }
    public Guid? InventoryLocationId { get; set; }
    public string? InventoryLocationName { get; set; }
    /// <summary>Expected cash frozen at closing.</summary>
    public decimal? ExpectedClosingAmount { get; set; }
    /// <summary>Counted minus expected: negative is a shortage.</summary>
    public decimal? ClosingDifference { get; set; }
    public string? ClosingNotes { get; set; }
    public List<CashCountLineDto> CashCount { get; set; } = [];
}

/// <summary>Bills or coins of one denomination counted at closing.</summary>
public class CashCountLineDto
{
    public decimal Denomination { get; set; }
    public int Quantity { get; set; }
}

public class CashTransactionDto
{
    public Guid Id { get; set; }
    public Guid CashSessionId { get; set; }
    public CashTransactionType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public OrderPaymentMethod? PaymentMethod { get; set; }
    public Guid? InventoryLocationId { get; set; }
    public string? InventoryLocationName { get; set; }
    public Guid? OrderId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

public class DailyCashCutDto
{
    public DateOnly Date { get; set; }
    public decimal SalesPaymentsTotal { get; set; }
    public int SalesPaymentsCount { get; set; }
    public decimal CashTotal { get; set; }
    public decimal CardTotal { get; set; }
    public decimal TransferTotal { get; set; }
    public decimal WalletTotal { get; set; }
    public decimal OtherTotal { get; set; }
    public decimal ManualIncomeTotal { get; set; }
    public decimal ManualExpenseTotal { get; set; }
    public decimal NetCashFlow { get; set; }
    /// <summary>Cash payments plus manual income minus manual expenses: the net change of the drawer.</summary>
    public decimal NetCashOnlyFlow { get; set; }
    public List<DailyCashCutByUserDto> ByUser { get; set; } = [];
    public List<DailyCashCutByLocationDto> ByLocation { get; set; } = [];
}

public class DailyCashCutByUserDto
{
    public string UserName { get; set; } = "Sistema";
    public int PaymentsCount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class DailyCashCutByLocationDto
{
    public Guid? InventoryLocationId { get; set; }
    public string LocationName { get; set; } = "Sin sucursal";
    public int PaymentsCount { get; set; }
    public decimal TotalAmount { get; set; }
}
