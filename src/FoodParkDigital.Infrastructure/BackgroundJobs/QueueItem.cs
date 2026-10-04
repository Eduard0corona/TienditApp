namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// Retry and lease state shared by the outbox and the webhook inbox. An item is pending while it is neither
/// processed nor dead-lettered; a worker claims it by renewing <see cref="LockToken"/> (a concurrency token),
/// so two instances cannot process the same item at once.
/// </summary>
public abstract class QueueItem
{
    public Guid Id { get; protected set; }
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset NextAttemptAt { get; protected set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset? DeadLetteredAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public Guid LockToken { get; private set; } = Guid.NewGuid();
    public string? LastError { get; private set; }

    public void Claim(DateTimeOffset now, TimeSpan lease)
    {
        LockedUntil = now.Add(lease);
        LockToken = Guid.NewGuid();
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LockedUntil = null;
        LastError = null;
    }

    /// <returns>True when the item ran out of attempts and was dead-lettered.</returns>
    public bool MarkFailed(DateTimeOffset now, string error, int maxAttempts)
    {
        Attempts++;
        LastError = error;
        LockedUntil = null;

        if (Attempts >= maxAttempts)
        {
            DeadLetteredAt = now;
            return true;
        }

        NextAttemptAt = now.Add(RetrySchedule.DelayAfter(Attempts));
        return false;
    }
}
