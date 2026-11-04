using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.PublicDirectory;

/// <summary>
/// How soon a business answers its requests online, outside Explorar (Fase 29K, ADR 0065): its store, its booking page
/// and its visit request say what its card in Explorar says, and its owner reads it in «Cómo te ven en TienditApp». It is
/// the entry of the business in the snapshot of Explorar (ADR 0064): the same rules, and no other read.
/// </summary>
internal static class PublicResponseTime
{
    /// <summary>
    /// How old a snapshot these pages take: they read one business of it, and what it says changes slowly (90 days of
    /// requests). An older one is rebuilt, as Explorar rebuilds it past <see cref="PublicDirectoryCache.Lifetime"/>.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The entry of the business in the snapshot, or null when Explorar does not list it. A snapshot that cannot be read
    /// never takes the page with it: the page says nothing of how soon it answers.
    /// </summary>
    public static async Task<PublicDirectorySnapshot.Entry?> EntryAsync(
        PublicDirectoryCache cache,
        IApplicationDbContext context,
        DateTimeOffset utcNow,
        Guid tenantId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await cache.GetAsync(
                utcNow,
                (now, ct) => PublicDirectorySnapshot.BuildAsync(context, now, ct, logger),
                cancellationToken,
                MaxAge);
            return snapshot.Entries.FirstOrDefault(entry => entry.TenantId == tenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not read how soon the business answers: its page says nothing of it.");
            return null;
        }
    }

    /// <summary>
    /// What its card in Explorar says (<see cref="ResponseTimes.Shown"/>), while it still takes requests: a page reads it
    /// as it is now, and the snapshot may be older.
    /// </summary>
    public static async Task<ResponseTime?> ShownAsync(
        PublicDirectoryCache cache,
        IApplicationDbContext context,
        DateTimeOffset utcNow,
        Guid tenantId,
        bool takesRequests,
        ILogger logger,
        CancellationToken cancellationToken)
        => takesRequests
            ? (await EntryAsync(cache, context, utcNow, tenantId, logger, cancellationToken))?.Business.ResponseTime
            : null;
}
