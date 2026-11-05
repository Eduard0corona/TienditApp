using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// Sends one member the email of a request online (Fase 29N, ADR 0068), from the platform's provider. It checks again that the
/// request still waits and that the member still gets them, as either may have changed since it was queued; then nothing is
/// sent. A failure of the provider throws, and the outbox retries this member's email alone.
/// </summary>
public sealed class AppointmentRequestEmailHandler : IOutboxEventHandler<AppointmentRequestEmailEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;

    public AppointmentRequestEmailHandler(
        IApplicationDbContext context, ITenantAccessService access, IEmailService email, IAppLinks links, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _email = email;
        _links = links;
        _clock = clock;
    }

    public async Task HandleAsync(
        AppointmentRequestEmailEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId)
            return;

        var now = _clock.UtcNow;
        var request = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.Id == domainEvent.AppointmentId && a.TenantId == tenantId && !a.FromTeam)
            .Select(a => new
            {
                a.Status,
                a.CreatedAt,
                a.StartAt,
                a.RequestedStartAt,
                a.CustomerName,
                a.ServiceName,
                HomeVisit = a.Visit != null
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (request is null || !RequestEmail.Worth(request.Status, request.CreatedAt, request.StartAt, now))
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
        var email = RequestEmail.Compose(
            business.Name,
            request.CustomerName,
            request.ServiceName,
            request.HomeVisit,
            request.StartAt,
            ResponseTimes.AnswerBy(request.CreatedAt, request.RequestedStartAt ?? request.StartAt),
            now,
            business.TimeZone,
            _links.BaseUrl);

        await _email.SendEmailAsync(recipient.Email, email.Subject, email.HtmlBody);
    }
}
