using System.Linq.Expressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Notices;

/// <summary>
/// Who gets an email that tells the business of something its customers did online (Fases 29N and 29O, ADR 0068 and 0069): its
/// active members who chose to get that notice (or own it and have not chosen), whose account is active with its email
/// confirmed, and who may act on it now, as the API would let them. Each notice says what its members chose and who may act.
/// </summary>
internal static class NoticeEmailRecipients
{
    public static async Task<IReadOnlyList<NoticeEmailRecipient>> ListAsync(
        IApplicationDbContext context,
        ITenantAccessService access,
        Guid tenantId,
        Guid? onlyUserId,
        Expression<Func<TenantUser, bool>> chose,
        Func<TenantAccessSnapshot, bool> mayAct,
        CancellationToken cancellationToken)
    {
        var members = await context.TenantUsers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId
                && m.Status == TenantUserStatus.Active
                && (onlyUserId == null || m.UserId == onlyUserId))
            .Where(chose)
            .Join(
                context.Users.Where(u => u.IsActive && u.EmailConfirmed && u.Email != null),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { u.Id, u.Email })
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken);

        var recipients = new List<NoticeEmailRecipient>();
        foreach (var member in members)
        {
            var snapshot = await access.GetAccessSnapshotAsync(member.Id, tenantId, cancellationToken);
            if (mayAct(snapshot))
                recipients.Add(new NoticeEmailRecipient(member.Id, member.Email!, snapshot.NormalizedCapabilities));
        }

        return recipients;
    }

    /// <summary>
    /// What a use case declaring <paramref name="permission"/> and <paramref name="anyOf"/> asks of whoever runs it: active in a
    /// business that is not suspended, with one of those modules, and the permission (owners and admins have every one).
    /// </summary>
    public static bool May(TenantAccessSnapshot access, TenantPermission permission, TenantCapability anyOf)
        => access is { HasActiveAccess: true, TenantSuspended: false }
            && (access.NormalizedCapabilities & anyOf) != 0
            && (access.IsManager || access.Permissions.Contains(permission.ToString(), StringComparer.OrdinalIgnoreCase));
}

/// <param name="Capabilities">The modules of the business, as the API reads them for this member now.</param>
internal sealed record NoticeEmailRecipient(Guid UserId, string Email, TenantCapability Capabilities);
