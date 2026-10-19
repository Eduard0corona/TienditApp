using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Reporting.Entities;

/// <summary>
/// Sales of one branch and kind of order on a past day, in the business's time zone. Read model of the reports:
/// a closed day never changes, because payments and refunds are stamped by the server and never edited, and
/// orders are counted on the day they were created and keep their branch. Snapshots are recomputed from the
/// sales when the business changes its time zone (<see cref="TimeZoneId"/> no longer matches).
/// </summary>
public class DailySalesSnapshot : BaseEntity, ITenantScoped
{
    public const int MaxTimeZoneIdLength = 100;

    private DailySalesSnapshot() { }

    public DailySalesSnapshot(
        Guid tenantId,
        DateOnly businessDate,
        Guid? branchId,
        OrderType orderType,
        string timeZoneId,
        DailySalesFigures figures,
        DateTimeOffset computedAt)
    {
        TenantId = tenantId;
        BusinessDate = businessDate;
        BranchId = branchId;
        OrderType = orderType;
        TimeZoneId = timeZoneId;
        ComputedAt = computedAt;
        OrdersCreated = figures.OrdersCreated;
        PaidOrders = figures.PaidOrders;
        PaymentsCount = figures.PaymentsCount;
        PaymentsAmount = figures.PaymentsAmount;
        CashAmount = figures.CashAmount;
        CardAmount = figures.CardAmount;
        TransferAmount = figures.TransferAmount;
        WalletAmount = figures.WalletAmount;
        OtherAmount = figures.OtherAmount;
        RefundsCount = figures.RefundsCount;
        RefundsAmount = figures.RefundsAmount;
        PlatformAmount = figures.PlatformAmount;
        OnlineAmount = figures.OnlineAmount;
    }

    public Guid TenantId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    /// <summary>Branch of the orders; none for orders made before branches existed without one.</summary>
    public Guid? BranchId { get; private set; }
    public OrderType OrderType { get; private set; }
    /// <summary>Time zone the day was cut with.</summary>
    public string TimeZoneId { get; private set; } = null!;
    public DateTimeOffset ComputedAt { get; private set; }

    /// <summary>Orders received that day, whatever happened to them later.</summary>
    public int OrdersCreated { get; private set; }
    /// <summary>Orders with at least one payment that day.</summary>
    public int PaidOrders { get; private set; }
    public int PaymentsCount { get; private set; }
    public decimal PaymentsAmount { get; private set; }
    public decimal CashAmount { get; private set; }
    public decimal CardAmount { get; private set; }
    public decimal TransferAmount { get; private set; }
    public decimal WalletAmount { get; private set; }
    public decimal OtherAmount { get; private set; }
    public int RefundsCount { get; private set; }
    public decimal RefundsAmount { get; private set; }
    /// <summary>
    /// What delivery platforms charged in their apps («Plataforma» payments): part of <see cref="PaymentsAmount"/>, never
    /// of a drawer. Days stored before the method existed read 0, which is what they had.
    /// </summary>
    public decimal PlatformAmount { get; private set; }

    /// <summary>
    /// What customers paid online into the business's Mercado Pago account («En línea» payments, Fase 24I): part of
    /// <see cref="PaymentsAmount"/>, never of a drawer. Days stored before the method existed read 0, which is what they had.
    /// </summary>
    public decimal OnlineAmount { get; private set; }

    public DailySalesFigures Figures => new(
        OrdersCreated, PaidOrders, PaymentsCount, PaymentsAmount, CashAmount, CardAmount, TransferAmount,
        WalletAmount, OtherAmount, RefundsCount, RefundsAmount, PlatformAmount, OnlineAmount);
}

/// <summary>
/// What was sold in a day: payments by method, refunds and orders. <see cref="PaymentsAmount"/> is the sum of the
/// amounts by method: cash, card, transfer, wallet, other, platform and online.
/// </summary>
/// <param name="PlatformAmount">What delivery platforms charged («Plataforma» payments); it defaults to 0.</param>
/// <param name="OnlineAmount">What customers paid online («En línea» payments); last, so it defaults to 0.</param>
public sealed record DailySalesFigures(
    int OrdersCreated,
    int PaidOrders,
    int PaymentsCount,
    decimal PaymentsAmount,
    decimal CashAmount,
    decimal CardAmount,
    decimal TransferAmount,
    decimal WalletAmount,
    decimal OtherAmount,
    int RefundsCount,
    decimal RefundsAmount,
    decimal PlatformAmount = 0m,
    decimal OnlineAmount = 0m)
{
    public static DailySalesFigures Zero { get; } = new(0, 0, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0m, 0m, 0m);

    /// <summary>What the business kept: charged minus given back.</summary>
    public decimal NetSales => PaymentsAmount - RefundsAmount;

    public bool IsEmpty => OrdersCreated == 0 && PaymentsCount == 0 && RefundsCount == 0;

    public DailySalesFigures Add(DailySalesFigures other) => new(
        OrdersCreated + other.OrdersCreated,
        PaidOrders + other.PaidOrders,
        PaymentsCount + other.PaymentsCount,
        PaymentsAmount + other.PaymentsAmount,
        CashAmount + other.CashAmount,
        CardAmount + other.CardAmount,
        TransferAmount + other.TransferAmount,
        WalletAmount + other.WalletAmount,
        OtherAmount + other.OtherAmount,
        RefundsCount + other.RefundsCount,
        RefundsAmount + other.RefundsAmount,
        PlatformAmount + other.PlatformAmount,
        OnlineAmount + other.OnlineAmount);
}
