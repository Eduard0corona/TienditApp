using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Common.Interfaces;

/// <param name="TenantSuspended">The business is suspended or cancelled (billing): its members keep their membership but not the use.</param>
/// <param name="IsManager">An active owner or admin of the business (read from the membership on every request, not from the token).</param>
/// <param name="EffectiveRole">The role the person has in this business now (see TenantPermissionCatalog.EffectiveRole); null without access.</param>
public sealed record TenantAccessSnapshot(
    bool HasActiveAccess,
    TenantCapability NormalizedCapabilities,
    IReadOnlyCollection<string> Permissions,
    bool TenantSuspended = false,
    bool IsManager = false,
    UserRole? EffectiveRole = null);

public interface ITenantAccessService
{
    Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken);
    Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken);
}
