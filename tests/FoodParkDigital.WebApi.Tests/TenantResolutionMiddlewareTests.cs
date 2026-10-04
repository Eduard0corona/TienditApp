using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.WebApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.WebApi.Tests;

public class TenantResolutionMiddlewareTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task RouteContainingHealthFragment_ShouldNotSkipPermissionChecks()
    {
        var access = new FakeTenantAccessService(active: true, permissions: [nameof(TenantPermission.ManageCatalog)]);

        var result = await InvokeAsync("/api/v1/orders/health", "GET", "STAFF", access);

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/my-tenant", "GET")]
    [InlineData("/api/v1/auth/my-tenant", "PUT")]
    [InlineData("/api/v1/auth/my-tenant/logo", "POST")]
    [InlineData("/api/v1/marketing/promotions/send", "POST")]
    public async Task TenantEndpoints_ShouldRejectDisabledMembers_EvenForTenantAdmins(string path, string method)
    {
        var access = new FakeTenantAccessService(active: false, permissions: []);

        var result = await InvokeAsync(path, method, "TENANT_ADMIN", access);

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(Guid.Empty, result.TenantService.TenantId);
    }

    [Fact]
    public async Task MyTenant_ShouldResolveTenant_ForActiveMemberWithoutPermissions()
    {
        var access = new FakeTenantAccessService(active: true, permissions: []);

        var result = await InvokeAsync("/api/v1/auth/my-tenant", "GET", "STAFF", access);

        Assert.True(result.NextCalled);
        Assert.Equal(TenantId, result.TenantService.TenantId);
    }

    [Fact]
    public async Task TenantOrders_ShouldRequireManageOrders()
    {
        var path = $"/api/v1/tenants/{TenantId}/orders";

        var denied = await InvokeAsync(path, "POST", "STAFF",
            new FakeTenantAccessService(active: true, permissions: [nameof(TenantPermission.ManageCatalog)]));
        var allowed = await InvokeAsync(path, "POST", "STAFF",
            new FakeTenantAccessService(active: true, permissions: [nameof(TenantPermission.ManageOrders)]));

        Assert.False(denied.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        Assert.True(allowed.NextCalled);
        Assert.Equal(TenantId, allowed.TenantService.TenantId);
    }

    [Theory]
    [InlineData(nameof(TenantPermission.ViewReports), true)]
    [InlineData(nameof(TenantPermission.ManageOrders), false)]
    public async Task Dashboard_ShouldFollowViewReports_ForStaff(string permission, bool allowed)
    {
        var result = await InvokeAsync("/api/v1/dashboard/metrics", "GET", "STAFF",
            new FakeTenantAccessService(active: true, permissions: [permission]));

        Assert.Equal(allowed, result.NextCalled);
        Assert.Equal(allowed ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Kitchen_ShouldRequireManageServiceBoardAndTheKitchenCapability()
    {
        const TenantCapability foodWithKitchen = TenantCapability.Food | TenantCapability.Kitchen;
        string[] boardPermission = [nameof(TenantPermission.ManageServiceBoard)];

        var allowed = await InvokeAsync("/api/v1/kitchen/orders", "GET", "KITCHEN",
            new FakeTenantAccessService(active: true, boardPermission, foodWithKitchen));
        var staffAllowed = await InvokeAsync("/api/v1/kitchen/orders", "GET", "STAFF",
            new FakeTenantAccessService(active: true, boardPermission, foodWithKitchen));
        var withoutPermission = await InvokeAsync("/api/v1/kitchen/orders", "GET", "STAFF",
            new FakeTenantAccessService(active: true, [nameof(TenantPermission.ManageOrders)], foodWithKitchen));
        var withoutCapability = await InvokeAsync("/api/v1/kitchen/orders", "GET", "KITCHEN",
            new FakeTenantAccessService(active: true, boardPermission, TenantCapability.Food));

        Assert.True(allowed.NextCalled);
        Assert.True(staffAllowed.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, withoutPermission.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, withoutCapability.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/refresh-token")]
    [InlineData("/api/v1/auth/register/business")]
    [InlineData("/api/v1/whatsapp/webhook")]
    [InlineData("/hubs/orders/negotiate")]
    public async Task ExcludedPaths_ShouldNotResolveTenant(string path)
    {
        var access = new FakeTenantAccessService(active: true, permissions: []);

        var result = await InvokeAsync(path, "POST", "STAFF", access);

        Assert.True(result.NextCalled);
        Assert.Equal(Guid.Empty, result.TenantService.TenantId);
        Assert.Equal(0, access.Calls);
    }

    [Fact]
    public async Task SuperAdminTenantOverride_ShouldBeAuditLogged()
    {
        var otherTenantId = Guid.NewGuid();
        var logger = new RecordingLogger<TenantResolutionMiddleware>();
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";
        context.Request.Method = "GET";
        context.Request.Headers["X-Tenant-Id"] = otherTenantId.ToString();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(ClaimTypes.Role, "SUPER_ADMIN"),
            new Claim("tenant_id", TenantId.ToString())
        ], authenticationType: "Test"));
        var tenantService = new FakeCurrentTenantService();

        await middleware.InvokeAsync(context, tenantService, new FakeTenantAccessService(active: true, permissions: []));

        Assert.True(nextCalled);
        Assert.Equal(otherTenantId, tenantService.TenantId);
        var entry = Assert.Single(logger.Messages);
        Assert.Contains("SuperAdminTenantOverride", entry);
        Assert.Contains(otherTenantId.ToString(), entry);
        Assert.Contains(UserId.ToString(), entry);
    }

    [Fact]
    public async Task ResolvedTenant_ShouldTagTheRequestMetrics_ForAlertsPerBusiness()
    {
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        var metricsTags = new MetricsTagsFeature();
        context.Features.Set<IHttpMetricsTagsFeature>(metricsTags);
        context.Request.Path = "/api/v1/auth/my-tenant";
        context.Request.Method = "GET";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(ClaimTypes.Role, "STAFF"),
            new Claim("tenant_id", TenantId.ToString())
        ], authenticationType: "Test"));

        await middleware.InvokeAsync(context, new FakeCurrentTenantService(), new FakeTenantAccessService(active: true, permissions: []));

        var tag = Assert.Single(metricsTags.Tags);
        Assert.Equal("tenant.id", tag.Key);
        Assert.Equal(TenantId.ToString(), tag.Value);
    }

    private sealed class MetricsTagsFeature : IHttpMetricsTagsFeature
    {
        public ICollection<KeyValuePair<string, object?>> Tags { get; } = [];
        public bool MetricsDisabled { get; set; }
    }

    private static async Task<InvocationResult> InvokeAsync(
        string path,
        string method,
        string role,
        FakeTenantAccessService access)
    {
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("tenant_id", TenantId.ToString())
        ], authenticationType: "Test"));

        var tenantService = new FakeCurrentTenantService();
        await middleware.InvokeAsync(context, tenantService, access);

        return new InvocationResult(nextCalled, context.Response.StatusCode, tenantService);
    }

    private sealed record InvocationResult(bool NextCalled, int StatusCode, FakeCurrentTenantService TenantService);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class FakeCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class FakeTenantAccessService(
        bool active,
        IReadOnlyCollection<string> permissions,
        TenantCapability capabilities = TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food | TenantCapability.Inventory)
        : ITenantAccessService
    {
        public int Calls { get; private set; }

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new TenantAccessSnapshot(
                active,
                capabilities,
                active ? permissions : []));
        }

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(active);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(permissions);

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken) =>
            Task.FromResult(permissions.Contains(permission.ToString()));
    }
}
