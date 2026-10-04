namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>Settings of the background workers (section <c>BackgroundJobs</c>).</summary>
public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>
    /// When false the workers are not started: events and webhook deliveries still accumulate in their
    /// tables and are processed once a process with the workers enabled runs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public QueueOptions Outbox { get; set; } = new();
    public QueueOptions Inbox { get; set; } = new();

    public sealed class QueueOptions
    {
        /// <summary>How often other instances look for work; a commit in this process wakes it at once.</summary>
        public int PollIntervalSeconds { get; set; } = 5;

        public int BatchSize { get; set; } = 20;

        /// <summary>After this many failed attempts the item is set aside (dead-lettered) and logged.</summary>
        public int MaxAttempts { get; set; } = 8;

        /// <summary>Processed items are deleted after this many days.</summary>
        public int RetentionDays { get; set; } = 7;

        /// <summary>A claimed item that is not finished within this time can be claimed by another instance.</summary>
        public int LeaseSeconds { get; set; } = 120;
    }
}
