using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// Who gets an email with each request online of a business (Fase 29N, ADR 0068): its active members who chose to get them
/// (or own it and have not chosen), whose account is active with its email confirmed, and who may answer the requests now
/// (the agenda's permission, in a business with appointments that is not suspended), as the API would let them.
/// </summary>
internal static class RequestEmailRecipients
{
    public static async Task<IReadOnlyList<RequestEmailRecipient>> ListAsync(
        IApplicationDbContext context,
        ITenantAccessService access,
        Guid tenantId,
        Guid? onlyUserId,
        CancellationToken cancellationToken)
    {
        var members = await context.TenantUsers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId
                && m.Status == TenantUserStatus.Active
                && (m.RequestEmails == true || (m.RequestEmails == null && m.Role == TenantUserRole.Owner))
                && (onlyUserId == null || m.UserId == onlyUserId))
            .Join(
                context.Users.Where(u => u.IsActive && u.EmailConfirmed && u.Email != null),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { u.Id, u.Email })
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken);

        var recipients = new List<RequestEmailRecipient>();
        foreach (var member in members)
        {
            if (MayAnswer(await access.GetAccessSnapshotAsync(member.Id, tenantId, cancellationToken)))
                recipients.Add(new RequestEmailRecipient(member.Id, member.Email!));
        }

        return recipients;
    }

    /// <summary>What the API asks of whoever accepts or declines a request (ManageAppointments and the module of appointments).</summary>
    internal static bool MayAnswer(TenantAccessSnapshot access)
        => access is { HasActiveAccess: true, TenantSuspended: false }
            && (access.NormalizedCapabilities & TenantCapability.Appointments) == TenantCapability.Appointments
            && (access.IsManager
                || access.Permissions.Contains(nameof(TenantPermission.ManageAppointments), StringComparer.OrdinalIgnoreCase));
}

internal sealed record RequestEmailRecipient(Guid UserId, string Email);
