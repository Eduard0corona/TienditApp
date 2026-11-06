using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// After its customer cancels an appointment (Fase 29O, ADR 0069), the email of each member who gets the agenda's notices: one
/// message per member in the outbox (<see cref="AppointmentCancellationEmailEvent"/>), saved with the record that this ran, so
/// each is sent and retried on its own. None for an appointment of the team, one cancelled over a day ago or whose time came.
/// </summary>
public sealed class AppointmentCancelledEmailsHandler : IOutboxEventHandler<AppointmentCancelledByCustomerEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IOutbox _outbox;
    private readonly IDateTimeProvider _clock;

    public AppointmentCancelledEmailsHandler(
        IApplicationDbContext context, ITenantAccessService access, IOutbox outbox, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task HandleAsync(
        AppointmentCancelledByCustomerEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        if (domainEvent.FromTeam)
            return;

        var appointment = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.Id == domainEvent.AppointmentId && a.TenantId == domainEvent.TenantId)
            .Select(a => new { a.Status, a.CancelledByCustomerAt, a.StartAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (appointment is null
            || !CancellationEmail.Worth(appointment.Status, appointment.CancelledByCustomerAt, appointment.StartAt, _clock.UtcNow))
            return;

        foreach (var recipient in await RequestEmailRecipients.ListAsync(_context, _access, domainEvent.TenantId, null, cancellationToken))
            _outbox.Add(
                new AppointmentCancellationEmailEvent(domainEvent.AppointmentId, recipient.UserId, domainEvent.WasRequest),
                domainEvent.TenantId);
    }
}
