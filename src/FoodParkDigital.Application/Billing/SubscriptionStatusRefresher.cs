using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Billing.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Billing;

/// <summary>
/// Moves subscriptions along their dates (trial or paid period over → past due → suspended after the grace days)
/// and carries the result to the business. Idempotent: a subscription already in the status its dates give is left
/// alone, so running it twice, or on two instances, changes nothing more.
/// </summary>
public static class SubscriptionStatusRefresher
{
    /// <summary>Businesses whose stored status no longer matches their dates (platform-wide, read only).</summary>
    public static async Task<List<Guid>> FindStaleTenantsAsync(IApplicationDbContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var candidates = await context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status != SubscriptionStatus.Cancelled && !x.SuspendedByPlatform)
            .ToListAsync(cancellationToken);
        return candidates.Where(x => x.Evaluate(now) != x.Status).Select(x => x.TenantId).ToList();
    }

    /// <summary>Refreshes the business in scope.</summary>
    /// <returns>True when its status changed.</returns>
    public static async Task<bool> RefreshAsync(IApplicationDbContext context, Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var subscription = await context.TenantSubscriptions.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (subscription is null)
            return false;

        var previous = subscription.Status;
        if (!subscription.Refresh(now))
            return false;

        var tenant = await context.Tenants.FirstAsync(x => x.Id == tenantId, cancellationToken);
        tenant.SetStatus(SubscriptionRules.TenantStatusFor(subscription.Status));
        context.AuditLogs.Add(SubscriptionRules.Audit(tenantId, "SubscriptionStatusChanged", "billing", new
        {
            Previous = previous.ToString(),
            Current = subscription.Status.ToString(),
            subscription.CoveredUntil
        }));
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
