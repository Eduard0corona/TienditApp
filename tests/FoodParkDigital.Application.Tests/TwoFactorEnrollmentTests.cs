using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Commands.TwoFactor;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
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
using QRCoder;

namespace FoodParkDigital.Application.Tests;

public class TwoFactorEnrollmentTests
{
    private const string Issuer = "FoodParkDigital";
    private const string Email = "owner@2fa.test";

    [Fact]
    public async Task Enable2FA_ShouldEmbedIdentityAuthenticatorKeyInQrCode_SoScannedCodesAreAccepted()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);
        var user = await CreateUserAsync(userManager);
        var twoFactorService = new TwoFactorService(Options.Create(new JwtSettings { Issuer = Issuer }));

        var enabled = await new Enable2FACommandHandler(userManager, twoFactorService)
            .Handle(new Enable2FACommand(user.Id), CancellationToken.None);

        Assert.True(enabled.IsSuccess, enabled.Error);
        // AuthenticatorTokenProvider decodifica esta clave como Base32 al verificar códigos.
        var authenticatorKey = (await userManager.GetAuthenticatorKeyAsync(user))!;

        // El QR debe llevar la clave de Identity como secret, no el Base32 de su texto UTF-8.
        var otpauthUri = $"otpauth://totp/{Issuer}:{Uri.EscapeDataString(Email)}?secret={authenticatorKey}&issuer={Issuer}";
        Assert.True(
            RenderQrCode(otpauthUri) == enabled.Value.QrCodeUrl,
            "El QR no contiene la clave de Identity como secret del otpauth.");
        Assert.Equal(authenticatorKey, enabled.Value.ManualEntryKey.Replace(" ", string.Empty));

        // Código que genera una app autenticadora con el secret del QR.
        var code = Totp.Compute(authenticatorKey, DateTimeOffset.UtcNow);
        Assert.True(await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            code));

        var verified = await new Verify2FACommandHandler(userManager)
            .Handle(new Verify2FACommand(user.Id, code), CancellationToken.None);

        Assert.True(verified.IsSuccess, verified.Error);
        Assert.True(await userManager.GetTwoFactorEnabledAsync(user));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext context)
    {
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
        // El proveedor que AddDefaultTokenProviders registra para Options.Tokens.AuthenticatorTokenProvider.
        userManager.RegisterTokenProvider(
            TokenOptions.DefaultAuthenticatorProvider,
            new AuthenticatorTokenProvider<ApplicationUser>());

        return userManager;
    }

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> userManager)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = Email,
            Email = Email,
            EmailConfirmed = true,
            FirstName = "Owner",
            LastName = "TwoFactor"
        };
        var created = await userManager.CreateAsync(user);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));

        return user;
    }

    // GoogleAuthenticator 3.2.0 dibuja el QR con QRCoder (ECC Q) y TwoFactorService pide 3 px por módulo.
    // QRCoder llega como dependencia de GoogleAuthenticator, así que es el mismo renderizador:
    // si los PNG coinciden, el QR codifica exactamente ese otpauth. Si cambia la versión de la
    // librería o el tamaño del QR, esta prueba falla a propósito y hay que actualizar el formato.
    private static string RenderQrCode(string otpauthUri)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(otpauthUri, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return $"data:image/png;base64,{Convert.ToBase64String(png.GetGraphic(3))}";
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
