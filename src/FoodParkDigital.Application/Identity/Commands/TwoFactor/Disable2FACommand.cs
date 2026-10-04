using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands.TwoFactor;

/// <summary>
/// Quitar el 2FA exige el segundo factor vigente: un código de la app autenticadora o, si se perdió
/// el dispositivo, un código de recuperación. Una sesión robada no basta para desactivarlo.
/// </summary>
public record Disable2FACommand(Guid UserId, string Code, bool UseRecoveryCode = false) : IRequest<Result>;

public class Disable2FACommandValidator : AbstractValidator<Disable2FACommand>
{
    public Disable2FACommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("El código de autenticación es obligatorio.")
            .MaximumLength(64)
            .WithMessage("El código de autenticación no es válido.");
    }
}

public class Disable2FACommandHandler : IRequestHandler<Disable2FACommand, Result>
{
    public const string NotEnabledError = "La autenticación de dos factores no está habilitada.";
    public const string InvalidCodeError = "Código de autenticación inválido.";
    public const string LockedOutError = "La cuenta está bloqueada. Intente más tarde.";

    private readonly UserManager<ApplicationUser> _userManager;

    public Disable2FACommandHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Result> Handle(Disable2FACommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Result.Failure("Usuario no encontrado.");

        // Sin 2FA no hay nada que quitar, y la clave de un alta sin confirmar no se toca.
        if (!await _userManager.GetTwoFactorEnabledAsync(user))
            return Result.Failure(NotEnabledError);

        if (await _userManager.IsLockedOutAsync(user))
            return Result.Failure(LockedOutError);

        if (!await _userManager.VerifyTwoFactorCodeAsync(user, request.Code, request.UseRecoveryCode))
        {
            // Mismo contador y bloqueo que el segundo paso del login: limita adivinar códigos.
            var accessFailed = await _userManager.AccessFailedAsync(user);
            if (accessFailed.Succeeded && await _userManager.IsLockedOutAsync(user))
                return Result.Failure(LockedOutError);

            return Result.Failure(InvalidCodeError);
        }

        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!result.Succeeded)
            return Result.Failure("Error al deshabilitar la autenticación de dos factores.");

        await _userManager.ResetAuthenticatorKeyAsync(user);
        await _userManager.ResetAccessFailedCountAsync(user);

        return Result.Success();
    }
}
