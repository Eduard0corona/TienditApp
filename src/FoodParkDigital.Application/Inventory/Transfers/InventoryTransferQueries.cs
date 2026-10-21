using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Transfers;

/// <summary>The transfers of the business, the newest first (Fase 27B, ADR 0038).</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryTransfersQuery : IRequest<PaginatedList<InventoryTransferListItemDto>>
{
    /// <summary>Those that left this location or entered it.</summary>
    public Guid? InventoryLocationId { get; init; }
    /// <summary>Folio or location, over every page.</summary>
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetInventoryTransfersQueryHandler : IRequestHandler<GetInventoryTransfersQuery, PaginatedList<InventoryTransferListItemDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryTransfersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<PaginatedList<InventoryTransferListItemDto>> Handle(GetInventoryTransfersQuery request, CancellationToken cancellationToken)
    {
        var transfers = _context.InventoryTransfers.AsNoTracking();
        if (request.InventoryLocationId is { } locationId)
            transfers = transfers.Where(t => t.FromLocationId == locationId || t.ToLocationId == locationId);

        var query =
            from transfer in transfers
            join origin in _context.InventoryLocations.AsNoTracking() on transfer.FromLocationId equals origin.Id
            join destination in _context.InventoryLocations.AsNoTracking() on transfer.ToLocationId equals destination.Id
            select new InventoryTransferListItemDto
            {
                Id = transfer.Id,
                TransferNumber = transfer.TransferNumber,
                FromLocationId = transfer.FromLocationId,
                FromLocationName = origin.Name,
                ToLocationId = transfer.ToLocationId,
                ToLocationName = destination.Name,
                TransferredAt = transfer.TransferredAt,
                TransferredBy = transfer.TransferredBy,
                LineCount = transfer.LineCount,
                TotalCost = transfer.TotalCost
            };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(t => t.TransferNumber.ToLower().Contains(search)
                || t.FromLocationName.ToLower().Contains(search)
                || t.ToLocationName.ToLower().Contains(search));
        }

        query = query.OrderByDescending(t => t.TransferredAt).ThenByDescending(t => t.TransferNumber);
        return PaginatedList<InventoryTransferListItemDto>.CreateAsync(query, request.PageNumber, request.PageSize, cancellationToken);
    }
}

/// <summary>A transfer with each item, what it was worth and what each location had before and after.</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryTransferQuery(Guid Id) : IRequest<InventoryTransferDto>;

public class GetInventoryTransferQueryHandler : IRequestHandler<GetInventoryTransferQuery, InventoryTransferDto>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryTransferQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryTransferDto> Handle(GetInventoryTransferQuery request, CancellationToken cancellationToken)
        => await InventoryTransferReads.TransferAsync(_context, request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(InventoryTransfer), request.Id);
}

/// <summary>How a transfer is read, right after making it too: its items are the lines of its two movements.</summary>
internal static class InventoryTransferReads
{
    public static async Task<InventoryTransferDto?> TransferAsync(IApplicationDbContext context, Guid transferId, CancellationToken cancellationToken)
    {
        var dto = await (
                from transfer in context.InventoryTransfers.AsNoTracking()
                join origin in context.InventoryLocations.AsNoTracking() on transfer.FromLocationId equals origin.Id
                join destination in context.InventoryLocations.AsNoTracking() on transfer.ToLocationId equals destination.Id
                join exit in context.InventoryMovements.AsNoTracking() on transfer.OutMovementId equals exit.Id
                join entry in context.InventoryMovements.AsNoTracking() on transfer.InMovementId equals entry.Id
                where transfer.Id == transferId
                select new InventoryTransferDto
                {
                    Id = transfer.Id,
                    TransferNumber = transfer.TransferNumber,
                    FromLocationId = transfer.FromLocationId,
                    FromLocationName = origin.Name,
                    ToLocationId = transfer.ToLocationId,
                    ToLocationName = destination.Name,
                    TransferredAt = transfer.TransferredAt,
                    TransferredBy = transfer.TransferredBy,
                    LineCount = transfer.LineCount,
                    TotalCost = transfer.TotalCost,
                    Notes = transfer.Notes,
                    OutMovementId = exit.Id,
                    OutMovementNumber = exit.MovementNumber,
                    InMovementId = entry.Id,
                    InMovementNumber = entry.MovementNumber
                })
            .FirstOrDefaultAsync(cancellationToken);
        if (dto is null)
            return null;

        var exits = await (
                from line in context.InventoryMovementLines.AsNoTracking()
                join supply in context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
                where line.MovementId == dto.OutMovementId
                orderby supply.Name
                select new { line.SupplyItemId, supply.Name, supply.Unit, line.Quantity, line.UnitCost, line.TotalCost, line.QuantityBefore, line.QuantityAfter })
            .ToListAsync(cancellationToken);
        var entries = await context.InventoryMovementLines.AsNoTracking()
            .Where(line => line.MovementId == dto.InMovementId)
            .Select(line => new { line.SupplyItemId, line.QuantityBefore, line.QuantityAfter })
            .ToDictionaryAsync(line => line.SupplyItemId, cancellationToken);

        dto.Lines = exits
            .Select(line =>
            {
                var entry = entries.GetValueOrDefault(line.SupplyItemId);
                return new InventoryTransferLineDto
                {
                    SupplyItemId = line.SupplyItemId,
                    SupplyItemName = line.Name,
                    Unit = line.Unit,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    TotalCost = line.TotalCost,
                    FromQuantityBefore = line.QuantityBefore,
                    FromQuantityAfter = line.QuantityAfter,
                    ToQuantityBefore = entry?.QuantityBefore ?? 0,
                    ToQuantityAfter = entry?.QuantityAfter ?? 0
                };
            })
            .ToList();
        return dto;
    }
}
