using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Identity.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands.TwoFactor;

public record Verify2FACommand(Guid UserId, string Code) : IRequest<Result<Verify2FAResponse>>;

public record Verify2FAResponse(IEnumerable<string> RecoveryCodes);

public class Verify2FACommandHandler : IRequestHandler<Verify2FACommand, Result<Verify2FAResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public Verify2FACommandHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Result<Verify2FAResponse>> Handle(Verify2FACommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Result.Failure<Verify2FAResponse>("Usuario no encontrado.");

        var verificationCode = request.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var is2FATokenValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            verificationCode);

        if (!is2FATokenValid)
            return Result.Failure<Verify2FAResponse>("Código de verificación inválido.");

        await _userManager.SetTwoFactorEnabledAsync(user, true);

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        return Result.Success(new Verify2FAResponse(recoveryCodes!));
    }
}
