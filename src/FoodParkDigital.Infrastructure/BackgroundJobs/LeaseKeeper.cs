using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// Keeps the lease of a batch of claimed items while a worker goes through them (Fase 28K, ADR 0056). Every third of the
/// lease it claims again, with a new <c>LockToken</c>, each item it has not finished, the waiting ones included: a handler
/// that takes longer than the lease (an email whose PDF and mail server take minutes) is not taken by another instance and
/// run twice.
/// <list type="bullet">
/// <item>An item another instance claimed anyway (this one stalled past its lease) is lost: <see cref="TokenFor"/> is
/// cancelled, and <see cref="CompleteAsync"/> does not complete it, so only the instance that holds it does.</item>
/// <item>Renewing and completing never overlap: both attach the item to their own context.</item>
/// </list>
/// </summary>
internal sealed class LeaseKeeper<TItem> : IAsyncDisposable
    where TItem : class
{
    private static readonly TimeSpan ShortestRenewal = TimeSpan.FromMilliseconds(100);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly TimeSpan _lease;
    private readonly Action<TItem, DateTimeOffset, TimeSpan> _claim;
    private readonly Func<TItem, Guid> _id;
    private readonly ILogger _logger;
    private readonly Dictionary<TItem, CancellationTokenSource> _held;
    private readonly HashSet<TItem> _lost = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping;
    private readonly Task _renewing;

    public LeaseKeeper(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        TimeSpan lease,
        IReadOnlyCollection<TItem> items,
        Action<TItem, DateTimeOffset, TimeSpan> claim,
        Func<TItem, Guid> id,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _lease = lease;
        _claim = claim;
        _id = id;
        _logger = logger;
        _held = items.ToDictionary<TItem, TItem, CancellationTokenSource>(
            item => item,
            _ => CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
            ReferenceEqualityComparer.Instance);
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _renewing = items.Count == 0 ? Task.CompletedTask : RenewWhileHeldAsync(_stopping.Token);
    }

    /// <summary>How often the leases are renewed: a third of the lease, so two renewals can fail before it runs out.</summary>
    public static TimeSpan RenewalInterval(TimeSpan lease)
    {
        var third = TimeSpan.FromTicks(lease.Ticks / 3);
        return third < ShortestRenewal ? ShortestRenewal : third;
    }

    /// <summary>The token the item's work runs with: cancelled when the worker stops or another instance takes it.</summary>
    public CancellationToken TokenFor(TItem item)
    {
        lock (_lost)
            return _held[item].Token;
    }

    /// <summary>Whether another instance claimed the item after this one.</summary>
    public bool IsLost(TItem item)
    {
        lock (_lost)
            return _lost.Contains(item);
    }

    /// <summary>Completes the item, unless it was lost; it is not renewed after this.</summary>
    /// <returns>False when the item was lost and <paramref name="complete"/> did not run.</returns>
    public async Task<bool> CompleteAsync(TItem item, Func<Task> complete, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsLost(item))
                return false;
            await complete();
            return true;
        }
        finally
        {
            Release(item);
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        try
        {
            await _renewing;
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var source in _held.Values)
            source.Dispose();
        _stopping.Dispose();
        _gate.Dispose();
    }

    private async Task RenewWhileHeldAsync(CancellationToken stoppingToken)
    {
        var interval = RenewalInterval(_lease);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, stoppingToken);
            try
            {
                await RenewAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // An unreachable database: the next renewal tries again, within the lease that is left.
                _logger.LogWarning(Services.LogRedaction.RedactDatabaseMessages(ex), "The leases of a batch could not be renewed");
            }
        }
    }

    private async Task RenewAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<TItem> pending;
            lock (_lost)
                pending = _held.Keys.Where(item => !_lost.Contains(item)).ToList();
            if (pending.Count == 0)
                return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _clock.UtcNow;
            foreach (var item in pending)
            {
                // The renewed LockToken is a concurrency token, as when it was claimed: an item another instance took
                // since then matches no row.
                context.Attach(item);
                _claim(item, now, _lease);
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    _logger.LogWarning("Item {ItemId} was claimed by another worker before this one finished it", _id(item));
                    lock (_lost)
                        _lost.Add(item);
                    await _held[item].CancelAsync();
                }
                finally
                {
                    context.Entry(item).State = EntityState.Detached;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Release(TItem item)
    {
        lock (_lost)
        {
            _held.Remove(item, out var source);
            source?.Dispose();
        }
    }
}
