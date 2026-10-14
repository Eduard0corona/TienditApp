using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Events;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees;

/// <summary>
/// An invitation is a link, valid for a week, where the person chooses their own password: the admin never invents
/// one. It goes by email when that is configured and the admin can share a link as well, by WhatsApp. Accepting it
/// activates every pending invitation of the account.
/// </summary>
public static class EmployeeInvitations
{
    /// <summary>Its own Identity token provider (registered in Infrastructure): an invitation never resets a password.</summary>
    public const string TokenProvider = "Invitation";
    public const string TokenPurpose = "AcceptInvitation";

    public const string AlreadyAcceptedMessage = "La invitación ya se usó: entra con tu correo y contraseña.";
    public const string InvalidMessage = "La invitación no es válida o ya expiró: pide que te la reenvíen.";

    public static string PathFor(Guid userId, string token)
        => $"/aceptar-invitacion?userId={userId}&token={Uri.EscapeDataString(token)}";

    public static async Task<string> CreatePathAsync(UserManager<ApplicationUser> userManager, ApplicationUser user)
        => PathFor(user.Id, await userManager.GenerateUserTokenAsync(user, TokenProvider, TokenPurpose));

    public static Task<bool> IsValidAsync(UserManager<ApplicationUser> userManager, ApplicationUser user, string token)
        => userManager.VerifyUserTokenAsync(user, TokenProvider, TokenPurpose, token);

    /// <summary>
    /// Creates the link the admin shares and asks for the invitation email, which goes out from the outbox with a link
    /// of its own (<c>AccountEmailRequestedHandler</c>) once the caller saves: with the membership, so neither is
    /// stored without the other.
    /// </summary>
    /// <returns>The invitation link, relative to the app.</returns>
    public static async Task<string> IssueAsync(
        UserManager<ApplicationUser> userManager,
        IOutbox outbox,
        ApplicationUser user,
        Guid tenantId)
    {
        var path = await CreatePathAsync(userManager, user);
        if (!string.IsNullOrWhiteSpace(user.Email))
            outbox.Add(new AccountEmailRequestedEvent(AccountEmailPurpose.TeamInvitation, UserId: user.Id), tenantId);
        return path;
    }

    /// <summary>The names of the businesses that invited the account and wait for it.</summary>
    public static Task<List<string>> PendingBusinessesAsync(IApplicationDbContext context, Guid userId, CancellationToken cancellationToken)
        // IgnoreQueryFilters: the person has no business selected yet; every pending invitation of the account is theirs.
        => context.TenantUsers
            .IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.Status == TenantUserStatus.Invited)
            .Join(context.Tenants, x => x.TenantId, t => t.Id, (x, t) => t.Name)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

    /// <summary>Activates every pending invitation of the account (the caller saves).</summary>
    public static async Task ActivatePendingAsync(IApplicationDbContext context, Guid userId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: accepting happens before any business is selected, for all the invitations of the account.
        var pending = await context.TenantUsers
            .IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.Status == TenantUserStatus.Invited)
            .ToListAsync(cancellationToken);
        foreach (var membership in pending)
            membership.SetStatus(TenantUserStatus.Active);
    }
}
