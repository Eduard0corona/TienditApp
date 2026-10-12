using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>A settlement of a delivery platform's deposit, as the list shows it: its period, the deposit and its accounts.</summary>
public class PlatformSettlementDto
{
    public Guid Id { get; set; }
    public OrderChannel Channel { get; set; }
    public PlatformSettlementStatus Status { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateOnly DepositedOn { get; set; }

    /// <summary>What arrived; negative when the platform charged the business instead.</summary>
    public decimal DepositedAmount { get; set; }

    public string? Reference { get; set; }
    public string? Notes { get; set; }

    /// <summary>The sum of its orders' expected net.</summary>
    public decimal OrdersTotal { get; set; }

    /// <summary>The sum of its adjustments.</summary>
    public decimal AdjustmentsTotal { get; set; }

    /// <summary>What the platform should have deposited: its orders plus its adjustments.</summary>
    public decimal ExpectedDeposit { get; set; }

    /// <summary>What arrived minus what should have: it closes only at zero.</summary>
    public decimal Difference { get; set; }

    public int OrderCount { get; set; }

    /// <summary>The revision a change sends back; an older one is refused.</summary>
    public int Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>
/// A settlement with its orders and adjustments and, while it is open, the other orders of the platform still pending up
/// to the end of its period, to add.
/// </summary>
public class PlatformSettlementDetailDto
{
    public PlatformSettlementDto Settlement { get; set; } = null!;
    public List<PlatformSettlementOrderDto> Orders { get; set; } = [];
    public List<PlatformSettlementAdjustmentDto> Adjustments { get; set; } = [];

    /// <summary>The pending orders of the platform registered up to the end of the period, not in it (only while open).</summary>
    public List<PlatformSettlementOrderDto> Candidates { get; set; } = [];

    /// <summary>More pending orders than fit in one settlement: the oldest are listed.</summary>
    public bool CandidatesTruncated { get; set; }
}

/// <summary>An order of a delivery platform in a settlement, or pending for one, with what the platform owes for it.</summary>
public class PlatformSettlementOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;

    /// <summary>The folio the platform's app shows for the order.</summary>
    public string ExternalOrderNumber { get; set; } = null!;

    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>The date of the business the order was registered on.</summary>
    public DateOnly RegisteredOn { get; set; }

    /// <summary>Whether it was registered within the period of the settlement.</summary>
    public bool InPeriod { get; set; }

    public OrderStatus Status { get; set; }
    public PlatformCollectedBy CollectedBy { get; set; }
    public decimal OrderTotal { get; set; }

    /// <summary>What was given back of the order so far.</summary>
    public decimal RefundedAmount { get; set; }

    /// <summary>What the settlement counted for the order (for a pending one, what it would count now).</summary>
    public decimal ExpectedNet { get; set; }

    /// <summary>What the order is expected to bring now: different from <see cref="ExpectedNet"/> after a refund since the last save.</summary>
    public decimal CurrentExpectedNet { get; set; }
}

/// <summary>Part of a deposit no order explains, with its reason and signed amount.</summary>
public class PlatformSettlementAdjustmentDto
{
    public PlatformSettlementAdjustmentKind Kind { get; set; }
    public string? Description { get; set; }

    /// <summary>Negative when the platform deducts it, positive when it adds it.</summary>
    public decimal Amount { get; set; }
}

/// <summary>The orders of a platform pending for a new settlement of a period, the oldest first.</summary>
public class PendingPlatformOrdersDto
{
    public List<PlatformSettlementOrderDto> Orders { get; set; } = [];

    /// <summary>More pending orders than fit in one settlement: the oldest are listed.</summary>
    public bool Truncated { get; set; }

    /// <summary>The settlement of the platform already open, if there is one: a new one waits until it closes or is voided.</summary>
    public Guid? OpenSettlementId { get; set; }
}

/// <summary>What a delivery platform still owes the business for its orders not settled yet.</summary>
public class PlatformReceivableDto
{
    public OrderChannel Channel { get; set; }

    /// <summary>Whether the business takes orders of the platform now.</summary>
    public bool IsActive { get; set; }

    public int PendingOrders { get; set; }

    /// <summary>The sum of their expected net (each one rounded as a settlement counts it).</summary>
    public decimal PendingAmount { get; set; }

    /// <summary>The date of the business of the oldest pending order.</summary>
    public DateOnly? OldestPendingOn { get; set; }

    public Guid? OpenSettlementId { get; set; }

    /// <summary>The last date of the business a closed settlement paid: the next period starts the day after.</summary>
    public DateOnly? SettledThrough { get; set; }
}
