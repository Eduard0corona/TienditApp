namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// Wakes a worker as soon as new work is committed in this process, so it does not wait for its next poll.
/// Other instances still find the work on their poll.
/// </summary>
public abstract class WorkSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        // At most one pending wake-up: several commits while the worker runs need a single extra pass.
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another thread released it first.
            }
        }
    }

    /// <returns>True when woken by <see cref="Notify"/>, false when the timeout elapsed.</returns>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => _signal.WaitAsync(timeout, cancellationToken);
}

public sealed class OutboxSignal : WorkSignal;

public sealed class InboxSignal : WorkSignal;
