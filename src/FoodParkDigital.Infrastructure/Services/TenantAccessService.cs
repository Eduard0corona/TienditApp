using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

public class TenantAccessService : ITenantAccessService
{
    private readonly IApplicationDbContext _context;

    public TenantAccessService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        return await _context.TenantUsers
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.UserId == userId
                    && x.TenantId == tenantId
                    && x.Status == TenantUserStatus.Active,
                cancellationToken);
    }

    public async Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken)
    {
        var capabilities = await GetNormalizedCapabilitiesAsync(tenantId, cancellationToken);

        return (capabilities & capability) == capability;
    }

    public async Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await GetNormalizedCapabilitiesAsync(tenantId, cancellationToken);

        return (tenantCapabilities & capabilities) != 0;
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var membership = await _context.TenantUsers
            .IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.TenantId == tenantId)
            .Select(x => new
            {
                x.Status,
                x.Role,
                x.PermissionCodes,
                UserRole = _context.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.Id == x.UserId)
                    .Select(u => (UserRole?)u.Role)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null || membership.Status != TenantUserStatus.Active)
            return [];

        var userRole = membership.UserRole ?? UserRole.Staff;

        return TenantPermissionCatalog.ResolveEffectiveCodes(
            membership.PermissionCodes,
            userRole,
            membership.Role);
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        Guid tenantId,
        TenantPermission permission,
        CancellationToken cancellationToken)
    {
        var permissions = await GetPermissionsAsync(userId, tenantId, cancellationToken);
        return permissions.Contains(permission.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<TenantCapability> GetNormalizedCapabilitiesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var configuredCapabilities = await _context.TenantSettings
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .Select(x => (TenantCapability?)x.Capabilities)
            .FirstOrDefaultAsync(cancellationToken);

        var businessType = await _context.Tenants
            .IgnoreQueryFilters()
            .Where(x => x.Id == tenantId)
            .Select(x => (BusinessType?)x.BusinessType)
            .FirstOrDefaultAsync(cancellationToken);

        return TenantCapabilityRules.NormalizeCapabilities(
            configuredCapabilities ?? TenantCapability.None,
            businessType);
    }
}
