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
    public MessagingOptions Messaging { get; set; } = new();

    /// <summary>Campaigns, appointment reminders and the queue of outgoing WhatsApp messages.</summary>
    public sealed class MessagingOptions
    {
        public int PollIntervalSeconds { get; set; } = 10;

        public int BatchSize { get; set; } = 20;

        /// <summary>Attempts of a message with transient failures before it is marked as failed.</summary>
        public int MaxAttempts { get; set; } = 6;

        public int LeaseSeconds { get; set; } = 120;

        /// <summary>Pause after each send, so a campaign does not burst the provider (about 10 per second).</summary>
        public int SendIntervalMilliseconds { get; set; } = 100;

        /// <summary>How often the appointments due for a reminder are looked up.</summary>
        public int ReminderScanMinutes { get; set; } = 5;
    }

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
