using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// Reminds a business of the requests online still waiting an hour before their limit to be answered in time (Fase 29U, ADR
/// 0075). It runs in the business's own scope (the worker finds which businesses have any); each request reminded raises
/// its event, which the outbox turns into the emails, saved by the caller.
/// </summary>
public sealed class AnswerReminderPlanner
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public AnswerReminderPlanner(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <summary>
    /// The requests online that might be due: still waiting, from a customer, not reminded, and arrived within the last day
    /// (an older one is past its limit). Whether each is due now is the request's own rule.
    /// </summary>
    internal static IQueryable<Domain.Appointments.Entities.Appointment> Waiting(
        IQueryable<Domain.Appointments.Entities.Appointment> appointments, DateTimeOffset now)
    {
        var since = now.AddHours(-ResponseTimes.LimitHours[^1]);
        return appointments.Where(a => a.Channel == AppointmentChannel.Online
            && a.Status == AppointmentStatus.Requested
            && !a.FromTeam
            && a.AnswerReminderSentAt == null
            && a.CreatedAt >= since);
    }

    /// <returns>The requests reminded now, not saved yet.</returns>
    public async Task<int> RemindDueAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var reminded = 0;
        foreach (var request in await Waiting(_context.Appointments, now).ToListAsync(cancellationToken))
        {
            if (request.RemindToAnswer(now))
                reminded++;
        }
        return reminded;
    }
}
