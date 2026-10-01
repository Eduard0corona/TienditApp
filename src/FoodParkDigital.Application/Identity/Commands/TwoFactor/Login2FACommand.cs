using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands.TwoFactor;

public record Login2FACommand : IRequest<Result<AuthResponse>>
{
    /// <summary>Desafío emitido por <see cref="LoginCommand"/> tras validar la contraseña.</summary>
    public string TwoFactorChallengeToken { get; init; } = null!;
    public string Code { get; init; } = null!;
    public bool UseRecoveryCode { get; init; }
}

public class Login2FACommandValidator : AbstractValidator<Login2FACommand>
{
    public Login2FACommandValidator()
    {
        RuleFor(x => x.TwoFactorChallengeToken).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
    }
}

public class Login2FACommandHandler : IRequestHandler<Login2FACommand, Result<AuthResponse>>
{
    public const string InvalidChallengeError = "La verificación expiró o no es válida. Inicia sesión de nuevo.";
    public const string InvalidCodeError = "Código de autenticación inválido.";
    public const string LockedOutError = "La cuenta está bloqueada. Intente más tarde.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITwoFactorLoginChallengeService _twoFactorChallengeService;
    private readonly IAuthSessionIssuer _authSessionIssuer;

    public Login2FACommandHandler(
        UserManager<ApplicationUser> userManager,
        ITwoFactorLoginChallengeService twoFactorChallengeService,
        IAuthSessionIssuer authSessionIssuer)
    {
        _userManager = userManager;
        _twoFactorChallengeService = twoFactorChallengeService;
        _authSessionIssuer = authSessionIssuer;
    }

    public async Task<Result<AuthResponse>> Handle(Login2FACommand request, CancellationToken cancellationToken)
    {
        // El desafío prueba que la contraseña ya se validó: sin él no existe segundo paso.
        var user = await _twoFactorChallengeService.ValidateAsync(request.TwoFactorChallengeToken, cancellationToken);
        if (user is null || !user.IsActive || !await _userManager.GetTwoFactorEnabledAsync(user))
            return Result.Failure<AuthResponse>(InvalidChallengeError);

        if (await _userManager.IsLockedOutAsync(user))
            return Result.Failure<AuthResponse>(LockedOutError);

        if (!await VerifyCodeAsync(user, request.Code, request.UseRecoveryCode))
        {
            // Mismo contador y bloqueo de Identity que SignInManager aplica al segundo factor.
            var accessFailed = await _userManager.AccessFailedAsync(user);
            if (accessFailed.Succeeded && await _userManager.IsLockedOutAsync(user))
            {
                await _twoFactorChallengeService.ConsumeAsync(user, cancellationToken);
                return Result.Failure<AuthResponse>(LockedOutError);
            }

            return Result.Failure<AuthResponse>(InvalidCodeError);
        }

        // Consumir antes de emitir tokens: si otra petición ya usó este desafío, esta falla.
        if (!await _twoFactorChallengeService.ConsumeAsync(user, cancellationToken))
            return Result.Failure<AuthResponse>(InvalidChallengeError);

        var resetLockout = await _userManager.ResetAccessFailedCountAsync(user);
        if (!resetLockout.Succeeded)
            return Result.Failure<AuthResponse>(InvalidChallengeError);

        return Result.Success(await _authSessionIssuer.IssueAsync(user, cancellationToken));
    }

    private async Task<bool> VerifyCodeAsync(ApplicationUser user, string? code, bool useRecoveryCode)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        if (useRecoveryCode)
        {
            // Identity elimina el código al canjearlo, así que cada código de recuperación sirve una sola vez.
            var recoveryCode = code.Replace(" ", string.Empty).ToUpperInvariant();
            var redeemed = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode);
            return redeemed.Succeeded;
        }

        var verificationCode = code.Replace(" ", string.Empty).Replace("-", string.Empty);
        return await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            verificationCode);
    }
}
