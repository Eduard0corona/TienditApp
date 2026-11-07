using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// Who gets the agenda's emails of a business: one with each request online (Fase 29N, ADR 0068), since the Fase 29O (ADR
/// 0069) one with each appointment its customer cancels and, since the Fase 29U (ADR 0075), a reminder of each request still
/// waiting an hour before its limit. Its active members who chose to get them (or own it and have
/// not chosen), whose account is active with its email confirmed, and who may answer the requests now (the agenda's
/// permission, in a business with appointments that is not suspended), as the API would let them.
/// </summary>
internal static class RequestEmailRecipients
{
    public static Task<IReadOnlyList<NoticeEmailRecipient>> ListAsync(
        IApplicationDbContext context,
        ITenantAccessService access,
        Guid tenantId,
        Guid? onlyUserId,
        CancellationToken cancellationToken)
        => NoticeEmailRecipients.ListAsync(
            context,
            access,
            tenantId,
            onlyUserId,
            m => m.RequestEmails == true || (m.RequestEmails == null && m.Role == TenantUserRole.Owner),
            MayAnswer,
            cancellationToken);

    /// <summary>What the API asks of whoever accepts or declines a request (ManageAppointments and the module of appointments).</summary>
    internal static bool MayAnswer(TenantAccessSnapshot access)
        => NoticeEmailRecipients.May(access, TenantPermission.ManageAppointments, TenantCapability.Appointments);
}
