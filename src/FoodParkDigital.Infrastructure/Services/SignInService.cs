using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Infrastructure.Services;

public class SignInService : ISignInService
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public SignInService(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    public async Task<SignInResultDto> CheckPasswordSignInAsync(
        Guid userId,
        string password,
        bool lockoutOnFailure)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return new SignInResultDto(false);

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure);

        return new SignInResultDto(
            result.Succeeded,
            result.IsLockedOut,
            result.IsNotAllowed,
            result.RequiresTwoFactor);
    }

    public async Task<SignInResultDto> TwoFactorAuthenticatorSignInAsync(
        string code,
        bool isPersistent,
        bool rememberClient)
    {
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent, rememberClient);

        return new SignInResultDto(
            result.Succeeded,
            result.IsLockedOut,
            result.IsNotAllowed,
            result.RequiresTwoFactor);
    }

    public async Task<SignInResultDto> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

        return new SignInResultDto(
            result.Succeeded,
            result.IsLockedOut,
            result.IsNotAllowed,
            result.RequiresTwoFactor);
    }
}
