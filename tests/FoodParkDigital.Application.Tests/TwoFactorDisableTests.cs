using System.Globalization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands.TwoFactor;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Application.Tests;

public class TwoFactorDisableTests
{
    [Fact]
    public async Task Disable2FA_ShouldTurnOffTwoFactorAndDiscardKey_WhenAuthenticatorCodeIsValid()
    {
        await using var fixture = await TwoFactorDisableFixture.CreateAsync();

        var result = await fixture.DisableAsync(fixture.CurrentTotp());

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await fixture.UserManager.GetTwoFactorEnabledAsync(fixture.User));
        // La app autenticadora queda desvinculada: volver a activar el 2FA exige un QR nuevo.
        Assert.NotEqual(fixture.AuthenticatorKey, await fixture.UserManager.GetAuthenticatorKeyAsync(fixture.User));
    }

    [Fact]
    public async Task Disable2FA_ShouldKeepTwoFactorAndCountAttempt_WhenCodeIsInvalid()
    {
        await using var fixture = await TwoFactorDisableFixture.CreateAsync();

        var result = await fixture.DisableAsync(fixture.InvalidTotp());

        Assert.Equal(Disable2FACommandHandler.InvalidCodeError, result.Error);
        Assert.True(await fixture.UserManager.GetTwoFactorEnabledAsync(fixture.User));
        Assert.Equal(fixture.AuthenticatorKey, await fixture.UserManager.GetAuthenticatorKeyAsync(fixture.User));
        Assert.Equal(1, await fixture.UserManager.GetAccessFailedCountAsync(fixture.User));

        // Un error de captura no bloquea; el código correcto desactiva y reinicia el contador.
        var retry = await fixture.DisableAsync(fixture.CurrentTotp());

        Assert.True(retry.IsSuccess, retry.Error);
        Assert.False(await fixture.UserManager.GetTwoFactorEnabledAsync(fixture.User));
        Assert.Equal(0, await fixture.UserManager.GetAccessFailedCountAsync(fixture.User));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Disable2FA_ShouldRejectEmptyCode_AndKeepTwoFactor(string? code)
    {
        await using var fixture = await TwoFactorDisableFixture.CreateAsync();
        var command = new Disable2FACommand(fixture.User.Id, code!);

        var validation = new Disable2FACommandValidator().Validate(command);
        var result = await fixture.DisableAsync(code!);

        Assert.Contains(validation.Errors, e => e.PropertyName == nameof(Disable2FACommand.Code));
        Assert.Equal(Disable2FACommandHandler.InvalidCodeError, result.Error);
        Assert.True(await fixture.UserManager.GetTwoFactorEnabledAsync(fixture.User));
        Assert.Equal(fixture.AuthenticatorKey, await fixture.UserManager.GetAuthenticatorKeyAsync(fixture.User));
    }

    [Fact]
    public async Task Disable2FA_ShouldLockOutAndRejectValidCode_AfterMaxFailedAttempts()
    {
        await using var fixture = await TwoFactorDisableFixture.CreateAsync();
        var maxAttempts = fixture.UserManager.Options.Lockout.MaxFailedAccessAttempts;

        Result? lastAttempt = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
            lastAttempt = await fixture.DisableAsync(fixture.InvalidTotp());

        Assert.Equal(Disable2FACommandHandler.LockedOutError, lastAttempt!.Error);
        Assert.True(await fixture.UserManager.IsLockedOutAsync(fixture.User));

        // Mientras dure el bloqueo, ni un código válido desactiva el 2FA.
        var withValidCode = await fixture.DisableAsync(fixture.CurrentTotp());

        Assert.Equal(Disable2FACommandHandler.LockedOutError, withValidCode.Error);
        Assert.True(await fixture.UserManager.GetTwoFactorEnabledAsync(fixture.User));
    }

    [Fact]
    public async Task Disable2FA_ShouldAcceptRecoveryCodeOnce_WhenAuthenticatorIsLost()
    {
        await using var fixture = await TwoFactorDisableFixture.CreateAsync();
        var recoveryCodes = (await fixture.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(fixture.User, 2))!.ToArray();
        const string unknownRecoveryCode = "00000-00000";
        Assert.DoesNotContain(unknownRecoveryCode, recoveryCodes);

        var withUnknownCode = await fixture.DisableAsync(unknownRecoveryCode, useRecoveryCode: true);

        Assert.Equal(Disable2FACommandHandler.InvalidCodeError, withUnknownCode.Error);
        Assert.True(await fixture.UserManager.GetTwoFactorEnabledAsync(fixture.User));
        Assert.Equal(2, await fixture.UserManager.CountRecoveryCodesAsync(fixture.User));

        var result = await fixture.DisableAsync(recoveryCodes[0].ToLowerInvariant(), useRecoveryCode: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await fixture.UserManager.GetTwoFactorEnabledAsync(fixture.User));
        Assert.Equal(1, await fixture.UserManager.CountRecoveryCodesAsync(fixture.User));
        Assert.Equal(0, await fixture.UserManager.GetAccessFailedCountAsync(fixture.User));
    }

    [Fact]
    public async Task Disable2FA_ShouldChangeNothing_WhenTwoFactorIsNotEnabled()
    {
        // Alta iniciada con Enable2FA (ya hay clave) pero sin confirmar con Verify2FA.
        await using var fixture = await TwoFactorDisableFixture.CreateAsync(twoFactorEnabled: false);

        var result = await fixture.DisableAsync(fixture.CurrentTotp());

        Assert.Equal(Disable2FACommandHandler.NotEnabledError, result.Error);
        Assert.False(await fixture.UserManager.GetTwoFactorEnabledAsync(fixture.User));
        // El QR recién escaneado sigue sirviendo para confirmar el alta.
        Assert.Equal(fixture.AuthenticatorKey, await fixture.UserManager.GetAuthenticatorKeyAsync(fixture.User));
        Assert.Equal(0, await fixture.UserManager.GetAccessFailedCountAsync(fixture.User));
    }

    private sealed class TwoFactorDisableFixture : IAsyncDisposable
    {
        private const string Email = "owner@2fa.test";

        private ApplicationDbContext _context = null!;
        private Disable2FACommandHandler _handler = null!;

        public UserManager<ApplicationUser> UserManager { get; private init; } = null!;
        public ApplicationUser User { get; private set; } = null!;
        public string AuthenticatorKey { get; private set; } = null!;

        public static async Task<TwoFactorDisableFixture> CreateAsync(bool twoFactorEnabled = true)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());

            var userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
                Options.Create(new IdentityOptions()),
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

            var fixture = new TwoFactorDisableFixture
            {
                _context = context,
                _handler = new Disable2FACommandHandler(userManager),
                UserManager = userManager
            };

            await fixture.SeedUserAsync(twoFactorEnabled);
            return fixture;
        }

        public Task<Result> DisableAsync(string code, bool useRecoveryCode = false)
            => _handler.Handle(new Disable2FACommand(User.Id, code, useRecoveryCode), CancellationToken.None);

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

        private async Task SeedUserAsync(bool twoFactorEnabled)
        {
            User = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = Email,
                Email = Email,
                EmailConfirmed = true,
                FirstName = "Owner",
                LastName = "TwoFactor",
                Role = UserRole.TenantAdmin
            };
            var created = await UserManager.CreateAsync(User);
            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));

            // Mismo alta que Enable2FACommand; Verify2FACommand es quien enciende el 2FA.
            await UserManager.ResetAuthenticatorKeyAsync(User);
            if (twoFactorEnabled)
                await UserManager.SetTwoFactorEnabledAsync(User, true);
            AuthenticatorKey = (await UserManager.GetAuthenticatorKeyAsync(User))!;
        }

        public async ValueTask DisposeAsync()
        {
            UserManager.Dispose();
            await _context.DisposeAsync();
        }
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
