using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Counts;

/// <summary>The counts of the business, the newest first (Fase 27A, ADR 0037).</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryCountsQuery : IRequest<PaginatedList<InventoryCountListItemDto>>
{
    public InventoryCountStatus? Status { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetInventoryCountsQueryHandler : IRequestHandler<GetInventoryCountsQuery, PaginatedList<InventoryCountListItemDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryCountsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<PaginatedList<InventoryCountListItemDto>> Handle(GetInventoryCountsQuery request, CancellationToken cancellationToken)
    {
        var counts = _context.InventoryCounts.AsNoTracking();
        if (request.Status is { } status)
            counts = counts.Where(c => c.Status == status);
        if (request.InventoryLocationId is { } locationId)
            counts = counts.Where(c => c.InventoryLocationId == locationId);

        var query =
            from count in counts
            join location in _context.InventoryLocations.AsNoTracking() on count.InventoryLocationId equals location.Id
            orderby count.StartedAt descending, count.CountNumber descending
            select new InventoryCountListItemDto
            {
                Id = count.Id,
                CountNumber = count.CountNumber,
                InventoryLocationId = count.InventoryLocationId,
                InventoryLocationName = location.Name,
                Status = count.Status,
                CategoryName = count.CategoryName,
                StartedAt = count.StartedAt,
                StartedBy = count.StartedBy,
                PostedAt = count.PostedAt,
                CancelledAt = count.CancelledAt,
                LineCount = count.LineCount,
                CountedLineCount = count.CountedLineCount
            };
        return PaginatedList<InventoryCountListItemDto>.CreateAsync(query, request.PageNumber, request.PageSize, cancellationToken);
    }
}

/// <summary>A count with what its differences are worth.</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryCountQuery(Guid Id) : IRequest<InventoryCountDto>;

public class GetInventoryCountQueryHandler : IRequestHandler<GetInventoryCountQuery, InventoryCountDto>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryCountQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryCountDto> Handle(GetInventoryCountQuery request, CancellationToken cancellationToken)
        => await InventoryCountReads.CountAsync(_context, request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(InventoryCount), request.Id);
}

/// <summary>The items of a count by name, searched by name, code, SKU or barcode (a scanner types it), over every page.</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryCountLinesQuery : IRequest<PaginatedList<InventoryCountLineDto>>
{
    public Guid CountId { get; init; }
    public string? Search { get; init; }
    /// <summary>Only the items not counted yet.</summary>
    public bool? Pending { get; init; }
    /// <summary>Only the counted items whose quantity is not what the system had.</summary>
    public bool? WithDifference { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public class GetInventoryCountLinesQueryHandler : IRequestHandler<GetInventoryCountLinesQuery, PaginatedList<InventoryCountLineDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryCountLinesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<InventoryCountLineDto>> Handle(GetInventoryCountLinesQuery request, CancellationToken cancellationToken)
        => await InventoryCountReads.LinesAsync(_context, request, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(InventoryCount), request.CountId);
}

/// <summary>How a count and its items are read, after a change too.</summary>
internal static class InventoryCountReads
{
    public static async Task<InventoryCountDto?> CountAsync(IApplicationDbContext context, Guid countId, CancellationToken cancellationToken)
    {
        var dto = await (
                from count in context.InventoryCounts.AsNoTracking()
                join location in context.InventoryLocations.AsNoTracking() on count.InventoryLocationId equals location.Id
                where count.Id == countId
                select new InventoryCountDto
                {
                    Id = count.Id,
                    CountNumber = count.CountNumber,
                    InventoryLocationId = count.InventoryLocationId,
                    InventoryLocationName = location.Name,
                    Status = count.Status,
                    CategoryName = count.CategoryName,
                    Notes = count.Notes,
                    StartedAt = count.StartedAt,
                    StartedBy = count.StartedBy,
                    PostedAt = count.PostedAt,
                    PostedBy = count.PostedBy,
                    CancelledAt = count.CancelledAt,
                    CancelledBy = count.CancelledBy,
                    CancelReason = count.CancelReason,
                    LineCount = count.LineCount,
                    CountedLineCount = count.CountedLineCount,
                    Revision = count.Revision,
                    IncreaseMovementId = count.IncreaseMovementId,
                    DecreaseMovementId = count.DecreaseMovementId
                })
            .FirstOrDefaultAsync(cancellationToken);
        if (dto is null)
            return null;

        var differences = await (
                from line in context.InventoryCountLines.AsNoTracking()
                join supply in context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
                where line.CountId == countId && line.CountedQuantity != null && line.CountedQuantity != line.SystemQuantity
                select new { Difference = line.CountedQuantity!.Value - line.SystemQuantity!.Value, supply.AverageCost, supply.LastCost, supply.DefaultCost })
            .ToListAsync(cancellationToken);
        dto.DifferenceLineCount = differences.Count;

        var movementIds = new[] { dto.IncreaseMovementId, dto.DecreaseMovementId }.OfType<Guid>().ToList();
        var movements = await context.InventoryMovements.AsNoTracking()
            .Where(m => movementIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MovementNumber, m.TotalAmount })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var increase = dto.IncreaseMovementId is { } increaseId ? movements.GetValueOrDefault(increaseId) : null;
        var decrease = dto.DecreaseMovementId is { } decreaseId ? movements.GetValueOrDefault(decreaseId) : null;
        dto.IncreaseMovementNumber = increase?.MovementNumber;
        dto.DecreaseMovementNumber = decrease?.MovementNumber;

        switch (dto.Status)
        {
            case InventoryCountStatus.InProgress:
                var valued = differences
                    .Select(d => d.Difference * (InventoryLedger.AdjustmentUnitCost(d.Difference, d.AverageCost, d.LastCost, d.DefaultCost) ?? 0m))
                    .ToList();
                dto.SurplusValue = Money(valued.Where(v => v > 0).Sum());
                dto.ShortageValue = Money(-valued.Where(v => v < 0).Sum());
                break;
            case InventoryCountStatus.Posted:
                dto.SurplusValue = Money(increase?.TotalAmount ?? 0m);
                dto.ShortageValue = Money(decrease?.TotalAmount ?? 0m);
                break;
        }

        return dto;
    }

    /// <summary>The items of the count; null when the count does not exist in the business.</summary>
    public static async Task<PaginatedList<InventoryCountLineDto>?> LinesAsync(
        IApplicationDbContext context, GetInventoryCountLinesQuery request, CancellationToken cancellationToken)
    {
        var count = await context.InventoryCounts.AsNoTracking()
            .Where(c => c.Id == request.CountId)
            .Select(c => new { c.InventoryLocationId, c.Status, c.IncreaseMovementId, c.DecreaseMovementId })
            .FirstOrDefaultAsync(cancellationToken);
        if (count is null)
            return null;

        var rows = Rows(context, request.CountId, count.InventoryLocationId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            rows = rows.Where(r =>
                r.Name.ToLower().Contains(search)
                || (r.Code != null && r.Code.ToLower().Contains(search))
                || (r.Sku != null && r.Sku.ToLower().Contains(search))
                || (r.Barcode != null && r.Barcode.ToLower().Contains(search)));
        }
        if (request.Pending == true)
            rows = rows.Where(r => r.Line.CountedQuantity == null);
        if (request.WithDifference == true)
            rows = rows.Where(r => r.Line.CountedQuantity != null && r.Line.CountedQuantity != r.Line.SystemQuantity);

        var (pageNumber, pageSize) = Paging.Normalize(request.PageNumber, request.PageSize);
        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(r => r.Name).ThenBy(r => r.Line.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var lines = await MapAsync(context, page, count.Status, count.IncreaseMovementId, count.DecreaseMovementId, cancellationToken);
        return new PaginatedList<InventoryCountLineDto>(lines, total, pageNumber, pageSize);
    }

    /// <summary>These items of the count, as they are now.</summary>
    public static async Task<List<InventoryCountLineDto>> LinesAsync(
        IApplicationDbContext context, InventoryCount count, IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken)
    {
        var page = await Rows(context, count.Id, count.InventoryLocationId)
            .Where(r => lineIds.Contains(r.Line.Id))
            .OrderBy(r => r.Name).ThenBy(r => r.Line.Id)
            .ToListAsync(cancellationToken);
        return await MapAsync(context, page, count.Status, count.IncreaseMovementId, count.DecreaseMovementId, cancellationToken);
    }

    private static IQueryable<LineRow> Rows(IApplicationDbContext context, Guid countId, Guid locationId)
        => from line in context.InventoryCountLines.AsNoTracking()
           join supply in context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
           join stock in context.InventoryStocks.AsNoTracking().Where(s => s.InventoryLocationId == locationId)
               on line.SupplyItemId equals stock.SupplyItemId into stocks
           from stock in stocks.DefaultIfEmpty()
           where line.CountId == countId
           select new LineRow
           {
               Line = line,
               Name = supply.Name,
               Code = supply.Code,
               Sku = supply.Sku,
               Barcode = supply.Barcode,
               CategoryName = supply.CategoryName,
               Unit = supply.Unit,
               IsActive = supply.IsActive,
               AverageCost = supply.AverageCost,
               LastCost = supply.LastCost,
               DefaultCost = supply.DefaultCost,
               OnHand = stock == null ? 0m : stock.OnHandQuantity
           };

    private static async Task<List<InventoryCountLineDto>> MapAsync(
        IApplicationDbContext context,
        List<LineRow> rows,
        InventoryCountStatus status,
        Guid? increaseMovementId,
        Guid? decreaseMovementId,
        CancellationToken cancellationToken)
    {
        // A posted count shows what its adjustments did: each item that moved has one line in one of them.
        var posted = new Dictionary<Guid, (decimal Before, decimal After, decimal? Total)>();
        if (status == InventoryCountStatus.Posted)
        {
            var movementIds = new[] { increaseMovementId, decreaseMovementId }.OfType<Guid>().ToList();
            var supplyIds = rows.Select(r => r.Line.SupplyItemId).ToList();
            posted = (await context.InventoryMovementLines.AsNoTracking()
                    .Where(l => movementIds.Contains(l.MovementId) && supplyIds.Contains(l.SupplyItemId))
                    .Select(l => new { l.SupplyItemId, l.QuantityBefore, l.QuantityAfter, l.TotalCost })
                    .ToListAsync(cancellationToken))
                .ToDictionary(l => l.SupplyItemId, l => (l.QuantityBefore, l.QuantityAfter, l.TotalCost));
        }

        return rows.Select(row =>
        {
            var line = row.Line;
            var dto = new InventoryCountLineDto
            {
                Id = line.Id,
                SupplyItemId = line.SupplyItemId,
                SupplyItemName = row.Name,
                SupplyCode = row.Code,
                SupplySku = row.Sku,
                SupplyBarcode = row.Barcode,
                CategoryName = row.CategoryName,
                Unit = row.Unit,
                IsSupplyActive = row.IsActive,
                CountedQuantity = line.CountedQuantity,
                SystemQuantity = line.SystemQuantity,
                Difference = line.Difference,
                CountedAt = line.CountedAt,
                CountedBy = line.CountedBy,
                Notes = line.Notes
            };
            switch (status)
            {
                case InventoryCountStatus.InProgress:
                    dto.CurrentQuantity = row.OnHand;
                    dto.ResultingQuantity = line.ResultingQuantity(row.OnHand);
                    if (line.Difference is { } difference && difference != 0)
                        dto.DifferenceValue = Money(difference
                            * (InventoryLedger.AdjustmentUnitCost(difference, row.AverageCost, row.LastCost, row.DefaultCost) ?? 0m));
                    break;
                case InventoryCountStatus.Posted when posted.TryGetValue(line.SupplyItemId, out var moved):
                    dto.CurrentQuantity = moved.Before;
                    dto.ResultingQuantity = moved.After;
                    dto.DifferenceValue = Money((moved.After > moved.Before ? 1 : -1) * (moved.Total ?? 0m));
                    break;
            }
            return dto;
        }).ToList();
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed class LineRow
    {
        public InventoryCountLine Line { get; init; } = null!;
        public string Name { get; init; } = string.Empty;
        public string? Code { get; init; }
        public string? Sku { get; init; }
        public string? Barcode { get; init; }
        public string? CategoryName { get; init; }
        public Domain.Catalog.Enums.UnitType Unit { get; init; }
        public bool IsActive { get; init; }
        public decimal AverageCost { get; init; }
        public decimal LastCost { get; init; }
        public decimal DefaultCost { get; init; }
        public decimal OnHand { get; init; }
    }
}
