using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// Degraded (still serving traffic) when the outbox or the webhook inbox set work aside in the last day,
/// or when pending work is older than it should be, which means the workers are stuck or not running.
/// </summary>
public sealed class BackgroundQueuesHealthCheck : IHealthCheck
{
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DeadLetterWindow = TimeSpan.FromDays(1);

    private readonly ApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public BackgroundQueuesHealthCheck(ApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var deadLetterSince = now - DeadLetterWindow;
        var stuckBefore = now - StuckAfter;

        var outboxDeadLettered = await _context.OutboxMessages.CountAsync(m => m.DeadLetteredAt > deadLetterSince, cancellationToken);
        var inboxDeadLettered = await _context.InboxMessages.CountAsync(m => m.DeadLetteredAt > deadLetterSince, cancellationToken);
        var outboxStuck = await _context.OutboxMessages.CountAsync(
            m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt < stuckBefore, cancellationToken);
        var inboxStuck = await _context.InboxMessages.CountAsync(
            m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt < stuckBefore, cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["outboxDeadLettered24h"] = outboxDeadLettered,
            ["inboxDeadLettered24h"] = inboxDeadLettered,
            ["outboxOverdue"] = outboxStuck,
            ["inboxOverdue"] = inboxStuck
        };

        return outboxDeadLettered + inboxDeadLettered + outboxStuck + inboxStuck == 0
            ? HealthCheckResult.Healthy(data: data)
            : HealthCheckResult.Degraded("Background work was set aside or is overdue.", data: data);
    }
}
