using System.Security.Claims;
using FoodParkDigital.Application.Audit;
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
    public async Task RouteContainingHealthFragment_ShouldNotSkipTheMembershipCheck()
    {
        var access = new FakeTenantAccessService(active: false, permissions: [nameof(TenantPermission.ManageOrders)]);

        var result = await InvokeAsync("/api/v1/orders/health", "GET", "STAFF", access);

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(1, access.Calls);
    }

    [Theory]
    [InlineData("/api/v1/employees")]
    [InlineData("/api/v1/tenants/9a4b8f0e-2f6c-4a51-9d39-0d6f1f4f3b11/orders")]
    [InlineData("/api/v1/a-route-added-later")]
    public async Task BusinessRoutes_ShouldNeedAnActiveMembership_AndLeavePermissionsToTheUseCase(string path)
    {
        // Every /api route acts on the business of the token unless it is about the account: a route nobody listed is
        // checked too. Who may do what in it (permission and module) is declared by its request.
        var disabled = await InvokeAsync(path, "POST", "STAFF", new FakeTenantAccessService(active: false, permissions: []));
        var memberWithoutPermissions = await InvokeAsync(path, "POST", "STAFF", new FakeTenantAccessService(active: true, permissions: []));

        Assert.False(disabled.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, disabled.StatusCode);
        Assert.True(memberWithoutPermissions.NextCalled);
        Assert.Equal(TenantId, memberWithoutPermissions.TenantService.TenantId);
    }

    [Theory]
    [InlineData("/api/v1/auth/profile")]
    [InlineData("/api/v1/auth/switch-tenant")]
    [InlineData("/api/v1/me/tenants")]
    [InlineData("/api/v1/twofactor/enable")]
    [InlineData("/api/v1/platform/overview")]
    [InlineData("/api/v1/tenants")]
    [InlineData("/api/v1/public/menu/demo")]
    public async Task AccountRoutes_ShouldNotNeedTheMembership(string path)
    {
        // A person who lost access to the token's business still reaches their profile and the switch to another one.
        var access = new FakeTenantAccessService(active: false, permissions: []);

        var result = await InvokeAsync(path, "GET", "STAFF", access);

        Assert.True(result.NextCalled);
        Assert.Equal(0, access.Calls);
    }

    [Fact]
    public async Task BusinessRoute_ShouldAskToChooseABusiness_WhenTheTokenHasNone()
    {
        var withoutBusiness = await InvokeAsync(
            "/api/v1/orders", "GET", "STAFF", new FakeTenantAccessService(active: true, permissions: []), withBusiness: false);
        var account = await InvokeAsync(
            "/api/v1/me/tenants", "GET", "STAFF", new FakeTenantAccessService(active: true, permissions: []), withBusiness: false);

        Assert.False(withoutBusiness.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, withoutBusiness.StatusCode);
        Assert.True(account.NextCalled);
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

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Branches_ShouldReachEveryMember_WithoutTheInventoryModule(string method)
    {
        // Who may read or manage them is decided by the use case (any member reads, owners and admins manage).
        var result = await InvokeAsync("/api/v1/branches", method, "STAFF",
            new FakeTenantAccessService(active: true, [nameof(TenantPermission.ManageOrders)], TenantCapability.Service));

        Assert.True(result.NextCalled);
        Assert.Equal(TenantId, result.TenantService.TenantId);
    }

    [Theory]
    [InlineData("/api/v1/orders", "GET", false)]
    [InlineData("/api/v1/auth/my-tenant", "PUT", false)]
    [InlineData("/api/v1/auth/my-tenant", "GET", true)]
    [InlineData("/api/v1/auth/my-tenant/subscription", "GET", true)]
    public async Task SuspendedBusiness_ShouldOnlyReadWhyItIsSuspended(string path, string method, bool allowed)
    {
        var result = await InvokeAsync(path, method, "TENANT_ADMIN",
            new FakeTenantAccessService(active: true, [], suspended: true, isManager: true));

        Assert.Equal(allowed, result.NextCalled);
        Assert.Equal(allowed ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden, result.StatusCode);
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
    public async Task SuperAdminTenantOverride_ShouldBeRecordedInTheBusiness_BeforeTheRequestRuns()
    {
        var otherTenantId = Guid.NewGuid();
        var logger = new RecordingLogger<TenantResolutionMiddleware>();
        var audit = new FakeSupportAccessAudit();

        var result = await InvokeAsSuperAdminAsync("/api/v1/customers", otherTenantId, audit, logger, query: "?search=5512345678");

        Assert.True(result.NextCalled);
        Assert.Equal(otherTenantId, result.TenantService.TenantId);
        var recorded = Assert.Single(audit.Recorded);
        Assert.Equal((otherTenantId, TenantId, "GET", "/api/v1/customers"), recorded);
        Assert.True(audit.RecordedBeforeNext);
        // The log line stays, for the request log.
        var entry = Assert.Single(logger.Messages);
        Assert.Contains("SuperAdminTenantOverride", entry);
        Assert.Contains(otherTenantId.ToString(), entry);
        Assert.Contains(UserId.ToString(), entry);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SuperAdminTenantOverride_ShouldNotRun_WhenItCannotBeRecorded(bool withAuditService)
    {
        var result = await InvokeAsSuperAdminAsync(
            "/api/v1/orders", Guid.NewGuid(), withAuditService ? new FakeSupportAccessAudit(fails: true) : null);

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Contains("\"code\":\"server_error\"", result.Body);
    }

    [Fact]
    public async Task SuperAdminTenantOverride_ShouldNotRun_ForABusinessThatDoesNotExist()
    {
        var result = await InvokeAsSuperAdminAsync("/api/v1/orders", Guid.NewGuid(), new FakeSupportAccessAudit(exists: false));

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/orders", true)]
    [InlineData("/api/v1/me", false)]
    public async Task SuperAdmin_ShouldNotBeRecorded_InTheirOwnBusinessOrOnAccountRoutes(string path, bool ownBusiness)
    {
        var audit = new FakeSupportAccessAudit();

        var result = await InvokeAsSuperAdminAsync(path, ownBusiness ? TenantId : Guid.NewGuid(), audit);

        Assert.True(result.NextCalled);
        Assert.Empty(audit.Recorded);
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
        FakeTenantAccessService access,
        bool withBusiness = true)
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
        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Role, role)];
        if (withBusiness)
            claims.Add(new Claim("tenant_id", TenantId.ToString()));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));

        var tenantService = new FakeCurrentTenantService();
        await middleware.InvokeAsync(context, tenantService, access);

        return new InvocationResult(nextCalled, context.Response.StatusCode, tenantService);
    }

    private sealed record InvocationResult(bool NextCalled, int StatusCode, FakeCurrentTenantService TenantService);

    private static async Task<SuperAdminResult> InvokeAsSuperAdminAsync(
        string path,
        Guid headerTenantId,
        FakeSupportAccessAudit? audit,
        ILogger<TenantResolutionMiddleware>? logger = null,
        string query = "")
    {
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            if (audit is not null)
                audit.RecordedBeforeNext = audit.Recorded.Count > 0;
            return Task.CompletedTask;
        }, logger);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Request.Method = "GET";
        context.Request.Headers["X-Tenant-Id"] = headerTenantId.ToString();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(ClaimTypes.Role, "SUPER_ADMIN"),
            new Claim("tenant_id", TenantId.ToString())
        ], authenticationType: "Test"));
        var tenantService = new FakeCurrentTenantService();

        await middleware.InvokeAsync(
            context, tenantService, new FakeTenantAccessService(active: false, permissions: []), supportAccessAudit: audit);

        var body = System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        return new SuperAdminResult(nextCalled, context.Response.StatusCode, tenantService, body);
    }

    private sealed record SuperAdminResult(bool NextCalled, int StatusCode, FakeCurrentTenantService TenantService, string Body);

    private sealed class FakeSupportAccessAudit(bool exists = true, bool fails = false) : ISupportAccessAudit
    {
        public List<(Guid TenantId, Guid SessionTenantId, string Method, string Path)> Recorded { get; } = [];
        public bool RecordedBeforeNext { get; set; }

        public Task<bool> RecordAsync(Guid tenantId, Guid sessionTenantId, string method, string path, CancellationToken cancellationToken)
        {
            if (fails)
                throw new InvalidOperationException("The database is not available.");
            if (!exists)
                return Task.FromResult(false);

            Recorded.Add((tenantId, sessionTenantId, method, path));
            return Task.FromResult(true);
        }
    }

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
        TenantCapability capabilities = TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food | TenantCapability.Inventory,
        bool suspended = false,
        bool isManager = false)
        : ITenantAccessService
    {
        public int Calls { get; private set; }

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new TenantAccessSnapshot(
                active,
                capabilities,
                active ? permissions : [],
                suspended,
                active && isManager));
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
