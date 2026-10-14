using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity;

/// <summary>
/// A sign-up with an email that already has an account creates nothing, but still hashes the password, as creating
/// the account would: the answer takes about as long, so its time tells nothing about the email.
/// </summary>
internal static class SignUpTiming
{
    public static void HashAsIfCreating(UserManager<ApplicationUser> userManager, ApplicationUser candidate, string password)
        => userManager.PasswordHasher.HashPassword(candidate, password);
}
