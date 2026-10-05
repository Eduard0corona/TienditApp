using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.WebApi.Hubs;

namespace FoodParkDigital.WebApi.Tests;

public class HubTenantAccessTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task ResolveAsync_ShouldRejectDisabledMembership()
    {
        var access = await HubTenantAccess.ResolveAsync(User("STAFF"), new FakeAccess(active: false), CancellationToken.None);

        Assert.Null(access);
    }

    [Fact]
    public async Task ResolveAsync_ShouldRejectTokenWithoutTenant()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "STAFF")],
            authenticationType: "Test"));

        var access = await HubTenantAccess.ResolveAsync(principal, new FakeAccess(active: true), CancellationToken.None);

        Assert.Null(access);
    }

    [Fact]
    public async Task ChatEvents_ShouldRequireManageWhatsApp_ForStaff()
    {
        var kitchen = await HubTenantAccess.ResolveAsync(
            User("KITCHEN"), new FakeAccess(true, nameof(TenantPermission.ManageOrders)), CancellationToken.None);
        var agent = await HubTenantAccess.ResolveAsync(
            User("STAFF"), new FakeAccess(true, nameof(TenantPermission.ManageWhatsApp)), CancellationToken.None);

        Assert.NotNull(kitchen);
        Assert.False(kitchen!.HasAnyPermission(TenantPermission.ManageWhatsApp));
        Assert.NotNull(agent);
        Assert.True(agent!.HasAnyPermission(TenantPermission.ManageWhatsApp));
        Assert.Equal(TenantId, agent.TenantId);
    }

    [Fact]
    public async Task EveryPermission_ShouldComeFromTheMembership_NotFromTheTokenRole()
    {
        // A TENANT_ADMIN token issued before a demotion, or of an account that owns another business, but staff here.
        var staffHere = await HubTenantAccess.ResolveAsync(
            User("TENANT_ADMIN"), new FakeAccess(true, nameof(TenantPermission.ManageOrders)), CancellationToken.None);
        var ownerHere = await HubTenantAccess.ResolveAsync(
            User("STAFF"), new FakeAccess(true) { IsManager = true }, CancellationToken.None);

        Assert.False(staffHere!.HasAnyPermission(TenantPermission.ManageWhatsApp));
        Assert.True(ownerHere!.HasAnyPermission(TenantPermission.ManageWhatsApp));
    }

    [Fact]
    public async Task SuperAdmin_ShouldNotRequireMembership()
    {
        var access = await HubTenantAccess.ResolveAsync(User("SUPER_ADMIN"), new FakeAccess(active: false), CancellationToken.None);

        Assert.NotNull(access);
        Assert.True(access!.HasAnyPermission(TenantPermission.ManageWhatsApp));
    }

    private static ClaimsPrincipal User(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("tenant_id", TenantId.ToString())
        ], authenticationType: "Test"));

    private sealed class FakeAccess(bool active, params string[] permissions) : ITenantAccessService
    {
        public bool IsManager { get; init; }

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantAccessSnapshot(active, TenantCapability.Retail, active ? permissions : [], IsManager: active && IsManager));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(active);
        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(permissions);
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken) =>
            Task.FromResult(permissions.Contains(permission.ToString()));
    }
}
