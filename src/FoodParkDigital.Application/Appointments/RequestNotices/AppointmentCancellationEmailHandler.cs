using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// Sends one member the email of an appointment its customer cancelled (Fase 29O, ADR 0069), from the platform's provider. It
/// checks again that its time has not come and that the member still gets the agenda's notices and may attend it, as either may
/// have changed since it was queued; then nothing is sent. A failure of the provider throws, and the outbox retries this
/// member's email alone.
/// </summary>
public sealed class AppointmentCancellationEmailHandler : IOutboxEventHandler<AppointmentCancellationEmailEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;

    public AppointmentCancellationEmailHandler(
        IApplicationDbContext context, ITenantAccessService access, IEmailService email, IAppLinks links, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _email = email;
        _links = links;
        _clock = clock;
    }

    public async Task HandleAsync(
        AppointmentCancellationEmailEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId)
            return;

        var appointment = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.Id == domainEvent.AppointmentId && a.TenantId == tenantId && !a.FromTeam)
            .Select(a => new
            {
                a.Status,
                a.CancelledByCustomerAt,
                a.StartAt,
                a.CustomerName,
                a.ServiceName,
                HomeVisit = a.Visit != null
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (appointment is null
            || !CancellationEmail.Worth(appointment.Status, appointment.CancelledByCustomerAt, appointment.StartAt, _clock.UtcNow))
            return;

        var recipient = (await RequestEmailRecipients.ListAsync(_context, _access, tenantId, domainEvent.UserId, cancellationToken))
            .SingleOrDefault();
        if (recipient is null)
            return;

        var business = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Name, TimeZone = t.Settings != null ? t.Settings.TimeZone : null })
            .FirstAsync(cancellationToken);
        var email = CancellationEmail.Compose(
            business.Name,
            appointment.CustomerName,
            appointment.ServiceName,
            appointment.HomeVisit,
            domainEvent.WasRequest,
            appointment.StartAt,
            business.TimeZone,
            _links.BaseUrl);

        await _email.SendEmailAsync(recipient.Email, email.Subject, email.HtmlBody);
    }
}
