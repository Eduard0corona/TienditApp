using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
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
    public async Task Switch_ShouldGoOnWithTheSameSession_ReplacingItsToken_AndLeaveOtherSessionsAlone()
    {
        await using var fixture = await Fixture.CreateAsync();
        var current = fixture.Jwt.GenerateRefreshToken(fixture.UserId, Guid.NewGuid());
        var otherDevice = fixture.Jwt.GenerateRefreshToken(fixture.UserId, Guid.NewGuid());
        fixture.Context.RefreshTokens.AddRange(current.Token, otherDevice.Token);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.SwitchAsync(current.Value);

        Assert.True(result.IsSuccess, result.Error);
        var tokens = await fixture.Context.RefreshTokens.AsNoTracking().ToListAsync();
        var replaced = tokens.Single(x => x.Id == current.Token.Id);
        var issued = tokens.Single(x => x.TokenHash == RefreshToken.HashOf(result.Value!.RefreshToken!));
        Assert.Equal(RefreshTokenRevocationReason.Rotated, replaced.RevocationReason);
        Assert.True(issued.IsActive);
        Assert.Equal(current.Token.SessionId, issued.SessionId);
        Assert.Equal(current.Token.SessionId, fixture.Jwt.LastAccessTokenSession);
        Assert.False(tokens.Single(x => x.Id == otherDevice.Token.Id).IsRevoked);
    }

    [Fact]
    public async Task Switch_WithoutAnActiveTokenOfTheAccount_ShouldStartANewSession_AndRevokeNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var someoneElses = fixture.Jwt.GenerateRefreshToken(Guid.NewGuid(), Guid.NewGuid());
        fixture.Context.RefreshTokens.Add(someoneElses.Token);
        await fixture.Context.SaveChangesAsync();

        var withAnotherAccountsToken = await fixture.SwitchAsync(someoneElses.Value);
        var withoutCookie = await fixture.SwitchAsync(null);

        Assert.True(withAnotherAccountsToken.IsSuccess, withAnotherAccountsToken.Error);
        Assert.True(withoutCookie.IsSuccess, withoutCookie.Error);
        var tokens = await fixture.Context.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.False(tokens.Single(x => x.Id == someoneElses.Token.Id).IsRevoked);
        var issued = tokens.Where(x => x.UserId == fixture.UserId).ToList();
        Assert.Equal(2, issued.Count);
        Assert.All(issued, token => Assert.NotEqual(someoneElses.Token.SessionId, token.SessionId));
        Assert.NotEqual(issued[0].SessionId, issued[1].SessionId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Guid _targetTenantId = Guid.NewGuid();

        private Fixture(ApplicationDbContext context) => Context = context;

        public ApplicationDbContext Context { get; }
        public Guid UserId { get; } = Guid.NewGuid();
        public FakeJwtTokenService Jwt { get; } = new();

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var fixture = new Fixture(new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator()));
            fixture.Context.Users.Add(new ApplicationUser
            {
                Id = fixture.UserId,
                Email = "staff@example.com",
                UserName = "staff@example.com",
                FirstName = "Staff",
                LastName = "Test",
                IsActive = true
            });
            fixture.Context.TenantUsers.Add(new TenantUser(fixture._targetTenantId, fixture.UserId, TenantUserRole.Staff));
            await fixture.Context.SaveChangesAsync();
            return fixture;
        }

        public Task<Result<AuthResponse>> SwitchAsync(string? currentRefreshToken) =>
            new SwitchTenantCommandHandler(
                    new TestCurrentUserService(UserId),
                    Context,
                    new TestUserManager(Context),
                    Jwt,
                    new FakeTenantAccessService(),
                    new FixedClock(DateTimeOffset.UtcNow))
                .Handle(new SwitchTenantCommand(_targetTenantId) { CurrentRefreshToken = currentRefreshToken }, CancellationToken.None);

        public ValueTask DisposeAsync() => Context.DisposeAsync();
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
        public Guid? LastAccessTokenSession { get; private set; }

        public string GenerateAccessToken(ApplicationUser user, Guid sessionId, TenantTokenContext? tenantContext = null)
        {
            LastAccessTokenSession = sessionId;
            return "access-token";
        }

        public IssuedRefreshToken GenerateRefreshToken(Guid userId, Guid sessionId)
        {
            var value = Guid.NewGuid().ToString("N");
            return new IssuedRefreshToken(
                new RefreshToken(userId, sessionId, value, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)),
                value);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
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
