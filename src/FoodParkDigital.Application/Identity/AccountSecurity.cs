using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity;

/// <summary>
/// Limits per account, on top of the per-IP limit of the auth endpoints (which a request spread over many addresses
/// does not stop): one SMS code per minute, and wrong passwords or codes count towards the lockout of the account.
/// </summary>
public static class AccountSecurity
{
    public static readonly TimeSpan SmsCodeCooldown = TimeSpan.FromMinutes(1);

    public const string SmsCodeWaitMessage = "Espera un minuto para pedir otro código.";
    public const string LockedOutMessage = "Demasiados intentos: la cuenta quedó bloqueada unos minutos. Puedes recuperar tu contraseña.";
    public const string InvalidCodeMessage = "El código no es válido o ya expiró. Pide uno nuevo.";

    /// <summary>
    /// The one answer of a failed anonymous recovery by SMS: wrong or expired code, an account that does not exist or has
    /// no confirmed phone, and a locked one, which is not told apart (it would say the account exists). Everyone learns
    /// that the account locks after several attempts.
    /// </summary>
    public const string SmsRecoveryFailedMessage =
        "El código no es válido o ya venció. Pide otro; después de varios intentos la cuenta se bloquea unos minutos.";

    /// <summary>
    /// Reserves the next SMS code of <paramref name="user"/>. False while the last one is recent, or when another
    /// request reserved it at the same time (the concurrency stamp of the account decides).
    /// </summary>
    public static async Task<bool> TryReserveSmsCodeAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        DateTimeOffset now)
    {
        if (user.SmsCodeSentAt is { } last && now - last < SmsCodeCooldown)
            return false;

        user.SmsCodeSentAt = now;
        return (await userManager.UpdateAsync(user)).Succeeded;
    }

    /// <summary>
    /// Checks a code sent to the person (a token of <paramref name="tokenProvider"/>) and counts a wrong one towards
    /// the lockout, so six digits cannot be guessed by trying.
    /// </summary>
    public static async Task<CredentialCheck> VerifyCodeAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string tokenProvider,
        string purpose,
        string code)
    {
        if (await userManager.IsLockedOutAsync(user))
            return CredentialCheck.LockedOut;

        var normalized = NormalizeCode(code);
        if (normalized is not null && await userManager.VerifyUserTokenAsync(user, tokenProvider, purpose, normalized))
            return CredentialCheck.Valid;

        await userManager.AccessFailedAsync(user);
        return await userManager.IsLockedOutAsync(user) ? CredentialCheck.LockedOut : CredentialCheck.Invalid;
    }

    /// <summary>Checks the password of the account, counting a wrong one towards the lockout like the login does.</summary>
    public static async Task<CredentialCheck> VerifyPasswordAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string password)
    {
        if (await userManager.IsLockedOutAsync(user))
            return CredentialCheck.LockedOut;

        if (await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.ResetAccessFailedCountAsync(user);
            return CredentialCheck.Valid;
        }

        await userManager.AccessFailedAsync(user);
        return await userManager.IsLockedOutAsync(user) ? CredentialCheck.LockedOut : CredentialCheck.Invalid;
    }

    /// <summary>Recovering the password proves the account is theirs: it also lifts a lockout.</summary>
    public static async Task UnlockAsync(UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        await userManager.ResetAccessFailedCountAsync(user);
        if (await userManager.IsLockedOutAsync(user))
            await userManager.SetLockoutEndDateAsync(user, null);
    }

    /// <summary>The digits of a code; five digits are a six-digit code that lost its leading zero.</summary>
    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var digits = new string(code.Where(char.IsDigit).ToArray());
        if (digits.Length == 5)
            return digits.PadLeft(6, '0');

        return digits.Length == 6 ? digits : null;
    }

    public const string WrongPasswordMessage = "La contraseña no es correcta.";

    public static string? CodeMessageFor(CredentialCheck check) => check switch
    {
        CredentialCheck.Valid => null,
        CredentialCheck.LockedOut => LockedOutMessage,
        _ => InvalidCodeMessage
    };

    public static string? PasswordMessageFor(CredentialCheck check) => check switch
    {
        CredentialCheck.Valid => null,
        CredentialCheck.LockedOut => LockedOutMessage,
        _ => WrongPasswordMessage
    };
}

public enum CredentialCheck
{
    Valid,
    Invalid,
    LockedOut
}
