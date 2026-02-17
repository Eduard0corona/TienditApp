using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Inventory.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Queries;

public record GetSupplyItemsQuery : IRequest<PaginatedList<SupplyItemDto>>
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public bool? LowStockOnly { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetSupplyItemsQueryHandler : IRequestHandler<GetSupplyItemsQuery, PaginatedList<SupplyItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetSupplyItemsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<SupplyItemDto>> Handle(GetSupplyItemsQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return new PaginatedList<SupplyItemDto>([], 0, request.PageNumber, request.PageSize);

        var query = _context.SupplyItems.AsNoTracking();

        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                x.Name.ToLower().Contains(search) ||
                (x.Code != null && x.Code.ToLower().Contains(search)) ||
                (x.Sku != null && x.Sku.ToLower().Contains(search)) ||
                (x.Barcode != null && x.Barcode.ToLower().Contains(search)) ||
                (x.Brand != null && x.Brand.ToLower().Contains(search)));
        }

        var projected = query
            .OrderBy(x => x.Name)
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
            });

        if (request.LowStockOnly.GetValueOrDefault())
            projected = projected.Where(x => x.TotalOnHand <= x.ReorderPoint);

        var list = await PaginatedList<SupplyItemDto>.CreateAsync(projected, request.PageNumber, request.PageSize, cancellationToken);
        list.Items.ForEach(x =>
        {
            x.TotalAvailable = x.TotalOnHand - x.TotalReserved;
            x.IsLowStock = x.TotalOnHand <= x.ReorderPoint;
        });

        return list;
    }
}
