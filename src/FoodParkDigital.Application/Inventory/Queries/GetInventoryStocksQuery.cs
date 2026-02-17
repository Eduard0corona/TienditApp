using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Inventory.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Queries;

public record GetInventoryStocksQuery : IRequest<PaginatedList<InventoryStockDto>>
{
    public string? Search { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public bool? LowStockOnly { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetInventoryStocksQueryHandler : IRequestHandler<GetInventoryStocksQuery, PaginatedList<InventoryStockDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetInventoryStocksQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<InventoryStockDto>> Handle(GetInventoryStocksQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return new PaginatedList<InventoryStockDto>([], 0, request.PageNumber, request.PageSize);

        var query =
            from stock in _context.InventoryStocks.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on stock.SupplyItemId equals supply.Id
            join location in _context.InventoryLocations.AsNoTracking() on stock.InventoryLocationId equals location.Id
            select new InventoryStockDto
            {
                StockId = stock.Id,
                SupplyItemId = supply.Id,
                SupplyItemName = supply.Name,
                SupplyCode = supply.Code,
                SupplySku = supply.Sku,
                SupplyBarcode = supply.Barcode,
                Unit = supply.Unit,
                InventoryLocationId = location.Id,
                InventoryLocationName = location.Name,
                InventoryLocationCode = location.Code,
                OnHandQuantity = stock.OnHandQuantity,
                ReservedQuantity = stock.ReservedQuantity,
                AvailableQuantity = stock.OnHandQuantity - stock.ReservedQuantity,
                MinStock = supply.MinStock,
                ReorderPoint = supply.ReorderPoint,
                MaxStock = supply.MaxStock,
                LastCost = stock.LastCost,
                LastMovementAt = stock.LastMovementAt,
                IsSupplyActive = supply.IsActive,
                IsLocationActive = location.IsActive,
                IsLowStock = stock.OnHandQuantity <= supply.ReorderPoint
            };

        if (request.InventoryLocationId.HasValue)
            query = query.Where(x => x.InventoryLocationId == request.InventoryLocationId.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                x.SupplyItemName.ToLower().Contains(search) ||
                (x.SupplyCode != null && x.SupplyCode.ToLower().Contains(search)) ||
                (x.SupplySku != null && x.SupplySku.ToLower().Contains(search)) ||
                (x.SupplyBarcode != null && x.SupplyBarcode.ToLower().Contains(search)) ||
                x.InventoryLocationName.ToLower().Contains(search));
        }

        if (request.LowStockOnly.GetValueOrDefault())
            query = query.Where(x => x.IsLowStock);

        query = query.OrderBy(x => x.SupplyItemName).ThenBy(x => x.InventoryLocationName);
        return await PaginatedList<InventoryStockDto>.CreateAsync(query, request.PageNumber, request.PageSize, cancellationToken);
    }
}
