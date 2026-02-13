using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Identity.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands.TwoFactor;

public record Disable2FACommand(Guid UserId) : IRequest<Result>;

public class Disable2FACommandHandler : IRequestHandler<Disable2FACommand, Result>
{
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

        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!result.Succeeded)
            return Result.Failure("Error al deshabilitar la autenticación de dos factores.");

        await _userManager.ResetAuthenticatorKeyAsync(user);

        return Result.Success();
    }
}
