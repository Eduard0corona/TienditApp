using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Queries;

[RequiresPermission(TenantPermission.ManageCatalog)]
public record GetCategoryTemplatesQuery : IRequest<List<CategoryTemplateDto>>;

public class GetCategoryTemplatesQueryHandler : IRequestHandler<GetCategoryTemplatesQuery, List<CategoryTemplateDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetCategoryTemplatesQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<CategoryTemplateDto>> Handle(GetCategoryTemplatesQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanManageCatalog(tenantCapabilities))
            return [];

        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == _tenantService.TenantId)
            .Select(x => new { x.BusinessType, x.Industry })
            .FirstOrDefaultAsync(cancellationToken);

        var industryNormalized = tenant?.Industry?.Trim().ToLowerInvariant();
        var recommendedTemplateId = ResolveRecommendedTemplateId(tenant?.BusinessType, industryNormalized);

        return CategoryTemplateCatalog.GetAll()
            .Select(template => new CategoryTemplateDto
            {
                Id = template.Id,
                Name = template.Name,
                Description = template.Description,
                BusinessType = template.BusinessType,
                IsRecommended = template.Id.Equals(recommendedTemplateId, StringComparison.OrdinalIgnoreCase),
                CategoryCount = template.Categories.Count,
                Categories = template.Categories
                    .Select(c => new CategoryTemplateItemDto
                    {
                        Name = c.Name,
                        Description = c.Description,
                        SortOrder = c.SortOrder
                    })
                    .ToList()
            })
            .ToList();
    }

    private static string ResolveRecommendedTemplateId(Domain.Tenants.Enums.BusinessType? businessType, string? industryNormalized)
    {
        if (!string.IsNullOrWhiteSpace(industryNormalized))
        {
            if (industryNormalized.Contains("lavander"))
                return "laundry";
            if (industryNormalized.Contains("barber"))
                return "barber";
            if (industryNormalized.Contains("alimento") || industryNormalized.Contains("restaurant"))
                return "food";
        }

        return businessType switch
        {
            Domain.Tenants.Enums.BusinessType.Food => "food",
            Domain.Tenants.Enums.BusinessType.Retail => "retail",
            Domain.Tenants.Enums.BusinessType.Service => "services",
            _ => "services"
        };
    }
}
