using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Identity;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A session starts at sign-in and renews with a refresh token that rotates on every use. A replaced token that comes
/// back ends its session, signing out ends it and a new password ends them all; its access tokens count only while it
/// is open. Each step runs in its own context, as separate requests do.
/// </summary>
public class UserSessionTests
{
    private const string Password = "Secreto1";

    [Fact]
    public async Task Renewal_ShouldKeepTheSession_AndReplaceItsToken()
    {
        await using var fixture = await SessionFixture.CreateAsync();
        var signIn = await fixture.SignInAsync();

        var renewed = await fixture.RenewAsync(signIn.RefreshToken!);

        Assert.True(renewed.IsSuccess, renewed.Error);
        Assert.NotEqual(signIn.RefreshToken, renewed.Value!.RefreshToken);
        var tokens = await fixture.TokensAsync();
        var first = tokens.Single(t => t.TokenHash == RefreshToken.HashOf(signIn.RefreshToken!));
        var next = tokens.Single(t => t.TokenHash == RefreshToken.HashOf(renewed.Value.RefreshToken!));
        Assert.Equal(RefreshTokenRevocationReason.Rotated, first.RevocationReason);
        Assert.Null(next.RevokedAt);
        Assert.Equal(first.SessionId, next.SessionId);
        Assert.Equal(first.SessionId.ToString(), SessionOf(signIn.AccessToken));
        Assert.Equal(first.SessionId.ToString(), SessionOf(renewed.Value.AccessToken));
        Assert.True(await fixture.IsOpenAsync(renewed.Value.AccessToken));
    }

    [Fact]
    public async Task ReplacedTokenPresentedAgain_ShouldEndItsSession_AndOnlyThatOne()
    {
        await using var fixture = await SessionFixture.CreateAsync();
        var copied = await fixture.SignInAsync();
        var otherDevice = await fixture.SignInAsync();
        var renewed = await fixture.RenewAsync(copied.RefreshToken!);
        fixture.Clock.UtcNow += RefreshTokenCommandHandler.ConcurrentRenewalWindow + TimeSpan.FromSeconds(1);

        var reused = await fixture.RenewAsync(copied.RefreshToken!);

        Assert.Equal(RefreshTokenCommandHandler.InvalidSessionError, reused.Error);
        // Whoever holds the newest token of the copied session loses it too.
        Assert.False(await fixture.IsOpenAsync(renewed.Value!.AccessToken));
        Assert.Equal(RefreshTokenCommandHandler.InvalidSessionError, (await fixture.RenewAsync(renewed.Value.RefreshToken!)).Error);
        var newest = (await fixture.TokensAsync()).Single(t => t.TokenHash == RefreshToken.HashOf(renewed.Value.RefreshToken!));
        Assert.Equal(RefreshTokenRevocationReason.ReuseDetected, newest.RevocationReason);
        Assert.True(await fixture.IsOpenAsync(otherDevice.AccessToken));
        Assert.True((await fixture.RenewAsync(otherDevice.RefreshToken!)).IsSuccess);
    }

    [Fact]
    public async Task UnknownOrExpiredToken_ShouldNotRenew()
    {
        await using var fixture = await SessionFixture.CreateAsync();
        var signIn = await fixture.SignInAsync();

        var unknown = await fixture.RenewAsync("no-es-un-token");
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(8);
        var expired = await fixture.RenewAsync(signIn.RefreshToken!);

        Assert.Equal(RefreshTokenCommandHandler.InvalidSessionError, unknown.Error);
        Assert.Equal(RefreshTokenCommandHandler.InvalidSessionError, expired.Error);
        Assert.False(await fixture.IsOpenAsync(signIn.AccessToken));
    }

    [Fact]
    public async Task TwoRenewalsWithTheSameToken_ShouldReplaceItOnce()
    {
        await using var fixture = await SessionFixture.CreateAsync();
        var signIn = await fixture.SignInAsync();
        Result<AuthResponse>? first = null;
        // The second request has read the token when the first one saves its renewal.
        fixture.BeforeSave.Arm(
            context => context.ChangeTracker.Entries<RefreshToken>().Any(e => e.State == EntityState.Added),
            async _ => first = await fixture.RenewAsync(signIn.RefreshToken!));

        var second = await fixture.RenewAsync(signIn.RefreshToken!);

        Assert.True(fixture.BeforeSave.Fired);
        Assert.True(first!.IsSuccess, first.Error);
        Assert.Equal(RefreshTokenCommandHandler.ConcurrentRenewalError, second.Error);
        // The in-memory provider keeps the losing insert (it has no transactions); RefreshTokenSqlServerTests shows that
        // SQL Server keeps only the winner's token.
        var tokens = await fixture.TokensAsync();
        Assert.Equal(
            RefreshTokenRevocationReason.Rotated,
            tokens.Single(t => t.TokenHash == RefreshToken.HashOf(signIn.RefreshToken!)).RevocationReason);
        Assert.Null(tokens.Single(t => t.TokenHash == RefreshToken.HashOf(first.Value!.RefreshToken!)).RevokedAt);
        // Not taken for a copied token: the session goes on.
        Assert.True(await fixture.IsOpenAsync(first.Value.AccessToken));
    }

    [Fact]
    public async Task RenewalThatLostARaceByAMoment_ShouldTryAgain_WithoutEndingTheSession()
    {
        // A browser without the lock between tabs: the second renewal arrives just after the first one committed.
        await using var fixture = await SessionFixture.CreateAsync();
        var signIn = await fixture.SignInAsync();
        var renewed = await fixture.RenewAsync(signIn.RefreshToken!);
        fixture.Clock.UtcNow += TimeSpan.FromSeconds(3);

        var late = await fixture.RenewAsync(signIn.RefreshToken!);

        Assert.Equal(RefreshTokenCommandHandler.ConcurrentRenewalError, late.Error);
        Assert.True(await fixture.IsOpenAsync(renewed.Value!.AccessToken));
        Assert.True((await fixture.RenewAsync(renewed.Value.RefreshToken!)).IsSuccess);
        Assert.DoesNotContain(await fixture.TokensAsync(), t => t.RevocationReason == RefreshTokenRevocationReason.ReuseDetected);
    }

    [Fact]
    public async Task SigningOut_ShouldEndTheSession_AndItsAccessTokens()
    {
        await using var fixture = await SessionFixture.CreateAsync();
        var signIn = await fixture.SignInAsync();
        var renewed = await fixture.RenewAsync(signIn.RefreshToken!);

        var signedOut = await fixture.SignOutAsync(renewed.Value!.RefreshToken!);

        Assert.True(signedOut.IsSuccess);
        Assert.False(await fixture.IsOpenAsync(signIn.AccessToken));
        Assert.False(await fixture.IsOpenAsync(renewed.Value.AccessToken));
        Assert.Equal(RefreshTokenCommandHandler.InvalidSessionError, (await fixture.RenewAsync(renewed.Value.RefreshToken!)).Error);
        var newest = (await fixture.TokensAsync()).Single(t => t.TokenHash == RefreshToken.HashOf(renewed.Value.RefreshToken!));
        Assert.Equal(RefreshTokenRevocationReason.SignedOut, newest.RevocationReason);
    }

    [Fact]
    public async Task ManyLiveTokens_ShouldBeCheckedAtOnce_LikeOneByOne()
    {
        // The live hub connections are checked this way, all together.
        await using var fixture = await SessionFixture.CreateAsync();
        var kept = await fixture.SignInAsync();
        var signedOut = await fixture.SignInAsync();
        await fixture.SignOutAsync(signedOut.RefreshToken!);

        var open = await fixture.AreOpenAsync(kept.AccessToken, signedOut.AccessToken, kept.AccessToken);
        await fixture.ChangeSecurityStampAsync();
        var afterStampChange = await fixture.AreOpenAsync(kept.AccessToken, signedOut.AccessToken);

        Assert.Equal([true, false, true], open);
        Assert.Equal([false, false], afterStampChange);
    }

    [Fact]
    public async Task NewPassword_ShouldEndEverySession_AndTheirAccessTokensRightAway()
    {
        await using var fixture = await SessionFixture.CreateAsync();
        var thisDevice = await fixture.SignInAsync();
        var otherDevice = await fixture.SignInAsync();

        var changed = await fixture.ChangePasswordAsync("NuevaClave2");

        Assert.True(changed.IsSuccess, changed.Error);
        Assert.False(await fixture.IsOpenAsync(thisDevice.AccessToken));
        Assert.False(await fixture.IsOpenAsync(otherDevice.AccessToken));
        Assert.All(await fixture.TokensAsync(), token =>
            Assert.Equal(RefreshTokenRevocationReason.PasswordChanged, token.RevocationReason));
        Assert.False((await fixture.RenewAsync(otherDevice.RefreshToken!)).IsSuccess);
        Assert.True(await fixture.IsOpenAsync((await fixture.SignInAsync()).AccessToken));
    }

    [Fact]
    public async Task ChangedSecurityStamp_ShouldEndTheAccessTokens_WhileTheSessionRenews()
    {
        // Two-step verification or a confirmed phone change the stamp: the next request renews its access token.
        await using var fixture = await SessionFixture.CreateAsync();
        var signIn = await fixture.SignInAsync();

        await fixture.ChangeSecurityStampAsync();

        Assert.False(await fixture.IsOpenAsync(signIn.AccessToken));
        var renewed = await fixture.RenewAsync(signIn.RefreshToken!);
        Assert.True(renewed.IsSuccess, renewed.Error);
        Assert.True(await fixture.IsOpenAsync(renewed.Value!.AccessToken));
    }

    [Fact]
    public async Task DisabledAccount_ShouldEndItsAccessTokens()
    {
        await using var fixture = await SessionFixture.CreateAsync();
        var signIn = await fixture.SignInAsync();

        await fixture.DisableAccountAsync();

        Assert.False(await fixture.IsOpenAsync(signIn.AccessToken));
    }

    [Fact]
    public async Task AccessTokenWithoutSession_ShouldNotCount()
    {
        // Issued before sessions existed: the client renews it and gets one that has its session.
        await using var fixture = await SessionFixture.CreateAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, fixture.UserId.ToString())], "Bearer"));

        await using var context = fixture.NewContext();
        Assert.False(await new AccessTokenSessionValidator(context, fixture.Clock).IsOpenAsync(principal, CancellationToken.None));
    }

    [Fact]
    public async Task TokenStoredWithoutItsHash_ShouldNotRenew()
    {
        // Tokens are found only by their hash: the migration StopUsingPlainRefreshTokenValues hashed the plain values
        // left by older versions, and a row without a hash (left by a rollback) opens nothing.
        await using var fixture = await SessionFixture.CreateAsync();
        await fixture.AddUnhashedTokenAsync("valor-sin-hash");

        var renewed = await fixture.RenewAsync("valor-sin-hash");

        Assert.Equal(RefreshTokenCommandHandler.InvalidSessionError, renewed.Error);
    }

    private static string? SessionOf(string? accessToken) =>
        new JwtSecurityTokenHandler().ReadJwtToken(Assert.IsType<string>(accessToken)).Claims
            .SingleOrDefault(c => c.Type == SessionClaims.SessionId)?.Value;

    private sealed class SessionFixture : IAsyncDisposable
    {
        private const string Email = "dueno@sesiones.test";

        private static readonly JwtSettings Settings = new()
        {
            Secret = "session-tests-signing-key-not-a-real-secret-0123456789",
            Issuer = "FoodParkDigital.Tests",
            Audience = "FoodParkDigital.Tests",
            AccessTokenExpirationMinutes = 60,
            RefreshTokenExpirationDays = 7
        };

        private DbContextOptions<ApplicationDbContext> _options = null!;

        public BeforeSaveInterceptor BeforeSave { get; } = new();
        public SalesClock Clock { get; } = new() { UtcNow = DateTimeOffset.UtcNow };
        public Guid UserId { get; private set; }

        public static async Task<SessionFixture> CreateAsync()
        {
            var fixture = new SessionFixture();
            fixture._options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(fixture.BeforeSave)
                .Options;

            await using var context = fixture.NewContext();
            var tenant = new Tenant("Negocio de sesiones", "negocio-sesiones", BusinessType.Food, "negocio@sesiones.test");
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            using var userManager = NewUserManager(context);
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = Email,
                Email = Email,
                EmailConfirmed = true,
                FirstName = "Dueño",
                LastName = "Sesiones",
                Role = UserRole.TenantAdmin,
                DefaultTenantId = tenant.Id,
                IsActive = true
            };
            var created = await userManager.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
            context.TenantUsers.Add(new TenantUser(tenant.Id, user.Id, TenantUserRole.Owner));
            await context.SaveChangesAsync();

            fixture.UserId = user.Id;
            return fixture;
        }

        public ApplicationDbContext NewContext() => new(_options, new SalesTenantService(), new SalesNullMediator());

        public async Task<AuthResponse> SignInAsync()
        {
            await using var context = NewContext();
            var user = await context.Users.SingleAsync(u => u.Id == UserId);
            return await new AuthSessionIssuer(context, Jwt(), new TenantAccessService(context))
                .IssueAsync(user, CancellationToken.None);
        }

        public async Task<Result<AuthResponse>> RenewAsync(string refreshToken)
        {
            await using var context = NewContext();
            return await new RefreshTokenCommandHandler(
                    context, Jwt(), new TenantAccessService(context), Clock, NullLogger<RefreshTokenCommandHandler>.Instance)
                .Handle(new RefreshTokenCommand(refreshToken), CancellationToken.None);
        }

        public async Task<Result> SignOutAsync(string refreshToken)
        {
            await using var context = NewContext();
            return await new LogoutCommandHandler(context, Clock).Handle(new LogoutCommand(refreshToken), CancellationToken.None);
        }

        public async Task<Result> ChangePasswordAsync(string newPassword)
        {
            await using var context = NewContext();
            using var userManager = NewUserManager(context);
            return await new ChangePasswordCommandHandler(
                    userManager, new SalesCurrentUser { UserId = UserId.ToString() }, context, Clock)
                .Handle(new ChangePasswordCommand { CurrentPassword = Password, NewPassword = newPassword }, CancellationToken.None);
        }

        public async Task ChangeSecurityStampAsync()
        {
            await using var context = NewContext();
            using var userManager = NewUserManager(context);
            var user = await context.Users.SingleAsync(u => u.Id == UserId);
            Assert.True((await userManager.UpdateSecurityStampAsync(user)).Succeeded);
        }

        public async Task DisableAccountAsync()
        {
            await using var context = NewContext();
            var user = await context.Users.SingleAsync(u => u.Id == UserId);
            user.IsActive = false;
            await context.SaveChangesAsync();
        }

        /// <summary>Validated as the API does (signature, issuer, audience, lifetime), then the session.</summary>
        public async Task<bool> IsOpenAsync(string? accessToken)
        {
            await using var context = NewContext();
            return await new AccessTokenSessionValidator(context, Clock).IsOpenAsync(PrincipalOf(accessToken), CancellationToken.None);
        }

        public async Task<IReadOnlyList<bool>> AreOpenAsync(params string?[] accessTokens)
        {
            await using var context = NewContext();
            return await new AccessTokenSessionValidator(context, Clock)
                .AreOpenAsync(accessTokens.Select(PrincipalOf).ToList(), CancellationToken.None);
        }

        private static ClaimsPrincipal PrincipalOf(string? accessToken)
        {
            Assert.NotNull(accessToken);
            return new JwtSecurityTokenHandler().ValidateToken(accessToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Settings.Issuer,
                ValidAudience = Settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Settings.Secret)),
                ClockSkew = TimeSpan.Zero
            }, out _);
        }

        public async Task<List<RefreshToken>> TokensAsync()
        {
            await using var context = NewContext();
            return await context.RefreshTokens.AsNoTracking().ToListAsync();
        }

        /// <summary>An active token of a session of its own whose row lost its hash.</summary>
        public async Task<RefreshToken> AddUnhashedTokenAsync(string value)
        {
            var token = new RefreshToken(UserId, Guid.NewGuid(), value, Clock.UtcNow, Clock.UtcNow.AddDays(7));
            Set(token, nameof(RefreshToken.TokenHash), null);

            await using var context = NewContext();
            context.RefreshTokens.Add(token);
            await context.SaveChangesAsync();
            return token;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private JwtTokenService Jwt() => new(Options.Create(Settings), Clock);

        private static void Set(RefreshToken token, string property, object? value) =>
            typeof(RefreshToken).GetProperty(property)!.SetValue(token, value);

        private static UserManager<ApplicationUser> NewUserManager(ApplicationDbContext context)
        {
            var options = new IdentityOptions();
            options.User.RequireUniqueEmail = true;
            return new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
                Options.Create(options),
                new PasswordHasher<ApplicationUser>(),
                [new UserValidator<ApplicationUser>()],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                NullLogger<UserManager<ApplicationUser>>.Instance);
        }
    }
}
