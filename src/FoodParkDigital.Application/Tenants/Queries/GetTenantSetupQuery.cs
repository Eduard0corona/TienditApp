using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Queries;

[RequiresTenantMember]
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
            return Result.Failure<TenantSetupDto>("No se encontró el negocio del usuario.");

        var tenant = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId.Value)
            .Select(t => new { t.BusinessType, t.Industry, HasLogo = t.LogoUrl != null })
            .FirstOrDefaultAsync(cancellationToken);
        if (tenant is null)
            return Result.Failure<TenantSetupDto>("Negocio no encontrado.");

        var settings = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId.Value)
            .Select(x => new
            {
                x.Capabilities,
                x.PricesIncludeTax,
                x.DefaultTaxRate,
                x.OnboardingCompletedAt,
                x.HomeVisitRequestsEnabled,
                x.OnlineMenuMode,
                HasWeeklyHours = x.OpeningHours != null
            })
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
        // A business that has visits to quote on (a trade starts with them on, Fase 29A) has one step more: receiving
        // them, which waits for its zones and its hours. Without the agenda module its settings card is not there either.
        var homeVisitsStep = settings is { HomeVisitRequestsEnabled: true } && TenantCapabilityRules.CanAccessAppointments(capabilities)
            ? new TenantSetupStepDto
            {
                Key = "homeVisits",
                Done = HomeVisitRequestAvailability.ReceivesVisits(
                    enabled: true,
                    capabilities,
                    settings.OnlineMenuMode,
                    hasActiveZones: await _context.DeliveryZones.AnyAsync(zone => zone.IsActive, cancellationToken),
                    settings.HasWeeklyHours)
            }
            : null;

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
                .. (homeVisitsStep is null ? [] : new[] { homeVisitsStep }),
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
                    Categories = template.Categories.Select(category => category.Name).ToList(),
                    IsRecommended = template.IsMadeFor(tenant.Industry)
                })
                .ToList()
        });
    }
}
