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

    public async Task<TenantAccessSnapshot> GetAccessSnapshotAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _context.TenantUsers
            .IgnoreQueryFilters()
            .AsNoTracking()
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
                    .FirstOrDefault(),
                BusinessType = _context.Tenants
                    .IgnoreQueryFilters()
                    .Where(t => t.Id == x.TenantId)
                    .Select(t => (BusinessType?)t.BusinessType)
                    .FirstOrDefault(),
                TenantStatus = _context.Tenants
                    .IgnoreQueryFilters()
                    .Where(t => t.Id == x.TenantId)
                    .Select(t => (TenantStatus?)t.Status)
                    .FirstOrDefault(),
                Capabilities = _context.TenantSettings
                    .IgnoreQueryFilters()
                    .Where(s => s.TenantId == x.TenantId)
                    .Select(s => (TenantCapability?)s.Capabilities)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return new TenantAccessSnapshot(
                HasActiveAccess: false,
                NormalizedCapabilities: TenantCapability.None,
                Permissions: []);
        }

        var normalizedCapabilities = TenantCapabilityRules.NormalizeCapabilities(
            snapshot.Capabilities ?? TenantCapability.None,
            snapshot.BusinessType);

        if (snapshot.Status != TenantUserStatus.Active)
        {
            return new TenantAccessSnapshot(
                HasActiveAccess: false,
                NormalizedCapabilities: normalizedCapabilities,
                Permissions: []);
        }

        var userRole = snapshot.UserRole ?? UserRole.Staff;
        var permissions = TenantPermissionCatalog.ResolveEffectiveCodes(
            snapshot.PermissionCodes,
            userRole,
            snapshot.Role);

        return new TenantAccessSnapshot(
            HasActiveAccess: true,
            NormalizedCapabilities: normalizedCapabilities,
            Permissions: permissions,
            TenantSuspended: snapshot.TenantStatus is TenantStatus.Suspended or TenantStatus.Inactive);
    }

    public async Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var snapshot = await GetAccessSnapshotAsync(userId, tenantId, cancellationToken);
        return snapshot.HasActiveAccess;
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
        var snapshot = await GetAccessSnapshotAsync(userId, tenantId, cancellationToken);
        return snapshot.Permissions;
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
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => (TenantCapability?)x.Capabilities)
            .FirstOrDefaultAsync(cancellationToken);

        var businessType = await _context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => (BusinessType?)x.BusinessType)
            .FirstOrDefaultAsync(cancellationToken);

        return TenantCapabilityRules.NormalizeCapabilities(
            configuredCapabilities ?? TenantCapability.None,
            businessType);
    }
}
