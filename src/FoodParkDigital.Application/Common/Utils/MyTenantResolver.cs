using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Utils;

/// <summary>
/// Resolves the tenant for "my tenant" operations.
/// </summary>
public static class MyTenantResolver
{
    /// <summary>
    /// Uses the tenant validated for the current request (this also honors the SUPER_ADMIN override).
    /// When no tenant was resolved for the request, falls back to the token's tenant, the user's
    /// default tenant or the oldest membership, and only accepts tenants where the user has an
    /// active membership: a disabled member must not keep reading or editing the tenant.
    /// </summary>
    public static async Task<Guid?> ResolveAsync(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        CancellationToken cancellationToken)
    {
        if (tenantService.TenantId != Guid.Empty)
            return tenantService.TenantId;

        if (!Guid.TryParse(currentUserService.UserId, out var userId))
            return null;

        // Pre-tenant scenario: there is no tenant context, so memberships are filtered explicitly
        // by user and active status.
        var activeMemberships = context.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.UserId == userId && tu.Status == TenantUserStatus.Active);

        var claimTenantId = currentUserService.TenantId;
        if (claimTenantId.HasValue
            && await activeMemberships.AnyAsync(tu => tu.TenantId == claimTenantId.Value, cancellationToken))
        {
            return claimTenantId;
        }

        var defaultTenantId = await context.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.DefaultTenantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (defaultTenantId.HasValue
            && await activeMemberships.AnyAsync(tu => tu.TenantId == defaultTenantId.Value, cancellationToken))
        {
            return defaultTenantId;
        }

        return await activeMemberships
            .OrderBy(tu => tu.CreatedAt)
            .Select(tu => (Guid?)tu.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
