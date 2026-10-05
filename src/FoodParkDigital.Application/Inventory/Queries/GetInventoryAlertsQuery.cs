using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Queries;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryAlertsQuery : IRequest<InventoryAlertsDto>;

public class GetInventoryAlertsQueryHandler : IRequestHandler<GetInventoryAlertsQuery, InventoryAlertsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetInventoryAlertsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<InventoryAlertsDto> Handle(GetInventoryAlertsQuery request, CancellationToken cancellationToken)
    {
        // Services only need a recipe where the business sells them.
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);

        var lowStockItems = await (
            from stock in _context.InventoryStocks.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on stock.SupplyItemId equals supply.Id
            join location in _context.InventoryLocations.AsNoTracking() on stock.InventoryLocationId equals location.Id
            where supply.IsActive && stock.OnHandQuantity <= supply.ReorderPoint
            orderby supply.IsCritical descending, (supply.ReorderPoint - stock.OnHandQuantity) descending
            select new InventoryAlertItemDto
            {
                SupplyItemId = supply.Id,
                SupplyItemName = supply.Name,
                InventoryLocationId = location.Id,
                InventoryLocationName = location.Name,
                OnHandQuantity = stock.OnHandQuantity,
                ReorderPoint = supply.ReorderPoint,
                Deficit = supply.ReorderPoint - stock.OnHandQuantity,
                IsOutOfStock = stock.OnHandQuantity <= 0,
                IsCritical = supply.IsCritical
            })
            .Take(50)
            .ToListAsync(cancellationToken);

        var lowStockCount = await (
            from stock in _context.InventoryStocks.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on stock.SupplyItemId equals supply.Id
            where supply.IsActive && stock.OnHandQuantity <= supply.ReorderPoint
            select stock.Id)
            .CountAsync(cancellationToken);

        var outOfStockCount = await (
            from stock in _context.InventoryStocks.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on stock.SupplyItemId equals supply.Id
            where supply.IsActive && stock.OnHandQuantity <= 0
            select stock.Id)
            .CountAsync(cancellationToken);

        var criticalLowStockCount = await (
            from stock in _context.InventoryStocks.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on stock.SupplyItemId equals supply.Id
            where supply.IsActive && supply.IsCritical && stock.OnHandQuantity <= supply.ReorderPoint
            select stock.Id)
            .CountAsync(cancellationToken);

        var serviceUnits = new[] { UnitType.Hour, UnitType.Session, UnitType.Service };

        var missingRecipeProductCount = await _context.Products
            .AsNoTracking()
            .Where(x => x.IsAvailable)
            .Where(x =>
                (!x.Unit.HasValue || !serviceUnits.Contains(x.Unit.Value) || TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, Domain.Orders.Enums.OrderType.Service)))
            .Where(x => !_context.ProductSupplyRequirements.Any(req => req.ProductId == x.Id && req.IsActive))
            .CountAsync(cancellationToken);

        return new InventoryAlertsDto
        {
            LowStockCount = lowStockCount,
            CriticalLowStockCount = criticalLowStockCount,
            OutOfStockCount = outOfStockCount,
            MissingRecipeProductCount = missingRecipeProductCount,
            Items = lowStockItems
        };
    }
}
