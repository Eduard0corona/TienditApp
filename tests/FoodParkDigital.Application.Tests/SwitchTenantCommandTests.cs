using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

public class SwitchTenantCommandTests
{
    [Fact]
    public async Task Switch_ShouldRevokeTheSessionsRefreshToken_AndOnlyThatOne()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
        var userId = Guid.NewGuid();
        var targetTenantId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = "staff@example.com",
            UserName = "staff@example.com",
            FirstName = "Staff",
            LastName = "Test",
            IsActive = true
        });
        context.TenantUsers.Add(new TenantUser(targetTenantId, userId, TenantUserRole.Staff));
        var current = new RefreshToken(userId, "current-session-token", DateTimeOffset.UtcNow.AddDays(7));
        var otherSession = new RefreshToken(userId, "other-device-token", DateTimeOffset.UtcNow.AddDays(7));
        context.RefreshTokens.AddRange(current, otherSession);
        await context.SaveChangesAsync();

        var handler = new SwitchTenantCommandHandler(
            new TestCurrentUserService(userId),
            context,
            new TestUserManager(context),
            new FakeJwtTokenService(),
            new FakeTenantAccessService());

        var result = await handler.Handle(
            new SwitchTenantCommand(targetTenantId) { CurrentRefreshToken = current.Token },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var tokens = await context.RefreshTokens.AsNoTracking().ToListAsync();
        var revoked = tokens.Single(x => x.Token == current.Token);
        Assert.True(revoked.IsRevoked);
        Assert.Equal(result.Value!.RefreshToken, revoked.ReplacedByToken);
        Assert.False(tokens.Single(x => x.Token == otherSession.Token).IsRevoked);
        Assert.False(tokens.Single(x => x.Token == result.Value.RefreshToken).IsRevoked);
    }

    private sealed class TestUserManager(ApplicationDbContext context) : UserManager<ApplicationUser>(
        new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
        Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
        new PasswordHasher<ApplicationUser>(),
        [],
        [],
        new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(),
        new ServiceCollection().BuildServiceProvider(),
        new NullLogger<UserManager<ApplicationUser>>());

    private sealed class FakeJwtTokenService : IJwtTokenService
    {
        public string GenerateAccessToken(ApplicationUser user, TenantTokenContext? tenantContext = null) => "access-token";
        public RefreshToken GenerateRefreshToken(Guid userId) => new(userId, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddDays(7));
        public Task<bool> ValidateRefreshTokenAsync(Guid userId, string refreshToken) => Task.FromResult(true);
        public Task RevokeRefreshTokenAsync(string refreshToken, string? replacedByToken = null) => Task.CompletedTask;
        public Task RevokeAllUserRefreshTokensAsync(Guid userId) => Task.CompletedTask;
        public Task<Guid?> GetUserIdFromRefreshTokenAsync(string refreshToken) => Task.FromResult<Guid?>(null);
        public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token) => null;
    }

    private sealed class FakeTenantAccessService : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Retail, []));
        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class TestCurrentUserService(Guid userId) : ICurrentUserService
    {
        public string? UserId { get; } = userId.ToString();
        public string? UserName => "staff@example.com";
        public string? Role => "STAFF";
        public string? TenantRole => "STAFF";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
    }
}
