using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Reviews.Notices;

/// <summary>
/// Who gets an email each time a customer writes a new review of the business (Fase 31K, ADR 0091): its active members who
/// chose to get them (or own it and have not chosen), whose account is active with its email confirmed, and who may answer
/// the reviews now (its owners and admins, in a business that is not suspended), as the API would let them.
/// </summary>
internal static class ReviewEmailRecipients
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
            m => m.ReviewEmails == true || (m.ReviewEmails == null && m.Role == TenantUserRole.Owner),
            MayAnswerReviews,
            cancellationToken);

    /// <summary>What the API asks of whoever reads and answers the reviews: an owner or admin, active in a business not suspended.</summary>
    internal static bool MayAnswerReviews(TenantAccessSnapshot access)
        => access is { HasActiveAccess: true, TenantSuspended: false, IsManager: true };
}
