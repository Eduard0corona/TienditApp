using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.PublicDirectory;

/// <summary>
/// How soon a business answers its requests online, outside Explorar (Fase 29K, ADR 0065): its store, its booking page
/// and its visit request say what its card in Explorar says, and its owner reads it in «Cómo te ven en TienditApp». It is
/// the entry of the business in the snapshot of Explorar (ADR 0064): the same rules, and no other read. Since Fase 29S
/// (ADR 0073) the entry also says how soon a trade quotes after a visit, for its visit request and its owner.
/// </summary>
internal static class PublicResponseTime
{
    /// <summary>
    /// How old a snapshot the customers' pages take: they read one business of it, and what it says changes slowly (90 days
    /// of requests). An older one is rebuilt by the page that finds it, as Explorar rebuilds it past
    /// <see cref="PublicDirectoryCache.Lifetime"/>. Its owner reads one as fresh as Explorar's.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The entry of the business in the snapshot, or null: Explorar does not list it, the snapshot could not read how soon
    /// its businesses answer, or another request is rebuilding it (<see cref="PublicDirectoryCache.GetWithoutWaitingAsync"/>).
    /// A snapshot that cannot be read never takes the page with it, and the page never waits in line for it.
    /// </summary>
    public static Task<PublicDirectorySnapshot.Entry?> EntryAsync(
        PublicDirectoryCache cache,
        IApplicationDbContext context,
        DateTimeOffset utcNow,
        Guid tenantId,
        ILogger logger,
        CancellationToken cancellationToken,
        TimeSpan? maxAge = null)
        => EntryAsync(
            cache, utcNow, tenantId, (now, ct) => PublicDirectorySnapshot.BuildAsync(context, now, ct, logger), maxAge ?? MaxAge,
            logger, cancellationToken);

    /// <summary>
    /// <see cref="EntryAsync(PublicDirectoryCache, IApplicationDbContext, DateTimeOffset, Guid, ILogger, CancellationToken, TimeSpan?)"/>
    /// with how the snapshot is built.
    /// </summary>
    internal static async Task<PublicDirectorySnapshot.Entry?> EntryAsync(
        PublicDirectoryCache cache,
        DateTimeOffset utcNow,
        Guid tenantId,
        Func<DateTimeOffset, CancellationToken, Task<PublicDirectorySnapshot>> build,
        TimeSpan maxAge,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await cache.GetWithoutWaitingAsync(utcNow, build, maxAge, cancellationToken);
            return snapshot is { ResponseTimesRead: true }
                ? snapshot.Entries.FirstOrDefault(entry => entry.TenantId == tenantId)
                : null;
        }
        // A customer who leaves cancels the page; the rebuild's own time limit, or any failure, only leaves it without it.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not read how soon the business answers: its page says nothing of it.");
            return null;
        }
    }

    /// <summary>
    /// What its card in Explorar says (<see cref="ResponseTimes.Shown"/>) while it still takes requests and appears in
    /// Explorar: a page reads both as they are now, and the snapshot may be older.
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
