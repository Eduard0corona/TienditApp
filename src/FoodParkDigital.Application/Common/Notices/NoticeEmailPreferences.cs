using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Notices;

/// <summary>
/// Where each person chooses their own notices by email, in the business they are in (Fases 29N and 29O, ADR 0068 and 0069):
/// their membership there, and the email of their account those notices reach.
/// </summary>
internal static class NoticeEmailPreferences
{
    public const string NotAMemberMessage = "Estos avisos son para el equipo del negocio.";

    /// <summary>The person's membership in the current business; none for whoever is not on its team (the platform's admin).</summary>
    public static Task<TenantUser?> MemberAsync(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        bool tracked,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.UserId, out var userId))
            return Task.FromResult<TenantUser?>(null);

        var members = tracked ? context.TenantUsers : context.TenantUsers.AsNoTracking();
        return members.FirstOrDefaultAsync(m => m.TenantId == tenantService.TenantId && m.UserId == userId, cancellationToken);
    }

    /// <summary>The email of the member's account, and whether it is confirmed: until it is, nothing is sent to it.</summary>
    public static async Task<(string? Email, bool Confirmed)> AccountAsync(
        IApplicationDbContext context, TenantUser member, CancellationToken cancellationToken)
    {
        var account = await context.Users
            .AsNoTracking()
            .Where(u => u.Id == member.UserId)
            .Select(u => new { u.Email, u.EmailConfirmed })
            .FirstAsync(cancellationToken);
        return (account.Email, account.EmailConfirmed);
    }
}
