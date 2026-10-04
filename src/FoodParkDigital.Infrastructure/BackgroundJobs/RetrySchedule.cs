namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>Waits between attempts: 10 s, 30 s, 2 min, 10 min, 30 min, 1 h, 3 h and then 6 h.</summary>
public static class RetrySchedule
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6)
    ];

    /// <param name="failedAttempts">Attempts that already failed, counting the one that just did.</param>
    public static TimeSpan DelayAfter(int failedAttempts)
        => Delays[Math.Clamp(failedAttempts, 1, Delays.Length) - 1];
}
