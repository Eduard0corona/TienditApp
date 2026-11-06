using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Quotes.ReplyNotices;

/// <summary>
/// Who gets an email each time a customer answers a quote of the business from its link (Fase 29O, ADR 0069): its active
/// members who chose to get them (or own it and have not chosen), whose account is active with its email confirmed, and who
/// may work its quotes now (ManageOrders, in a business with services or appointments that is not suspended), as the API would
/// let them.
/// </summary>
internal static class QuoteReplyEmailRecipients
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
            m => m.QuoteReplyEmails == true || (m.QuoteReplyEmails == null && m.Role == TenantUserRole.Owner),
            MayWorkQuotes,
            cancellationToken);

    /// <summary>What the API asks of whoever opens, corrects or converts a quote (ManageOrders and services or appointments).</summary>
    internal static bool MayWorkQuotes(TenantAccessSnapshot access)
        => NoticeEmailRecipients.May(access, TenantPermission.ManageOrders, QuoteReplyEmail.Modules);
}
