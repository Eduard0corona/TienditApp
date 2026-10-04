namespace FoodParkDigital.Infrastructure.BackgroundJobs;

internal static class BackOff
{
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The poll interval, doubled for each consecutive failed pass (an unreachable database) up to a minute,
    /// so an outage does not flood the logs.
    /// </summary>
    public static TimeSpan Delay(TimeSpan pollInterval, int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
            return pollInterval;

        var factor = Math.Pow(2, Math.Min(consecutiveFailures, 10));
        var delay = TimeSpan.FromTicks((long)Math.Min(pollInterval.Ticks * factor, Max.Ticks));
        return delay < pollInterval ? pollInterval : delay;
    }
}
