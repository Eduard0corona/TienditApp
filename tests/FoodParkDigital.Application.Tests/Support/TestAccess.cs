using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// The access context of a handler under test: the person is an active member of the current business, and an owner
/// or admin there when their membership role says so (what a real request reads from the membership).
/// </summary>
internal static class TestAccess
{
    public static CurrentAccessContext For(ICurrentUserService user, ICurrentTenantService tenant)
        => new(user, tenant, new MembershipOf(user));

    private sealed class MembershipOf(ICurrentUserService user) : ITenantAccessService
    {
        private const TenantCapability EveryModule = TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food
            | TenantCapability.Inventory | TenantCapability.Appointments | TenantCapability.Kitchen;

        private TenantAccessSnapshot Snapshot => new(
            HasActiveAccess: true,
            NormalizedCapabilities: EveryModule,
            Permissions: user.Permissions,
            IsManager: string.Equals(user.TenantRole, nameof(TenantUserRole.Owner), StringComparison.OrdinalIgnoreCase)
                || string.Equals(user.TenantRole, nameof(TenantUserRole.Admin), StringComparison.OrdinalIgnoreCase));

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(Snapshot);

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken)
            => Task.FromResult((EveryModule & capability) == capability);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken)
            => Task.FromResult((EveryModule & capabilities) != 0);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(user.Permissions);

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(user.Permissions.Contains(permission.ToString(), StringComparer.OrdinalIgnoreCase));
    }
}
