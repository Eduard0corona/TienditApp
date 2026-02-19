using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Common.Interfaces;

public sealed record TenantAccessSnapshot(
    bool HasActiveAccess,
    TenantCapability NormalizedCapabilities,
    IReadOnlyCollection<string> Permissions);

public interface ITenantAccessService
{
    Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken);
    Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken);
}
