using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Identity.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands.TwoFactor;

public record Enable2FACommand(Guid UserId) : IRequest<Result<Enable2FAResponse>>;

public record Enable2FAResponse(
    string SharedKey,
    string QrCodeUrl,
    string ManualEntryKey);

public class Enable2FACommandHandler : IRequestHandler<Enable2FACommand, Result<Enable2FAResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITwoFactorService _twoFactorService;

    public Enable2FACommandHandler(
        UserManager<ApplicationUser> userManager,
        ITwoFactorService twoFactorService)
    {
        _userManager = userManager;
        _twoFactorService = twoFactorService;
    }

    public async Task<Result<Enable2FAResponse>> Handle(Enable2FACommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Result.Failure<Enable2FAResponse>("Usuario no encontrado.");

        var is2FAEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        if (is2FAEnabled)
            return Result.Failure<Enable2FAResponse>("La autenticación de dos factores ya está habilitada.");

        await _userManager.ResetAuthenticatorKeyAsync(user);

        var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(authenticatorKey))
            return Result.Failure<Enable2FAResponse>("Error al generar la clave de autenticación.");

        var qrCodeInfo = _twoFactorService.GenerateQrCodeUri(user.Email!, authenticatorKey);

        return Result.Success(new Enable2FAResponse(
            authenticatorKey,
            qrCodeInfo.QrCodeUrl,
            qrCodeInfo.ManualEntryKey));
    }
}
