using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Rules;

/// <summary>
/// A customer profile's phone identifies its account only once the account proved by SMS that the
/// number is theirs: the user's confirmed phone must be the profile's phone, because
/// <c>PhoneNumberConfirmed</c> alone may refer to a different number set later on the user.
/// An unverified profile reserves nothing: orders placed with its number stay with a temporary
/// customer, anyone can register with it, and whoever confirms it by SMS takes it over.
/// </summary>
public static class CustomerPhoneOwnershipRules
{
    /// <summary>Country used to normalize the phones customers register and confirm.</summary>
    public const string DefaultCountry = "MX";

    /// <summary>The profile currently holding <paramref name="phoneNormalized"/>, if any.</summary>
    public static async Task<CustomerPhoneHolder?> FindHolderAsync(
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        string phoneNormalized,
        CancellationToken cancellationToken)
    {
        var holder = await context.CustomerProfiles
            .Where(profile => profile.PhoneNormalized == phoneNormalized)
            .Join(
                context.Users,
                profile => profile.UserId,
                user => user.Id,
                (profile, user) => new { profile.UserId, user.PhoneNumber, user.PhoneNumberConfirmed })
            .FirstOrDefaultAsync(cancellationToken);

        if (holder is null)
            return null;

        var isVerified = holder.PhoneNumberConfirmed
            && IsSamePhone(phoneNormalizationService, holder.PhoneNumber, phoneNormalized);

        return new CustomerPhoneHolder(holder.UserId, isVerified);
    }

    /// <summary>
    /// The account just proved <paramref name="phoneNumber"/> is theirs, so their profile takes it and orders placed
    /// with it are linked to them from now on. The number is taken from a profile whose account never confirmed it,
    /// never from an account that confirmed it first.
    /// </summary>
    public static async Task AssignConfirmedPhoneAsync(
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        ApplicationUser user,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var phoneNormalized = phoneNormalizationService.NormalizeToE164(phoneNumber, DefaultCountry);
        var holder = await FindHolderAsync(context, phoneNormalizationService, phoneNormalized, cancellationToken);

        if (holder is not null && holder.UserId != user.Id)
        {
            if (holder.IsVerified)
                return;

            // The phone is unique across profiles, so the unverified one must release it first.
            // Its account is kept; it just stops holding a number it never proved.
            var unverifiedProfile = await context.CustomerProfiles
                .FirstOrDefaultAsync(x => x.UserId == holder.UserId, cancellationToken);
            if (unverifiedProfile is not null)
            {
                context.CustomerProfiles.Remove(unverifiedProfile);
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        var profile = await context.CustomerProfiles
            .FirstOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);

        if (profile is null)
        {
            context.CustomerProfiles.Add(new CustomerProfile(
                user.Id,
                phoneNormalized,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.FirstName,
                user.LastName));
        }
        else if (profile.PhoneNormalized != phoneNormalized)
        {
            // The account confirmed a different number, so its previous one stops identifying it.
            profile.UpdateProfile(phoneNormalized, profile.DisplayName, profile.FirstName, profile.LastName);
        }
        else
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsSamePhone(
        IPhoneNormalizationService phoneNormalizationService,
        string? rawPhone,
        string phoneNormalized)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return false;

        try
        {
            return phoneNormalizationService.NormalizeToE164(rawPhone, DefaultCountry) == phoneNormalized;
        }
        catch (ArgumentException)
        {
            // A stored phone that no longer normalizes cannot prove ownership of any number.
            return false;
        }
    }
}

/// <param name="UserId">Account whose profile holds the number.</param>
/// <param name="IsVerified">Whether that account confirmed this number by SMS.</param>
public sealed record CustomerPhoneHolder(Guid UserId, bool IsVerified);
