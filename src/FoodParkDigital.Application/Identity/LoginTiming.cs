using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity;

/// <summary>
/// A login refused before looking at the password (no account with that email, an inactive or a locked one) still
/// checks it against a hash, never the account's: it takes about as long as a wrong password, so the time of the
/// answer tells nothing about the account.
/// </summary>
internal static class LoginTiming
{
    private static readonly ApplicationUser Placeholder = new();
    private static string? _hash;

    public static void CheckAgainstNoAccount(UserManager<ApplicationUser> userManager, string password)
    {
        var hasher = userManager.PasswordHasher;
        var hash = _hash ??= hasher.HashPassword(Placeholder, Guid.NewGuid().ToString("N"));
        hasher.VerifyHashedPassword(Placeholder, hash, password);
    }
}
