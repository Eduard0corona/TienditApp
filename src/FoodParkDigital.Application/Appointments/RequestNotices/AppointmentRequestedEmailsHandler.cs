using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// After a request online arrives (Fase 29N, ADR 0068), the email of each member who gets them: one message per member in the
/// outbox (<see cref="AppointmentRequestEmailEvent"/>), saved with the record that this ran, so each is sent and retried on
/// its own. None for a request of the team, one already answered, one that arrived over a day ago or whose time came.
/// </summary>
public sealed class AppointmentRequestedEmailsHandler : IOutboxEventHandler<AppointmentRequestedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IOutbox _outbox;
    private readonly IDateTimeProvider _clock;

    public AppointmentRequestedEmailsHandler(
        IApplicationDbContext context, ITenantAccessService access, IOutbox outbox, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task HandleAsync(
        AppointmentRequestedEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        if (domainEvent.FromTeam)
            return;

        var request = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.Id == domainEvent.AppointmentId && a.TenantId == domainEvent.TenantId)
            .Select(a => new { a.Status, a.CreatedAt, a.StartAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (request is null || !RequestEmail.Worth(request.Status, request.CreatedAt, request.StartAt, _clock.UtcNow))
            return;

        foreach (var recipient in await RequestEmailRecipients.ListAsync(_context, _access, domainEvent.TenantId, null, cancellationToken))
            _outbox.Add(new AppointmentRequestEmailEvent(domainEvent.AppointmentId, recipient.UserId), domainEvent.TenantId);
    }
}
