using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// An order of a delivery platform inside a settlement, with the expected net the settlement counted for it. An order is
/// in one settlement in force at most (<see cref="IsActive"/>): voiding the settlement keeps the row as history and frees
/// the order for another one.
/// </summary>
public class PlatformSettlementOrder : BaseEntity, ITenantScoped
{
    private PlatformSettlementOrder() { }

    internal PlatformSettlementOrder(Guid tenantId, Guid settlementId, Guid orderId, decimal expectedNet)
    {
        TenantId = tenantId;
        SettlementId = settlementId;
        OrderId = orderId;
        ExpectedNet = expectedNet;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public Guid SettlementId { get; private set; }
    public Guid OrderId { get; private set; }

    /// <summary>What the platform owed for the order when the settlement counted it (<see cref="PlatformSettlementCalculator"/>).</summary>
    public decimal ExpectedNet { get; private set; }

    /// <summary>Whether the settlement is in force (open or closed); false once it is voided.</summary>
    public bool IsActive { get; private set; }

    internal void Recount(decimal expectedNet) => ExpectedNet = expectedNet;

    internal void Release() => IsActive = false;
}

/// <summary>Part of a deposit that no order explains: a promotion, a claim, a fee, a balance… with its signed amount.</summary>
public class PlatformSettlementAdjustment : BaseEntity, ITenantScoped
{
    public const int MaxDescriptionLength = 200;
    /// <summary>The largest adjustment either way: a guard against a figure typed wrong.</summary>
    public const decimal MaxAmount = 1_000_000m;

    private PlatformSettlementAdjustment() { }

    internal PlatformSettlementAdjustment(
        Guid tenantId, Guid settlementId, PlatformSettlementAdjustmentKind kind, string? description, decimal amount)
    {
        TenantId = tenantId;
        SettlementId = settlementId;
        Kind = kind;
        Description = description;
        Amount = amount;
    }

    public Guid TenantId { get; private set; }
    public Guid SettlementId { get; private set; }
    public PlatformSettlementAdjustmentKind Kind { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Negative when the platform deducts it from the deposit, positive when it adds it.</summary>
    public decimal Amount { get; private set; }
}

/// <summary>The deposit a settlement explains: the period of orders it pays, when it arrived, how much and its reference.</summary>
/// <param name="PeriodFrom">The first date of the business the deposit pays.</param>
/// <param name="PeriodTo">The last date of the business the deposit pays.</param>
/// <param name="DepositedOn">When the money arrived (or was charged).</param>
/// <param name="DepositedAmount">Negative when the platform charged the business instead of depositing.</param>
public sealed record PlatformSettlementDeposit(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly DepositedOn,
    decimal DepositedAmount,
    string? Reference,
    string? Notes);

/// <summary>An order chosen for a settlement, with what the use case read of it.</summary>
/// <param name="RegisteredOn">The date of the business it was registered on.</param>
/// <param name="ExpectedNet">Its expected net now (<see cref="PlatformSettlementCalculator.ExpectedNet"/>).</param>
public sealed record PlatformSettlementOrderInput(Guid OrderId, OrderChannel Channel, DateOnly RegisteredOn, decimal ExpectedNet);

/// <summary>An adjustment as typed: the reason, a description and the signed amount.</summary>
public sealed record PlatformSettlementAdjustmentInput(PlatformSettlementAdjustmentKind Kind, string? Description, decimal Amount);
