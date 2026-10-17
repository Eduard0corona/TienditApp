using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Services;

/// <summary>The customer account behind a request, read from the database rather than from the token's claims.</summary>
public static class CustomerAccounts
{
    /// <summary>
    /// The signed-in account when it is an active customer account; null without a session, or for the account of a
    /// business's team or the platform, which buy as guests.
    /// </summary>
    public static async Task<ApplicationUser?> FindSignedInAsync(
        IApplicationDbContext context, ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.UserId, out var userId))
            return null;

        var account = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return account is { IsActive: true, Role: UserRole.Customer } ? account : null;
    }
}
