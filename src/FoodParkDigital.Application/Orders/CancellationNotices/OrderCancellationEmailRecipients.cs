using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Orders.CancellationNotices;

/// <summary>
/// Who gets an email each time a customer cancels an online order of the business (Fase 30B, ADR 0082): its active members who
/// chose to get them (or own it and have not chosen), whose account is active with its email confirmed, and who may confirm
/// its orders now (ManageOrders, in a business that sells and is not suspended), as the API would let them.
/// </summary>
internal static class OrderCancellationEmailRecipients
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
            m => m.OrderCancellationEmails == true || (m.OrderCancellationEmails == null && m.Role == TenantUserRole.Owner),
            MayConfirmOrders,
            cancellationToken);

    /// <summary>What the API asks of whoever accepts or declines an order (ManageOrders) in a business that sells.</summary>
    internal static bool MayConfirmOrders(TenantAccessSnapshot access)
        => NoticeEmailRecipients.May(access, TenantPermission.ManageOrders, OrderCancellationEmail.Modules);
}
