using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Queries;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetSupplyItemByIdQuery(Guid Id) : IRequest<SupplyItemDto>;

public class GetSupplyItemByIdQueryHandler : IRequestHandler<GetSupplyItemByIdQuery, SupplyItemDto>
{
    private readonly IApplicationDbContext _context;

    public GetSupplyItemByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SupplyItemDto> Handle(GetSupplyItemByIdQuery request, CancellationToken cancellationToken)
    {
        var supplyItem = await _context.SupplyItems
            .AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new { x.Id, x.TenantId })
            .FirstOrDefaultAsync(cancellationToken);

        if (supplyItem is null)
            throw new EntityNotFoundException("SupplyItem", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            supplyItem.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            throw new EntityNotFoundException("SupplyItem", request.Id);

        var dto = await _context.SupplyItems
            .AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new SupplyItemDto
            {
                Id = x.Id,
                Name = x.Name,
                Code = x.Code,
                Sku = x.Sku,
                Barcode = x.Barcode,
                Description = x.Description,
                Brand = x.Brand,
                Model = x.Model,
                CategoryName = x.CategoryName,
                Unit = x.Unit,
                DefaultCost = x.DefaultCost,
                LastCost = x.LastCost,
                AverageCost = x.AverageCost,
                MinStock = x.MinStock,
                MaxStock = x.MaxStock,
                ReorderPoint = x.ReorderPoint,
                TrackBatches = x.TrackBatches,
                TrackExpiration = x.TrackExpiration,
                IsPerishable = x.IsPerishable,
                IsCritical = x.IsCritical,
                TaxRate = x.TaxRate,
                Notes = x.Notes,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                TotalOnHand = _context.InventoryStocks
                    .Where(s => s.SupplyItemId == x.Id)
                    .Select(s => (decimal?)s.OnHandQuantity)
                    .Sum() ?? 0,
                TotalReserved = _context.InventoryStocks
                    .Where(s => s.SupplyItemId == x.Id)
                    .Select(s => (decimal?)s.ReservedQuantity)
                    .Sum() ?? 0
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
            throw new EntityNotFoundException("SupplyItem", request.Id);

        dto.TotalAvailable = dto.TotalOnHand - dto.TotalReserved;
        dto.IsLowStock = dto.TotalOnHand <= dto.ReorderPoint;
        return dto;
    }
}
