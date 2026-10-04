using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity;

public static class UserManagerExtensions
{
    /// <summary>
    /// Checks <paramref name="password"/> against the configured Identity password validators
    /// (the same ones <see cref="UserManager{TUser}.CreateAsync(TUser, string)"/> applies) without
    /// persisting anything, so a registration can reject it before writing related data.
    /// </summary>
    public static async Task<IdentityResult> ValidatePasswordPolicyAsync(
        this UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string password)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, password);
            if (!result.Succeeded)
                errors.AddRange(result.Errors);
        }

        return errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed([.. errors]);
    }

    public static string ToErrorMessage(this IdentityResult result)
        => string.Join(", ", result.Errors.Select(e => e.Description));

    /// <summary>
    /// Checks a second-factor code for a user with 2FA: an authenticator (TOTP) code, or a recovery
    /// code when <paramref name="useRecoveryCode"/> is set. Does not count failed attempts; callers
    /// decide how to apply lockout.
    /// </summary>
    public static async Task<bool> VerifyTwoFactorCodeAsync(
        this UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string? code,
        bool useRecoveryCode)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        if (useRecoveryCode)
        {
            // Identity elimina el código al canjearlo, así que cada código de recuperación sirve una sola vez.
            var recoveryCode = code.Replace(" ", string.Empty).ToUpperInvariant();
            var redeemed = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode);
            return redeemed.Succeeded;
        }

        var verificationCode = code.Replace(" ", string.Empty).Replace("-", string.Empty);
        return await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            verificationCode);
    }
}
