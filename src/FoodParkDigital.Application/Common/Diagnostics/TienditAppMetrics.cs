using System.Diagnostics.Metrics;

namespace FoodParkDigital.Application.Common.Diagnostics;

/// <summary>
/// Business and background-work metrics (meter <c>TienditApp</c>), exported with OpenTelemetry when an
/// exporter is configured. Tags carry the tenant id (an internal GUID, not personal data) so alerts can be
/// set per business.
/// </summary>
public static class TienditAppMetrics
{
    public const string MeterName = "TienditApp";
    public const string TenantTag = "tenant.id";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> OutboxMessages = Meter.CreateCounter<long>(
        "tienditapp.outbox.messages",
        description: "Outbox messages by outcome: processed, failed (will retry) or dead_lettered.");

    private static readonly Counter<long> InboxDeliveries = Meter.CreateCounter<long>(
        "tienditapp.inbox.deliveries",
        description: "Webhook deliveries by outcome: processed, failed (will retry) or dead_lettered.");

    private static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>(
        "tienditapp.messages.sent",
        description: "Messages handed to a provider (WhatsApp) by outcome: sent, transient_failure or rejected.");

    public static void RecordOutboxMessage(string outcome, string eventType, Guid? tenantId)
        => OutboxMessages.Add(1, Tags(("outcome", outcome), ("event.type", eventType), (TenantTag, tenantId?.ToString())));

    public static void RecordInboxDelivery(string outcome, string source)
        => InboxDeliveries.Add(1, Tags(("outcome", outcome), ("source", source)));

    public static void RecordMessageSent(string channel, string outcome, Guid tenantId)
        => MessagesSent.Add(1, Tags(("channel", channel), ("outcome", outcome), (TenantTag, tenantId.ToString())));

    private static ReadOnlySpan<KeyValuePair<string, object?>> Tags(params (string Key, string? Value)[] tags)
        => tags.Where(tag => tag.Value is not null)
            .Select(tag => new KeyValuePair<string, object?>(tag.Key, tag.Value))
            .ToArray();
}

public static class MetricOutcomes
{
    public const string Processed = "processed";
    public const string Failed = "failed";
    public const string DeadLettered = "dead_lettered";
    public const string Sent = "sent";
    public const string TransientFailure = "transient_failure";
    public const string Rejected = "rejected";
}
