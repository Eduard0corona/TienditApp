using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.Commands.TwoFactor;
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
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FoodParkDigital.Application.Tests;

public class TwoFactorLoginTests
{
    [Fact]
    public async Task Login_ShouldReturnOnlyChallenge_WhenUserHasTwoFactorEnabled()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();

        var result = await fixture.LoginAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value.RequiresTwoFactor);
        Assert.False(string.IsNullOrEmpty(result.Value.TwoFactorChallengeToken));
        Assert.Null(result.Value.AccessToken);
        Assert.Null(result.Value.RefreshToken);
        Assert.Null(result.Value.User);
        Assert.Empty(await fixture.Context.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Login2FA_ShouldIssueTokens_WhenAuthenticatorCodeIsValid()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var challenge = await fixture.GetChallengeAsync();
        var code = fixture.CurrentTotp();
        Assert.True(await fixture.UserManager.VerifyTwoFactorTokenAsync(
            fixture.User,
            fixture.UserManager.Options.Tokens.AuthenticatorTokenProvider,
            code));

        var result = await fixture.Login2FAAsync(challenge, code);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value.RequiresTwoFactor);
        Assert.False(string.IsNullOrEmpty(result.Value.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));
        var storedRefreshToken = Assert.Single(await fixture.Context.RefreshTokens.ToListAsync());
        Assert.Equal(fixture.User.Id, storedRefreshToken.UserId);
        // Only its hash is stored.
        Assert.Equal(RefreshToken.HashOf(result.Value.RefreshToken!), storedRefreshToken.TokenHash);
        Assert.Null(storedRefreshToken.LegacyValue);

        // Misma sesión que el login sin 2FA: tenant activo y permisos efectivos en el access token.
        var accessToken = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        Assert.Equal(fixture.Tenant.Id.ToString(), accessToken.Claims.Single(c => c.Type == "tenant_id").Value);
        var expectedPermissions = await new TenantAccessService(fixture.Context)
            .GetPermissionsAsync(fixture.User.Id, fixture.Tenant.Id, CancellationToken.None);
        Assert.NotEmpty(expectedPermissions);
        Assert.Equal(
            expectedPermissions.Order(StringComparer.OrdinalIgnoreCase),
            accessToken.Claims.Where(c => c.Type == "perm").Select(c => c.Value).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(fixture.Tenant.Id, result.Value.User!.TenantId);
    }

    [Fact]
    public async Task Login2FA_ShouldFailAndCountAttempt_WhenCodeIsInvalid()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var challenge = await fixture.GetChallengeAsync();

        var result = await fixture.Login2FAAsync(challenge, fixture.InvalidTotp());

        Assert.True(result.IsFailure);
        Assert.Equal(Login2FACommandHandler.InvalidCodeError, result.Error);
        Assert.Equal(1, await fixture.UserManager.GetAccessFailedCountAsync(fixture.User));
        Assert.Empty(await fixture.Context.RefreshTokens.ToListAsync());

        // Un error de captura no obliga a repetir la contraseña; el acierto reinicia el contador.
        var retry = await fixture.Login2FAAsync(challenge, fixture.CurrentTotp());

        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal(0, await fixture.UserManager.GetAccessFailedCountAsync(fixture.User));
    }

    [Fact]
    public async Task Login2FA_ShouldLockOutAndInvalidateChallenge_AfterMaxFailedAttempts()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var challenge = await fixture.GetChallengeAsync();
        var maxAttempts = fixture.UserManager.Options.Lockout.MaxFailedAccessAttempts;

        Result<AuthResponse>? lastAttempt = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
            lastAttempt = await fixture.Login2FAAsync(challenge, fixture.InvalidTotp());

        Assert.Equal(Login2FACommandHandler.LockedOutError, lastAttempt!.Error);
        Assert.True(await fixture.UserManager.IsLockedOutAsync(fixture.User));

        var withValidCode = await fixture.Login2FAAsync(challenge, fixture.CurrentTotp());

        Assert.Equal(Login2FACommandHandler.InvalidChallengeError, withValidCode.Error);
        Assert.Empty(await fixture.Context.RefreshTokens.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-jwt")]
    [InlineData("a.b.c")]
    public async Task Login2FA_ShouldFail_WithoutChallengeToken(string? challengeToken)
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();

        var result = await fixture.Login2FAAsync(challengeToken!, fixture.CurrentTotp());

        Assert.Equal(Login2FACommandHandler.InvalidChallengeError, result.Error);
        Assert.Equal(0, await fixture.UserManager.GetAccessFailedCountAsync(fixture.User));
        Assert.Empty(await fixture.Context.RefreshTokens.ToListAsync());
    }

    [Fact]
    public void Login2FAValidator_ShouldRejectLegacyEmailAndCodePayload()
    {
        var command = JsonSerializer.Deserialize<Login2FACommand>(
            """{ "email": "owner@2fa.test", "code": "123456" }""",
            JsonSerializerOptions.Web)!;

        var validation = new Login2FACommandValidator().Validate(command);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.PropertyName == nameof(Login2FACommand.TwoFactorChallengeToken));
    }

    [Fact]
    public async Task Login2FA_ShouldRejectAccessTokensAndChallengesSignedWithTheAccessTokenKey()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var accessToken = fixture.JwtTokens.GenerateAccessToken(fixture.User, Guid.NewGuid());
        var forgedChallenge = fixture.ResignWithAccessTokenKey(await fixture.GetChallengeAsync());

        var withAccessToken = await fixture.Login2FAAsync(accessToken, fixture.CurrentTotp());
        var withForgedChallenge = await fixture.Login2FAAsync(forgedChallenge, fixture.CurrentTotp());

        Assert.Equal(Login2FACommandHandler.InvalidChallengeError, withAccessToken.Error);
        Assert.Equal(Login2FACommandHandler.InvalidChallengeError, withForgedChallenge.Error);
        Assert.Empty(await fixture.Context.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task ChallengeToken_ShouldNotValidateWithAccessTokenParameters()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var challenge = await fixture.GetChallengeAsync();

        // The parameters the API validates access tokens with.
        var accessTokenParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = fixture.Settings.Issuer,
            ValidAudience = fixture.Settings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(fixture.Settings.Secret)),
            ClockSkew = TimeSpan.Zero
        };

        Assert.ThrowsAny<SecurityTokenException>(
            () => new JwtSecurityTokenHandler().ValidateToken(challenge, accessTokenParameters, out _));
    }

    [Fact]
    public async Task Login2FA_ShouldRejectReusedChallenge()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var challenge = await fixture.GetChallengeAsync();

        var first = await fixture.Login2FAAsync(challenge, fixture.CurrentTotp());
        var replay = await fixture.Login2FAAsync(challenge, fixture.CurrentTotp());

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(Login2FACommandHandler.InvalidChallengeError, replay.Error);
        Assert.Single(await fixture.Context.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Login_ShouldReplacePreviousChallenge()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var previous = await fixture.GetChallengeAsync();
        var current = await fixture.GetChallengeAsync();

        var withPrevious = await fixture.Login2FAAsync(previous, fixture.CurrentTotp());
        var withCurrent = await fixture.Login2FAAsync(current, fixture.CurrentTotp());

        Assert.Equal(Login2FACommandHandler.InvalidChallengeError, withPrevious.Error);
        Assert.True(withCurrent.IsSuccess, withCurrent.Error);
    }

    [Fact]
    public async Task Login2FA_ShouldFail_WhenSecurityStampChangesAfterPasswordStep()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var challenge = await fixture.GetChallengeAsync();

        await fixture.UserManager.UpdateSecurityStampAsync(fixture.User);
        var result = await fixture.Login2FAAsync(challenge, fixture.CurrentTotp());

        Assert.Equal(Login2FACommandHandler.InvalidChallengeError, result.Error);
        Assert.Empty(await fixture.Context.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Login2FA_ShouldFail_WhenChallengeExpired()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var challenge = await fixture.GetChallengeAsync();

        fixture.Clock.UtcNow += TwoFactorLoginChallengeService.Lifetime + TimeSpan.FromSeconds(1);
        var result = await fixture.Login2FAAsync(challenge, fixture.CurrentTotp());

        Assert.Equal(Login2FACommandHandler.InvalidChallengeError, result.Error);
        Assert.Empty(await fixture.Context.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Login2FA_ShouldAcceptEachRecoveryCodeOnlyOnce()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync();
        var recoveryCodes = (await fixture.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(fixture.User, 2))!.ToArray();

        var first = await fixture.Login2FAAsync(await fixture.GetChallengeAsync(), recoveryCodes[0], useRecoveryCode: true);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(1, await fixture.UserManager.CountRecoveryCodesAsync(fixture.User));

        var reused = await fixture.Login2FAAsync(await fixture.GetChallengeAsync(), recoveryCodes[0], useRecoveryCode: true);

        Assert.Equal(Login2FACommandHandler.InvalidCodeError, reused.Error);
        Assert.Equal(1, await fixture.UserManager.GetAccessFailedCountAsync(fixture.User));

        var other = await fixture.Login2FAAsync(
            await fixture.GetChallengeAsync(),
            recoveryCodes[1].ToLowerInvariant(),
            useRecoveryCode: true);

        Assert.True(other.IsSuccess, other.Error);
        Assert.Equal(0, await fixture.UserManager.CountRecoveryCodesAsync(fixture.User));
        Assert.Equal(2, await fixture.Context.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Login2FA_ShouldNotReactivateDisabledTenantMembership()
    {
        await using var fixture = await TwoFactorFixture.CreateAsync(TenantUserStatus.Disabled);
        var challenge = await fixture.GetChallengeAsync();

        var result = await fixture.Login2FAAsync(challenge, fixture.CurrentTotp());

        Assert.True(result.IsSuccess, result.Error);
        var accessToken = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        Assert.DoesNotContain(accessToken.Claims, c => c.Type == "tenant_id");
        var membership = Assert.Single(await fixture.Context.TenantUsers
            .IgnoreQueryFilters()
            .Where(x => x.UserId == fixture.User.Id)
            .ToListAsync());
        Assert.Equal(TenantUserStatus.Disabled, membership.Status);
    }

    private sealed class TwoFactorFixture : IAsyncDisposable
    {
        private const string Email = "owner@2fa.test";
        private const string Password = "Passw0rd";

        private LoginCommandHandler _loginHandler = null!;
        private Login2FACommandHandler _login2FAHandler = null!;

        public ApplicationDbContext Context { get; private init; } = null!;
        public UserManager<ApplicationUser> UserManager { get; private init; } = null!;
        public TestClock Clock { get; private init; } = null!;
        public JwtSettings Settings { get; private init; } = null!;
        public JwtTokenService JwtTokens { get; private init; } = null!;
        public ApplicationUser User { get; private set; } = null!;
        public Tenant Tenant { get; private set; } = null!;
        private string AuthenticatorKey { get; set; } = null!;

        public static async Task<TwoFactorFixture> CreateAsync(TenantUserStatus membershipStatus = TenantUserStatus.Active)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());

            var identityOptions = new IdentityOptions();
            identityOptions.User.RequireUniqueEmail = true;
            var userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
                Options.Create(identityOptions),
                new PasswordHasher<ApplicationUser>(),
                [new UserValidator<ApplicationUser>()],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                NullLogger<UserManager<ApplicationUser>>.Instance);
            userManager.RegisterTokenProvider(
                TokenOptions.DefaultAuthenticatorProvider,
                new AuthenticatorTokenProvider<ApplicationUser>());

            var settings = new JwtSettings
            {
                Secret = "two-factor-tests-signing-key-not-a-real-secret-0123456789",
                Issuer = "FoodParkDigital.Tests",
                Audience = "FoodParkDigital.Tests"
            };
            var clock = new TestClock(DateTimeOffset.UtcNow);
            var jwtTokens = new JwtTokenService(Options.Create(settings), clock);
            var challenges = new TwoFactorLoginChallengeService(userManager, clock, Options.Create(settings));
            var sessions = new AuthSessionIssuer(context, jwtTokens, new TenantAccessService(context));

            var fixture = new TwoFactorFixture
            {
                Context = context,
                UserManager = userManager,
                Clock = clock,
                Settings = settings,
                JwtTokens = jwtTokens,
                _loginHandler = new LoginCommandHandler(
                    userManager,
                    new PasswordSignInService(userManager),
                    context,
                    challenges,
                    sessions),
                _login2FAHandler = new Login2FACommandHandler(userManager, challenges, sessions)
            };

            await fixture.SeedUserWithTwoFactorAsync(membershipStatus);
            return fixture;
        }

        public Task<Result<AuthResponse>> LoginAsync()
            => _loginHandler.Handle(new LoginCommand { Email = Email, Password = Password }, CancellationToken.None);

        public async Task<string> GetChallengeAsync()
        {
            var login = await LoginAsync();
            Assert.True(login.IsSuccess, login.Error);
            Assert.True(login.Value.RequiresTwoFactor);
            return login.Value.TwoFactorChallengeToken!;
        }

        public Task<Result<AuthResponse>> Login2FAAsync(string challengeToken, string code, bool useRecoveryCode = false)
            => _login2FAHandler.Handle(
                new Login2FACommand
                {
                    TwoFactorChallengeToken = challengeToken,
                    Code = code,
                    UseRecoveryCode = useRecoveryCode
                },
                CancellationToken.None);

        public string CurrentTotp() => Totp.Compute(AuthenticatorKey, DateTimeOffset.UtcNow);

        public string InvalidTotp()
        {
            // Identity acepta códigos de ±2 pasos de 30 s; se excluye una ventana más amplia.
            var accepted = Enumerable.Range(-3, 7)
                .Select(step => Totp.Compute(AuthenticatorKey, DateTimeOffset.UtcNow.AddSeconds(30 * step)))
                .ToHashSet();

            var candidate = 0;
            while (accepted.Contains(candidate.ToString("D6", CultureInfo.InvariantCulture)))
                candidate++;

            return candidate.ToString("D6", CultureInfo.InvariantCulture);
        }

        public string ResignWithAccessTokenKey(string challengeToken)
        {
            var challenge = new JwtSecurityTokenHandler().ReadJwtToken(challengeToken);
            var forged = new JwtSecurityToken(
                issuer: challenge.Issuer,
                audience: challenge.Audiences.Single(),
                claims: challenge.Claims.Where(c => c.Type is "sub" or "jti" or "token_use"),
                expires: challenge.ValidTo,
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Settings.Secret)),
                    SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(forged);
        }

        private async Task SeedUserWithTwoFactorAsync(TenantUserStatus membershipStatus)
        {
            Tenant = new Tenant("Tenant 2FA", "tenant-2fa", BusinessType.Food, "tenant@2fa.test");
            Context.Tenants.Add(Tenant);
            await Context.SaveChangesAsync();

            User = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = Email,
                Email = Email,
                EmailConfirmed = true,
                FirstName = "Owner",
                LastName = "TwoFactor",
                Role = UserRole.TenantAdmin,
                DefaultTenantId = Tenant.Id,
                IsActive = true
            };
            var created = await UserManager.CreateAsync(User, Password);
            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));

            Context.TenantUsers.Add(new TenantUser(Tenant.Id, User.Id, TenantUserRole.Admin, membershipStatus));
            await Context.SaveChangesAsync();

            // Mismo alta que Enable2FACommand + Verify2FACommand.
            await UserManager.ResetAuthenticatorKeyAsync(User);
            await UserManager.SetTwoFactorEnabledAsync(User, true);
            AuthenticatorKey = (await UserManager.GetAuthenticatorKeyAsync(User))!;
        }

        public async ValueTask DisposeAsync()
        {
            UserManager.Dispose();
            await Context.DisposeAsync();
        }
    }

    /// <summary>Equivalente a SignInManager.CheckPasswordSignInAsync sin HttpContext.</summary>
    private sealed class PasswordSignInService : ISignInService
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public PasswordSignInService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<SignInResultDto> CheckPasswordSignInAsync(Guid userId, string password, bool lockoutOnFailure)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user is null)
                return new SignInResultDto(false);

            if (await _userManager.IsLockedOutAsync(user))
                return new SignInResultDto(false, IsLockedOut: true);

            return new SignInResultDto(await _userManager.CheckPasswordAsync(user, password));
        }
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public TestClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
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
