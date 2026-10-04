using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.WebApi.Hubs;

/// <summary>
/// Tenant access of a hub connection, validated against the database like the HTTP middleware does:
/// the tenant comes from the token, and the user must have an active membership in it.
/// </summary>
public sealed record HubTenantAccess(Guid TenantId, string? Role, IReadOnlyCollection<string> Permissions)
{
    public bool IsSuperAdmin => Role == "SUPER_ADMIN";

    public bool IsTenantAdmin => Role == "TENANT_ADMIN";

    // Mirrors TenantResolutionMiddleware: SUPER_ADMIN and TENANT_ADMIN are not restricted by permission codes.
    public bool HasAnyPermission(params TenantPermission[] permissions) =>
        IsSuperAdmin
        || IsTenantAdmin
        || permissions.Any(permission => Permissions.Contains(permission.ToString(), StringComparer.OrdinalIgnoreCase));

    public static async Task<HubTenantAccess?> ResolveAsync(
        ClaimsPrincipal? user,
        ITenantAccessService tenantAccessService,
        CancellationToken cancellationToken)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId))
            return null;

        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        if (role == "SUPER_ADMIN")
            return new HubTenantAccess(tenantId, role, []);

        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return null;

        var snapshot = await tenantAccessService.GetAccessSnapshotAsync(userId, tenantId, cancellationToken);
        // A business suspended for billing gets no live updates either.
        return snapshot is { HasActiveAccess: true, TenantSuspended: false }
            ? new HubTenantAccess(tenantId, role, snapshot.Permissions)
            : null;
    }
}
