using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Queries;

public record GetTenantSetupQuery : IRequest<Result<TenantSetupDto>>;

public class GetTenantSetupQueryHandler : IRequestHandler<GetTenantSetupQuery, Result<TenantSetupDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;

    public GetTenantSetupQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
    }

    public async Task<Result<TenantSetupDto>> Handle(GetTenantSetupQuery request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure<TenantSetupDto>("No se encontro el negocio del usuario.");

        var tenant = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId.Value)
            .Select(t => new { t.BusinessType, HasLogo = t.LogoUrl != null })
            .FirstOrDefaultAsync(cancellationToken);
        if (tenant is null)
            return Result.Failure<TenantSetupDto>("Negocio no encontrado.");

        var settings = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId.Value)
            .Select(x => new { x.Capabilities, x.PricesIncludeTax, x.DefaultTaxRate, x.OnboardingCompletedAt })
            .FirstOrDefaultAsync(cancellationToken);
        var capabilities = TenantCapabilityRules.NormalizeCapabilities(
            settings?.Capabilities ?? TenantCapability.None,
            tenant.BusinessType);

        // Tenant-scoped sets: the query filter keeps them to the current business.
        var hasProducts = await _context.Products.AnyAsync(cancellationToken);
        var hasCashSession = await _context.CashSessions.AnyAsync(cancellationToken);
        var hasOrder = await _context.Orders.AnyAsync(cancellationToken);
        var activeMembers = await _context.TenantUsers
            .CountAsync(member => member.Status == TenantUserStatus.Active, cancellationToken);

        return Result.Success(new TenantSetupDto
        {
            BusinessType = tenant.BusinessType,
            Capabilities = capabilities,
            PricesIncludeTax = settings?.PricesIncludeTax ?? false,
            DefaultTaxRate = settings?.DefaultTaxRate ?? VatRate.GeneralPercent,
            CompletedAt = settings?.OnboardingCompletedAt,
            HasProducts = hasProducts,
            Steps =
            [
                new TenantSetupStepDto { Key = "salesPolicy", Done = settings?.OnboardingCompletedAt is not null },
                new TenantSetupStepDto { Key = "catalog", Done = hasProducts },
                new TenantSetupStepDto { Key = "cash", Done = hasCashSession },
                new TenantSetupStepDto { Key = "firstSale", Done = hasOrder },
                new TenantSetupStepDto { Key = "team", Done = activeMembers > 1 },
                new TenantSetupStepDto { Key = "logo", Done = tenant.HasLogo }
            ],
            StarterCatalogs = StarterCatalogTemplates.GetAll()
                .Select(template => new StarterCatalogDto
                {
                    Id = template.Id,
                    Name = template.Name,
                    Description = template.Description,
                    BusinessType = template.BusinessType,
                    ProductCount = template.ProductCount,
                    Categories = template.Categories.Select(category => category.Name).ToList()
                })
                .ToList()
        });
    }
}
