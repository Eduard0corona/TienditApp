using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Common.Security;

/// <summary>
/// The current user's access to the current business, read once per request: TenantResolutionMiddleware primes it
/// with the snapshot it already loaded, and the authorization behavior and /me/access reuse it.
/// </summary>
public sealed class CurrentAccessContext
{
    public const string PlatformAdminRole = "SUPER_ADMIN";

    private readonly ICurrentUserService _user;
    private readonly ICurrentTenantService _tenant;
    private readonly ITenantAccessService _access;
    private (Guid UserId, Guid TenantId, TenantAccessSnapshot Snapshot)? _cached;
    private (Guid TenantId, TenantCapability Capabilities)? _capabilities;

    public CurrentAccessContext(ICurrentUserService user, ICurrentTenantService tenant, ITenantAccessService access)
    {
        _user = user;
        _tenant = tenant;
        _access = access;
    }

    public bool IsAuthenticated => Guid.TryParse(_user.UserId, out _);
    public bool IsPlatformAdmin => _user.Role == PlatformAdminRole;
    public Guid TenantId => _tenant.TenantId;

    /// <summary>
    /// Owner or admin of the current business, read from the membership on this request: a demotion applies at
    /// once, without waiting for the token to expire. The platform admin manages any business.
    /// </summary>
    public async Task<bool> IsTenantManagerAsync(CancellationToken cancellationToken)
    {
        if (IsPlatformAdmin)
            return true;
        if (_tenant.TenantId == Guid.Empty)
            return TenantManagementRules.CanManageTenant(_user);

        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot is { HasActiveAccess: true, IsManager: true };
    }

    public void Prime(Guid userId, Guid tenantId, TenantAccessSnapshot snapshot) => _cached = (userId, tenantId, snapshot);

    /// <summary>Membership, permissions and modules of the current user in the current business; null without both.</summary>
    public async Task<TenantAccessSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_user.UserId, out var userId) || _tenant.TenantId == Guid.Empty)
            return null;
        if (_cached is { } cached && cached.UserId == userId && cached.TenantId == _tenant.TenantId)
            return cached.Snapshot;

        var snapshot = await _access.GetAccessSnapshotAsync(userId, _tenant.TenantId, cancellationToken);
        _cached = (userId, _tenant.TenantId, snapshot);
        return snapshot;
    }

    /// <summary>The business's modules, also for the platform admin, who is not a member.</summary>
    public async Task<TenantCapability> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return TenantCapability.None;

        var snapshot = await GetSnapshotAsync(cancellationToken);
        if (snapshot is { HasActiveAccess: true })
            return snapshot.NormalizedCapabilities;
        if (_capabilities is { } known && known.TenantId == _tenant.TenantId)
            return known.Capabilities;

        var capabilities = TenantCapability.None;
        foreach (var flag in Enum.GetValues<TenantCapability>().Where(f => f != TenantCapability.None))
        {
            if (await _access.HasCapabilityAsync(_tenant.TenantId, flag, cancellationToken))
                capabilities |= flag;
        }

        _capabilities = (_tenant.TenantId, capabilities);
        return capabilities;
    }

    /// <summary>Whether the business has any of the modules (one query for the platform admin, who is not a member).</summary>
    public async Task<bool> HasAnyCapabilityAsync(TenantCapability anyOf, CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return false;

        var snapshot = await GetSnapshotAsync(cancellationToken);
        if (snapshot is { HasActiveAccess: true })
            return (snapshot.NormalizedCapabilities & anyOf) != 0;
        if (_capabilities is { } known && known.TenantId == _tenant.TenantId)
            return (known.Capabilities & anyOf) != 0;

        return await _access.HasAnyCapabilityAsync(_tenant.TenantId, anyOf, cancellationToken);
    }

    /// <summary>Effective permissions: every permission for owners, admins and the platform admin.</summary>
    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(CancellationToken cancellationToken)
    {
        if (await IsTenantManagerAsync(cancellationToken))
            return Enum.GetNames<TenantPermission>();

        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot is { HasActiveAccess: true } ? snapshot.Permissions : [];
    }
}
