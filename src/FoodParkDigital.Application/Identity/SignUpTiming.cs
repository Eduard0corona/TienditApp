using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity;

/// <summary>
/// A sign-up with an email that already has an account creates nothing, but still hashes the password, as creating
/// the account would: the most expensive step is in both answers. Creating an account still writes more rows, a
/// difference of a few milliseconds that the time of the answer can show; probing a free email that way creates its
/// account (and sends it its activation email), so each email can be probed once.
/// </summary>
internal static class SignUpTiming
{
    public static void HashAsIfCreating(UserManager<ApplicationUser> userManager, ApplicationUser candidate, string password)
        => userManager.PasswordHasher.HashPassword(candidate, password);
}
