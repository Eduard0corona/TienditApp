using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>
/// What the settlements read of the orders of a platform: which are still pending, what each is expected to bring now and
/// on which date of the business it was registered. Every query goes through the business's filter.
/// </summary>
internal static class PlatformSettlementReader
{
    /// <summary>
    /// An order of a platform with what its expected net is counted from. Built member by member, so a query can still
    /// filter and sort on it.
    /// </summary>
    internal sealed class PlatformOrderRow
    {
        public Guid OrderId { get; init; }
        public string OrderNumber { get; init; } = null!;
        public string ExternalOrderNumber { get; init; } = null!;
        public OrderChannel Channel { get; init; }
        public DateTimeOffset RegisteredAt { get; init; }
        public OrderStatus Status { get; init; }
        public PlatformCollectedBy CollectedBy { get; init; }
        public decimal OrderTotal { get; init; }
        public decimal RefundedAmount { get; init; }
        public decimal ExpectedSettlement { get; init; }
        public decimal NetPaidAmount { get; init; }

        /// <summary>What the order is expected to bring now (in memory, after the query).</summary>
        public decimal ExpectedNet => PlatformSettlementCalculator.ExpectedNet(ExpectedSettlement, NetPaidAmount, OrderTotal);
    }

    /// <summary>The orders chosen for a save, or why they cannot be.</summary>
    internal sealed record Chosen(IReadOnlyList<PlatformSettlementOrderInput> Orders, string? Error);

    public static async Task<TimeZoneInfo> ZoneAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
    {
        var timeZoneId = await context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        return TenantDateTimeResolver.Resolve(timeZoneId);
    }

    /// <summary>Every order of a delivery platform, of every platform, with what its expected net is counted from.</summary>
    public static IQueryable<PlatformOrderRow> Orders(IApplicationDbContext context)
        => from detail in context.PlatformOrderDetails.AsNoTracking()
           join order in context.Orders.AsNoTracking() on detail.OrderId equals order.Id
           select new PlatformOrderRow
           {
               OrderId = order.Id,
               OrderNumber = order.OrderNumber,
               ExternalOrderNumber = detail.ExternalOrderNumber,
               Channel = detail.Channel,
               RegisteredAt = detail.RegisteredAt,
               Status = order.Status,
               CollectedBy = detail.CollectedBy,
               OrderTotal = detail.OrderTotal,
               RefundedAmount = order.RefundedAmount,
               ExpectedSettlement = detail.ExpectedSettlement,
               NetPaidAmount = order.PaidAmount - order.RefundedAmount
           };

    /// <summary>
    /// The orders not in a settlement in force (but <paramref name="settlementId"/>, the one being saved) whose expected net
    /// is not zero: given back whole, or not collected yet by the business, an order brings nothing.
    /// </summary>
    public static IQueryable<PlatformOrderRow> Pending(IApplicationDbContext context, Guid? settlementId = null)
        => Orders(context).Where(row =>
            row.NetPaidAmount != 0
            && row.ExpectedSettlement != 0
            && !context.PlatformSettlementOrders.Any(line =>
                line.OrderId == row.OrderId && line.IsActive && (settlementId == null || line.SettlementId != settlementId)));

    /// <summary>
    /// The pending orders of a platform for a settlement of the period, the oldest first: those registered in it and, in
    /// the room left of one settlement's worth (<see cref="PlatformSettlement.MaxOrders"/>), the most recent of before (a
    /// statement brings them late; older ones still wait in Por cobrar). Truncated when some did not fit.
    /// </summary>
    public static async Task<(List<PlatformSettlementOrderDto> Orders, bool Truncated)> CandidatesAsync(
        IApplicationDbContext context,
        OrderChannel channel,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var startUtc = DailySalesReadModel.StartUtc(from, zone);
        var endUtc = DailySalesReadModel.EndUtc(to, zone);
        var pending = Pending(context).Where(row => row.Channel == channel && row.RegisteredAt <= endUtc);
        // A backlog of before never pushes the period's own orders out: they are read first.
        var inPeriod = await pending
            .Where(row => row.RegisteredAt >= startUtc)
            .OrderBy(row => row.RegisteredAt)
            .ThenBy(row => row.OrderId)
            .Take(PlatformSettlement.MaxOrders + 1)
            .ToListAsync(cancellationToken);
        var room = PlatformSettlement.MaxOrders + 1 - inPeriod.Count;
        var before = room <= 0
            ? []
            : await pending
                .Where(row => row.RegisteredAt < startUtc)
                .OrderByDescending(row => row.RegisteredAt)
                .ThenByDescending(row => row.OrderId)
                .Take(room)
                .ToListAsync(cancellationToken);
        // Each query reads one more than fits: more than a settlement's worth between the two says some were left out.
        var truncated = inPeriod.Count + before.Count > PlatformSettlement.MaxOrders;
        var kept = inPeriod.Take(PlatformSettlement.MaxOrders).ToList();
        kept.AddRange(before.Take(PlatformSettlement.MaxOrders - kept.Count));
        // Rounded to zero (a cent split in half), an order brings nothing either.
        var orders = kept
            .Where(row => row.ExpectedNet != 0)
            .OrderBy(row => row.RegisteredAt)
            .ThenBy(row => row.OrderId)
            .Select(row => ToDto(row, row.ExpectedNet, from, zone))
            .ToList();
        return (orders, truncated);
    }

    /// <summary>
    /// The orders chosen for a settlement, each with its date of the business and its expected net now. An order of
    /// another platform or business, or one that no longer exists, is refused; one in another settlement in force too.
    /// </summary>
    public static async Task<Chosen> ChosenAsync(
        IApplicationDbContext context,
        OrderChannel channel,
        IReadOnlyCollection<Guid> orderIds,
        Guid? settlementId,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
            return new Chosen([], null);

        var ids = orderIds.ToList();
        var rows = await Orders(context)
            .Where(row => ids.Contains(row.OrderId))
            .ToListAsync(cancellationToken);
        if (rows.Count != ids.Count || rows.Any(row => row.Channel != channel))
            return new Chosen([], PlatformSettlementErrors.OrderNotOfThePlatform);
        if (await context.PlatformSettlementOrders.AnyAsync(
                line => ids.Contains(line.OrderId) && line.IsActive && (settlementId == null || line.SettlementId != settlementId),
                cancellationToken))
            return new Chosen([], PlatformSettlementErrors.OrderTaken);

        var byId = rows.ToDictionary(row => row.OrderId);
        return new Chosen(
            ids.Select(id => byId[id])
                .Select(row => new PlatformSettlementOrderInput(
                    row.OrderId, row.Channel, DailySalesReadModel.LocalDate(row.RegisteredAt, zone), row.ExpectedNet))
                .ToList(),
            null);
    }

    /// <summary>The expected net of each order now.</summary>
    public static async Task<Dictionary<Guid, decimal>> ExpectedNetsAsync(
        IApplicationDbContext context, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var ids = orderIds.ToList();
        var rows = await Orders(context).Where(row => ids.Contains(row.OrderId)).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.OrderId, row => row.ExpectedNet);
    }

    /// <summary>A settlement with its orders, its adjustments and, while open, the pending orders it could add.</summary>
    public static async Task<PlatformSettlementDetailDto> DetailAsync(
        IApplicationDbContext context, Guid settlementId, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var settlement = await context.PlatformSettlements
            .AsNoTracking()
            .Include(s => s.Adjustments)
            .SingleOrDefaultAsync(s => s.Id == settlementId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PlatformSettlement), settlementId);

        var lines = await (
                from line in context.PlatformSettlementOrders.AsNoTracking()
                where line.SettlementId == settlementId
                join row in Orders(context) on line.OrderId equals row.OrderId
                select new { line.ExpectedNet, Row = row })
            .ToListAsync(cancellationToken);
        var orders = lines
            .OrderBy(line => line.Row.RegisteredAt)
            .ThenBy(line => line.Row.OrderId)
            .Select(line => ToDto(line.Row, line.ExpectedNet, settlement.PeriodFrom, zone))
            .ToList();

        // Its own orders are in a settlement in force: they are not pending, so they are never candidates.
        var candidates = new List<PlatformSettlementOrderDto>();
        var truncated = false;
        if (settlement.Status == PlatformSettlementStatus.Open)
        {
            (candidates, truncated) = await CandidatesAsync(
                context, settlement.Channel, settlement.PeriodFrom, settlement.PeriodTo, zone, cancellationToken);
        }

        return new PlatformSettlementDetailDto
        {
            Settlement = ToDto(settlement, orders.Count),
            Orders = orders,
            Adjustments = settlement.Adjustments
                .Select(adjustment => new PlatformSettlementAdjustmentDto
                {
                    Kind = adjustment.Kind,
                    Description = adjustment.Description,
                    Amount = adjustment.Amount
                })
                .ToList(),
            Candidates = candidates,
            CandidatesTruncated = truncated
        };
    }

    public static PlatformSettlementDto ToDto(PlatformSettlement settlement, int orderCount) => new()
    {
        Id = settlement.Id,
        Channel = settlement.Channel,
        Status = settlement.Status,
        PeriodFrom = settlement.PeriodFrom,
        PeriodTo = settlement.PeriodTo,
        DepositedOn = settlement.DepositedOn,
        DepositedAmount = settlement.DepositedAmount,
        Reference = settlement.Reference,
        Notes = settlement.Notes,
        OrdersTotal = settlement.OrdersTotal,
        AdjustmentsTotal = settlement.AdjustmentsTotal,
        ExpectedDeposit = settlement.ExpectedDeposit,
        Difference = settlement.Difference,
        OrderCount = orderCount,
        Revision = settlement.Revision,
        CreatedAt = settlement.CreatedAt,
        ClosedAt = settlement.ClosedAt,
        VoidedAt = settlement.VoidedAt,
        VoidReason = settlement.VoidReason
    };

    /// <param name="periodTo">The end of the period, for an order that may be registered after it (a statement's folio).</param>
    internal static PlatformSettlementOrderDto ToDto(
        PlatformOrderRow row,
        decimal expectedNet,
        DateOnly periodFrom,
        TimeZoneInfo zone,
        DateOnly? periodTo = null)
    {
        var registeredOn = DailySalesReadModel.LocalDate(row.RegisteredAt, zone);
        return new PlatformSettlementOrderDto
        {
            OrderId = row.OrderId,
            OrderNumber = row.OrderNumber,
            ExternalOrderNumber = row.ExternalOrderNumber,
            RegisteredAt = row.RegisteredAt,
            RegisteredOn = registeredOn,
            InPeriod = registeredOn >= periodFrom && (periodTo is null || registeredOn <= periodTo),
            Status = row.Status,
            CollectedBy = row.CollectedBy,
            OrderTotal = row.OrderTotal,
            RefundedAmount = row.RefundedAmount,
            ExpectedNet = expectedNet,
            CurrentExpectedNet = row.ExpectedNet
        };
    }
}
