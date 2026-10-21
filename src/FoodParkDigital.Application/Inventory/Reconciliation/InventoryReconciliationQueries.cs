using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Reconciliation;

/// <summary>
/// Revisar que cuadre (Fase 27C, ADR 0039): the stocks of the business, or of a location, whose quantity is not what
/// their history adds up to, and how many were compared.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryReconciliationQuery : IRequest<InventoryReconciliationDto>
{
    public Guid? InventoryLocationId { get; init; }
    /// <summary>Item or location name, over every page.</summary>
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public class GetInventoryReconciliationQueryHandler : IRequestHandler<GetInventoryReconciliationQuery, InventoryReconciliationDto>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryReconciliationQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryReconciliationDto> Handle(GetInventoryReconciliationQuery request, CancellationToken cancellationToken)
    {
        var compared = await InventoryReconciliationReads.CompareAsync(_context, request.InventoryLocationId, null, cancellationToken);
        var mismatched = compared.Where(c => c.Difference != 0).ToList();

        var supplyIds = mismatched.Select(m => m.SupplyItemId).Distinct().ToList();
        var locationIds = mismatched.Select(m => m.InventoryLocationId).Distinct().ToList();
        var supplies = await _context.SupplyItems.AsNoTracking()
            .Where(s => supplyIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Unit, s.IsActive })
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        var locations = await _context.InventoryLocations.AsNoTracking()
            .Where(l => locationIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var items = mismatched
            .Where(m => supplies.ContainsKey(m.SupplyItemId) && locations.ContainsKey(m.InventoryLocationId))
            .Select(m =>
            {
                var supply = supplies[m.SupplyItemId];
                return new InventoryReconciliationItemDto
                {
                    SupplyItemId = m.SupplyItemId,
                    SupplyItemName = supply.Name,
                    Unit = supply.Unit,
                    IsActive = supply.IsActive,
                    InventoryLocationId = m.InventoryLocationId,
                    InventoryLocationName = locations[m.InventoryLocationId],
                    OnHandQuantity = m.OnHandQuantity,
                    HistoryQuantity = m.HistoryQuantity,
                    Difference = m.Difference,
                    MovementCount = m.MovementCount
                };
            });
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            items = items.Where(i => i.SupplyItemName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || i.InventoryLocationName.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        var ordered = items
            .OrderBy(i => i.InventoryLocationName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.SupplyItemName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.SupplyItemId)
            .ToList();
        var (pageNumber, pageSize) = Paging.Normalize(request.PageNumber, request.PageSize);
        var page = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        return new InventoryReconciliationDto
        {
            CheckedCount = compared.Count,
            Mismatches = new PaginatedList<InventoryReconciliationItemDto>(page, ordered.Count, pageNumber, pageSize)
        };
    }
}

/// <summary>
/// Por qué no cuadra (Fase 27C, ADR 0039): the history of one stock walked in the order it was recorded, with each place
/// where the stock changed without a movement, each movement that wrote its balance wrong and each reconciliation.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryReconciliationDetailQuery(Guid SupplyItemId, Guid InventoryLocationId) : IRequest<InventoryReconciliationDetailDto>;

public class GetInventoryReconciliationDetailQueryHandler
    : IRequestHandler<GetInventoryReconciliationDetailQuery, InventoryReconciliationDetailDto>
{
    /// <summary>The most recent lines walked; what comes from before them is one place, «before what was reviewed».</summary>
    public const int MaxReviewedLines = 50_000;
    /// <summary>The most recent places listed.</summary>
    public const int MaxEvents = 50;

    private readonly IApplicationDbContext _context;

    public GetInventoryReconciliationDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryReconciliationDetailDto> Handle(GetInventoryReconciliationDetailQuery request, CancellationToken cancellationToken)
    {
        var supply = await _context.SupplyItems.AsNoTracking()
            .Where(s => s.Id == request.SupplyItemId)
            .Select(s => new { s.Id, s.Name, s.Unit, s.IsActive })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SupplyItem), request.SupplyItemId);
        var location = await _context.InventoryLocations.AsNoTracking()
            .Where(l => l.Id == request.InventoryLocationId)
            .Select(l => new { l.Id, l.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(InventoryLocation), request.InventoryLocationId);

        var compared = (await InventoryReconciliationReads.CompareAsync(_context, location.Id, [supply.Id], cancellationToken))
            .SingleOrDefault() ?? new StockAgainstHistory(supply.Id, location.Id, 0, 0, 0);

        // The most recent lines, newest first, then walked oldest first.
        var recent = await (
                from line in _context.InventoryMovementLines.AsNoTracking()
                join movement in _context.InventoryMovements.AsNoTracking() on line.MovementId equals movement.Id
                where line.SupplyItemId == supply.Id && line.InventoryLocationId == location.Id
                orderby line.CreatedAt descending, movement.MovementNumber descending, line.Id descending
                select new HistoryLine(
                    line.Id, line.CreatedAt, movement.Id, movement.MovementNumber, movement.MovementDate, movement.MovementType,
                    movement.MovementReason, line.Quantity, line.QuantityBefore, line.QuantityAfter))
            .Take(MaxReviewedLines)
            .ToListAsync(cancellationToken);
        recent.Reverse();
        var walked = InRecordedOrder(recent);

        var events = Walk(walked, compared.OnHandQuantity);
        if (walked.Count > 0 && walked.Count < compared.MovementCount)
        {
            // What the walked lines do not explain comes from before them: the identity of the gaps gives it exactly.
            var before = compared.Difference - events.Sum(e => e.Quantity);
            if (before != 0)
                events.Insert(0, new InventoryReconciliationEventDto
                {
                    Kind = InventoryReconciliationEventKind.BeforeReviewedHistory,
                    Quantity = before,
                    QuantityTo = walked[0].QuantityBefore,
                    MovementId = walked[0].MovementId,
                    MovementNumber = walked[0].MovementNumber,
                    MovementDate = walked[0].MovementDate
                });
        }
        else if (walked.Count > 0 && walked[0].QuantityBefore != 0)
        {
            events.Insert(0, new InventoryReconciliationEventDto
            {
                Kind = InventoryReconciliationEventKind.BeforeFirstMovement,
                Quantity = walked[0].QuantityBefore,
                QuantityFrom = 0,
                QuantityTo = walked[0].QuantityBefore,
                MovementId = walked[0].MovementId,
                MovementNumber = walked[0].MovementNumber,
                MovementDate = walked[0].MovementDate
            });
        }

        return new InventoryReconciliationDetailDto
        {
            SupplyItemId = supply.Id,
            SupplyItemName = supply.Name,
            Unit = supply.Unit,
            IsActive = supply.IsActive,
            InventoryLocationId = location.Id,
            InventoryLocationName = location.Name,
            OnHandQuantity = compared.OnHandQuantity,
            HistoryQuantity = compared.HistoryQuantity,
            Difference = compared.Difference,
            MovementCount = compared.MovementCount,
            ReviewedCount = walked.Count,
            EventCount = events.Count,
            Events = events.Skip(Math.Max(0, events.Count - MaxEvents)).ToList()
        };
    }

    /// <summary>
    /// The places of the history after its start: between two movements, inside one (or a reconciliation) and after the
    /// last one. With <c>stock − history = before₀ + Σ(beforeᵢ − afterᵢ₋₁) + Σ(afterᵢ − beforeᵢ − movedᵢ) + (stock − after_last)</c>,
    /// they and the start add up to the difference.
    /// </summary>
    private static List<InventoryReconciliationEventDto> Walk(IReadOnlyList<HistoryLine> lines, decimal onHand)
    {
        var events = new List<InventoryReconciliationEventDto>();
        if (lines.Count == 0)
        {
            if (onHand != 0)
                events.Add(new InventoryReconciliationEventDto { Kind = InventoryReconciliationEventKind.NoHistory, Quantity = onHand });
            return events;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i > 0 && line.QuantityBefore != lines[i - 1].QuantityAfter)
            {
                var previous = lines[i - 1];
                events.Add(new InventoryReconciliationEventDto
                {
                    Kind = InventoryReconciliationEventKind.BetweenMovements,
                    Quantity = line.QuantityBefore - previous.QuantityAfter,
                    QuantityFrom = previous.QuantityAfter,
                    QuantityTo = line.QuantityBefore,
                    MovementId = line.MovementId,
                    MovementNumber = line.MovementNumber,
                    MovementDate = line.MovementDate,
                    PreviousMovementId = previous.MovementId,
                    PreviousMovementNumber = previous.MovementNumber,
                    PreviousMovementDate = previous.MovementDate
                });
            }

            var moved = InventoryMovementTypes.Signed(line.MovementType, line.Quantity);
            var inside = line.QuantityAfter - line.QuantityBefore - moved;
            if (inside != 0)
                events.Add(new InventoryReconciliationEventDto
                {
                    Kind = line.MovementReason == InventoryMovementReason.LedgerReconciliation
                        ? InventoryReconciliationEventKind.Reconciled
                        : InventoryReconciliationEventKind.InMovement,
                    Quantity = inside,
                    QuantityFrom = line.QuantityBefore,
                    QuantityTo = line.QuantityAfter,
                    MovementId = line.MovementId,
                    MovementNumber = line.MovementNumber,
                    MovementDate = line.MovementDate,
                    MovementQuantity = moved
                });
        }

        var last = lines[^1];
        if (onHand != last.QuantityAfter)
            events.Add(new InventoryReconciliationEventDto
            {
                Kind = InventoryReconciliationEventKind.AfterLastMovement,
                Quantity = onHand - last.QuantityAfter,
                QuantityFrom = last.QuantityAfter,
                QuantityTo = onHand,
                MovementId = last.MovementId,
                MovementNumber = last.MovementNumber,
                MovementDate = last.MovementDate
            });
        return events;
    }

    /// <summary>
    /// Lines are recorded in order of when they were saved, then of their folio (taken in the transaction, in turn per
    /// business and day). Lines saved together with the same folio are put one after the other by their balances, so a
    /// movement with the same item twice does not look like a gap.
    /// </summary>
    private static List<HistoryLine> InRecordedOrder(List<HistoryLine> lines)
    {
        var ordered = new List<HistoryLine>(lines.Count);
        var i = 0;
        while (i < lines.Count)
        {
            var j = i + 1;
            while (j < lines.Count && lines[j].CreatedAt == lines[i].CreatedAt && lines[j].MovementNumber == lines[i].MovementNumber)
                j++;
            var tied = lines.GetRange(i, j - i);
            while (tied.Count > 0)
            {
                var after = ordered.Count > 0 ? ordered[^1].QuantityAfter : (decimal?)null;
                var next = tied.FirstOrDefault(l => l.QuantityBefore == after) ?? tied[0];
                ordered.Add(next);
                tied.Remove(next);
            }
            i = j;
        }
        return ordered;
    }

    private sealed record HistoryLine(
        Guid LineId,
        DateTimeOffset CreatedAt,
        Guid MovementId,
        string MovementNumber,
        DateTimeOffset MovementDate,
        InventoryMovementType MovementType,
        InventoryMovementReason MovementReason,
        decimal Quantity,
        decimal QuantityBefore,
        decimal QuantityAfter);
}
