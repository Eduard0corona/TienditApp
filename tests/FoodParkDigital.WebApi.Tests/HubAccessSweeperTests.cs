using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.WebApi.Hubs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Live connections are checked again like new ones: those whose session ended or that lost the access their hub
/// requires are closed, the rest stay, and the business access is read once per person and business.
/// </summary>
public class HubAccessSweeperTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Connections_WhoseSessionEnded_OrThatLostTheirAccess_ShouldClose()
    {
        var cashier = Guid.NewGuid();
        var demoted = Guid.NewGuid();
        var stillOpen = Connection(cashier, Guid.NewGuid());
        var secondTab = Connection(cashier, Guid.NewGuid());
        var signedOut = Connection(cashier, Guid.NewGuid());
        var withoutPermission = Connection(demoted, Guid.NewGuid());
        var access = new FakeAccess
        {
            [cashier] = [nameof(TenantPermission.ManageOrders)],
            [demoted] = [nameof(TenantPermission.ManageCatalog)]
        };
        var (sweeper, connections) = Sweeper(new FakeSessions(closed: SessionOf(signedOut)), access,
            stillOpen, secondTab, signedOut, withoutPermission);

        var closed = await sweeper.SweepAsync(CancellationToken.None);

        Assert.Equal(2, closed);
        Assert.True(signedOut.Aborted);
        Assert.True(withoutPermission.Aborted);
        Assert.False(stillOpen.Aborted);
        Assert.False(secondTab.Aborted);
        Assert.Equal(new[] { stillOpen.ConnectionId, secondTab.ConnectionId }.Order(),
            connections.Snapshot().Select(c => c.Context.ConnectionId).Order());
        // Two people with connections whose session is open: two reads, not one per connection.
        Assert.Equal(2, access.Reads);
    }

    [Fact]
    public async Task MembershipDisabled_ShouldCloseItsConnections()
    {
        var member = Guid.NewGuid();
        var connection = Connection(member, Guid.NewGuid());
        var (sweeper, _) = Sweeper(new FakeSessions(), new FakeAccess { Active = false }, connection);

        await sweeper.SweepAsync(CancellationToken.None);

        Assert.True(connection.Aborted);
    }

    private static (HubAccessSweeper Sweeper, HubConnections Connections) Sweeper(
        IAccessTokenSessionValidator sessions,
        ITenantAccessService access,
        params FakeContext[] live)
    {
        var connections = new HubConnections();
        foreach (var context in live)
            connections.Add(context, a => a.HasAnyPermission(TenantPermission.ManageOrders));

        var provider = new ServiceCollection()
            .AddSingleton(sessions)
            .AddSingleton(access)
            .BuildServiceProvider();
        var sweeper = new HubAccessSweeper(
            connections,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new HubAccessOptions()),
            NullLogger<HubAccessSweeper>.Instance);
        return (sweeper, connections);
    }

    private static FakeContext Connection(Guid userId, Guid sessionId) => new(new ClaimsPrincipal(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.Role, "STAFF"),
        new Claim("tenant_id", TenantId.ToString()),
        new Claim(SessionClaims.SessionId, sessionId.ToString())
    ], authenticationType: "Test")));

    private static string SessionOf(FakeContext context) => context.User.FindFirst(SessionClaims.SessionId)!.Value;

    private sealed class FakeContext(ClaimsPrincipal user) : HubCallerContext
    {
        public bool Aborted { get; private set; }

        public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");
        public override string? UserIdentifier => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        public override ClaimsPrincipal User { get; } = user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort() => Aborted = true;
    }

    /// <summary>Every session is open except the closed one.</summary>
    private sealed class FakeSessions(string? closed = null) : IAccessTokenSessionValidator
    {
        public Task<bool> IsOpenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<bool>> AreOpenAsync(IReadOnlyList<ClaimsPrincipal> principals, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<bool>>(principals
                .Select(p => p.FindFirst(SessionClaims.SessionId)?.Value != closed)
                .ToList());
    }

    /// <summary>The permissions of each person in the business; counts the reads.</summary>
    private sealed class FakeAccess : Dictionary<Guid, string[]>, ITenantAccessService
    {
        public bool Active { get; init; } = true;
        public int Reads { get; private set; }

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new TenantAccessSnapshot(Active, TenantCapability.None, TryGetValue(userId, out var codes) ? codes : []));
        }

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
