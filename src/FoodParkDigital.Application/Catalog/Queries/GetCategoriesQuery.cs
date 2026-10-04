using AutoMapper;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Queries;

[RequiresPermission(TenantPermission.ManageCatalog, TenantPermission.ManageOrders, TenantPermission.ManageInventory, TenantPermission.ManageAppointments)]
public record GetCategoriesQuery : IRequest<List<CategoryDto>>
{
    /// <summary>Also the archived ones, to restore them (Categorías). Menus and forms list only the active ones.</summary>
    public bool IncludeArchived { get; init; }
}

public class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, List<CategoryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public GetCategoriesQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<List<CategoryDto>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanManageCatalog(tenantCapabilities))
            return [];

        var query = _context.Categories.AsNoTracking();
        if (!request.IncludeArchived)
            query = query.Where(c => c.IsActive);
        var categories = await query
            .OrderBy(c => !c.IsActive)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<CategoryDto>>(categories);
        var ids = categories.Select(c => c.Id).ToList();
        var counts = await _context.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.CategoryId))
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);
        foreach (var dto in dtos)
            dto.ProductCount = counts.GetValueOrDefault(dto.Id);
        return dtos;
    }
}
