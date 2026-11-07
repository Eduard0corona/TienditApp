using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// After a request online is reminded (Fase 29U, ADR 0075), the reminder of each member who gets the emails of requests: one
/// message per member in the outbox (<see cref="AppointmentAnswerReminderEmailEvent"/>), saved with the record that this ran,
/// as the email of its arrival (Fase 29N). None once it was answered or cancelled, or its limit passed.
/// </summary>
public sealed class AppointmentAnswerDueSoonEmailsHandler : IOutboxEventHandler<AppointmentAnswerDueSoonEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IOutbox _outbox;
    private readonly IDateTimeProvider _clock;

    public AppointmentAnswerDueSoonEmailsHandler(
        IApplicationDbContext context, ITenantAccessService access, IOutbox outbox, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task HandleAsync(
        AppointmentAnswerDueSoonEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        var request = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.Id == domainEvent.AppointmentId && a.TenantId == domainEvent.TenantId && !a.FromTeam)
            .Select(a => new { a.Status, a.CreatedAt, a.StartAt, a.RequestedStartAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (request is null || !RequestEmail.ReminderWorth(request.Status, request.CreatedAt, request.RequestedStartAt ?? request.StartAt, _clock.UtcNow))
            return;

        foreach (var recipient in await RequestEmailRecipients.ListAsync(_context, _access, domainEvent.TenantId, null, cancellationToken))
            _outbox.Add(new AppointmentAnswerReminderEmailEvent(domainEvent.AppointmentId, recipient.UserId), domainEvent.TenantId);
    }
}
