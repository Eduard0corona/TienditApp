using System.Linq.Expressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Finance.Queries;

/// <summary>A session with its drawer totals computed in SQL (same rules as <see cref="CashSession"/>).</summary>
internal sealed class CashSessionRow
{
    public CashSession Session { get; init; } = null!;
    public string? LocationName { get; init; }
    public decimal Inflows { get; init; }
    public decimal Outflows { get; init; }
    public decimal NonCashInflows { get; init; }
}

internal static class CashSessionProjection
{
    public static Expression<Func<CashSession, CashSessionRow>> ToRow(IApplicationDbContext context) => x => new CashSessionRow
    {
        Session = x,
        LocationName = context.InventoryLocations
            .Where(l => l.Id == x.InventoryLocationId)
            .Select(l => l.Name)
            .FirstOrDefault(),
        // Only cash counts for the drawer; card, transfer and wallet payments are reported apart.
        Inflows = context.CashTransactions
            .Where(t => t.CashSessionId == x.Id && t.Amount > 0
                && (t.PaymentMethod == null || t.PaymentMethod == OrderPaymentMethod.Cash))
            .Sum(t => (decimal?)t.Amount) ?? 0m,
        Outflows = context.CashTransactions
            .Where(t => t.CashSessionId == x.Id && t.Amount < 0
                && (t.PaymentMethod == null || t.PaymentMethod == OrderPaymentMethod.Cash))
            .Sum(t => (decimal?)(-t.Amount)) ?? 0m,
        // Net of refunds made with those methods.
        NonCashInflows = context.CashTransactions
            .Where(t => t.CashSessionId == x.Id
                && t.PaymentMethod != null && t.PaymentMethod != OrderPaymentMethod.Cash)
            .Sum(t => (decimal?)t.Amount) ?? 0m
    };

    public static DTOs.CashSessionDto ToDto(CashSessionRow row, Guid? currentUserId) => new()
    {
        Id = row.Session.Id,
        OpenedAt = row.Session.OpenedAt,
        OpenedBy = row.Session.OpenedBy,
        OpenedByUserId = row.Session.OpenedByUserId,
        IsMine = row.Session.IsOwnedBy(currentUserId),
        InventoryLocationId = row.Session.InventoryLocationId,
        InventoryLocationName = row.LocationName,
        OpeningAmount = row.Session.OpeningAmount,
        ClosedAt = row.Session.ClosedAt,
        ClosedBy = row.Session.ClosedBy,
        ClosingAmount = row.Session.ClosingAmount,
        Status = row.Session.Status,
        Notes = row.Session.Notes,
        Inflows = row.Inflows,
        Outflows = row.Outflows,
        NonCashInflows = row.NonCashInflows,
        ExpectedBalance = row.Session.OpeningAmount + row.Inflows - row.Outflows,
        ExpectedClosingAmount = row.Session.ExpectedClosingAmount,
        ClosingDifference = row.Session.ClosingDifference,
        ClosingNotes = row.Session.ClosingNotes,
        CashCount = DTOs.CashSessionMapping.ReadCashCount(row.Session.CashCountJson)
    };
}
