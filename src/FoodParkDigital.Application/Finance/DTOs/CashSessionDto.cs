using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Finance.DTOs;

public class CashSessionDto
{
    public Guid Id { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    /// <summary>The user (email) of who opened the drawer, or "system" / "system-migration" when no person did.</summary>
    public string OpenedBy { get; set; } = null!;
    /// <summary>The name of who opened it, when they are a member of the business; without it, show <see cref="OpenedBy"/>.</summary>
    public string? OpenedByName { get; set; }
    public decimal OpeningAmount { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    /// <summary>The name of who closed it, when they are a member of the business; without it, show <see cref="ClosedBy"/>.</summary>
    public string? ClosedByName { get; set; }
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
    /// <summary>Branch of the drawer: payments of sales made there enter it.</summary>
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
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
    /// <summary>Every method without a bucket of its own: the buckets always add up to <see cref="SalesPaymentsTotal"/>.</summary>
    public decimal OtherTotal { get; set; }
    /// <summary>
    /// What the delivery platforms charged in their apps («Plataforma» payments): part of what was charged, but in no
    /// drawer and in no cashier's row. The platform deposits it later, minus its commission and withholdings.
    /// </summary>
    public decimal PlatformTotal { get; set; }
    public int PlatformPaymentsCount { get; set; }
    /// <summary>What went back as «Plataforma» in the day (orders a platform charged and then cancelled): no drawer gave it.</summary>
    public decimal PlatformRefundsTotal { get; set; }
    /// <summary>
    /// What customers paid online into the business's Mercado Pago account («En línea» payments, Fase 24I): part of what
    /// was charged, but in no drawer and in no cashier's row. Mercado Pago releases it to the business, minus its fee.
    /// </summary>
    public decimal OnlineTotal { get; set; }
    public int OnlinePaymentsCount { get; set; }
    /// <summary>What went back «En línea» in the day, through Mercado Pago: no drawer gave it.</summary>
    public decimal OnlineRefundsTotal { get; set; }
    /// <summary>Tips given with the payments (not sales), and the part of them paid in cash (in the drawer).</summary>
    public decimal TipsTotal { get; set; }
    public decimal CashTipsTotal { get; set; }
    /// <summary>Movements other than sales and tips (deposits, expenses, withdrawals, refunds), whatever their method.</summary>
    public decimal ManualIncomeTotal { get; set; }
    public decimal ManualExpenseTotal { get; set; }
    /// <summary>The part of those movements made in cash: only it enters or leaves the drawers.</summary>
    public decimal CashManualIncomeTotal { get; set; }
    public decimal CashManualExpenseTotal { get; set; }
    /// <summary>
    /// All the money the business received in the day: payments and tips plus manual income minus manual expenses, of any
    /// method, except what the platforms charged (<see cref="PlatformTotal"/>) and what was paid online
    /// (<see cref="OnlineTotal"/>), which reach the business later and minus a fee.
    /// </summary>
    public decimal NetCashFlow { get; set; }
    /// <summary>Cash payments and cash tips plus cash manual income minus cash manual expenses: the net change of the drawers.</summary>
    public decimal NetCashOnlyFlow { get; set; }
    /// <summary>Payments by the person who took them, without what the platforms charged.</summary>
    public List<DailyCashCutByUserDto> ByUser { get; set; } = [];
    public List<DailyCashCutByLocationDto> ByLocation { get; set; } = [];
    /// <summary>Payments by the branch where the sale was made.</summary>
    public List<DailyCashCutByBranchDto> ByBranch { get; set; } = [];
}

public class DailyCashCutByUserDto
{
    /// <summary>The user (email) who took the payments, or "Sistema": what the cut groups and filters by.</summary>
    public string UserName { get; set; } = "Sistema";
    /// <summary>Their name, when they are a member of the business; without it, show <see cref="UserName"/>.</summary>
    public string? DisplayName { get; set; }
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

public class DailyCashCutByBranchDto
{
    public Guid? BranchId { get; set; }
    public string BranchName { get; set; } = "Sin sucursal";
    public int PaymentsCount { get; set; }
    public decimal TotalAmount { get; set; }
}
