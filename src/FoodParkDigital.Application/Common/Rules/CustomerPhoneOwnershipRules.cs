using FoodParkDigital.Application.Common.Interfaces;
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
