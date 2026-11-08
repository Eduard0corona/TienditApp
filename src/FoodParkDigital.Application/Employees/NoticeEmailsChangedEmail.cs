using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.Tenants.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees;

/// <summary>
/// The email that tells a person of the team that someone else changed their notices by email (Fase 30A, ADR 0081): who,
/// in which business, and what they get now, with the way to change them in Mi perfil.
/// </summary>
internal static class NoticeEmailsChangedEmail
{
    public static (string Subject, string HtmlBody) Compose(
        string changedByName, string businessName, bool? requestEmails, bool? quoteReplyEmails, string? appUrl)
    {
        // Typed by people: one line each, and encoded in the body.
        var who = NoticeEmailText.OneLine(changedByName);
        var business = NoticeEmailText.OneLine(businessName);
        var app = NoticeEmailText.App(appUrl);

        var changes = new List<string>();
        if (requestEmails is { } requests)
            changes.Add(requests
                ? "Ahora te llegan las solicitudes en línea de la Agenda, su recordatorio antes del límite y las citas que sus clientes cancelan."
                : "Ya no te llegan las solicitudes en línea de la Agenda, su recordatorio ni las citas que sus clientes cancelan.");
        if (quoteReplyEmails is { } quoteReplies)
            changes.Add(quoteReplies
                ? "Ahora te llegan las respuestas de los clientes a las cotizaciones."
                : "Ya no te llegan las respuestas de los clientes a las cotizaciones.");

        var subject = $"{who} cambió tus avisos por correo en {business}";
        var profile = app is null ? "Mi perfil de TienditApp" : $"<a href='{app}/profile'>Mi perfil</a>";
        var body = string.Join(
            "\n",
            "<h2>Cambiaron tus avisos por correo</h2>",
            $"<p><strong>{Html(who)}</strong> cambió los avisos que te llegan de {Html(business)}:</p>",
            $"<ul>{string.Concat(changes.Select(change => $"<li>{change}</li>"))}</ul>",
            $"<p>Puedes cambiarlos cuando quieras en {profile}.</p>",
            $"<p style='color:#6c757d;font-size:12px'>Te llega porque alguien de {Html(business)} cambió tus avisos. Si estás de acuerdo, no tienes que hacer nada.</p>");
        return (subject, body);
    }

    private static string Html(string value) => NoticeEmailText.Html(value);
}

/// <summary>
/// Sends the person the email of a change to their notices made by someone else (Fase 30A, ADR 0081), from the platform's
/// provider: only while they are still an active member of the business with an active account and a confirmed email, as
/// the other notices. It says who changed them by their name in the account; one that no longer exists is «Alguien de tu
/// equipo». A failure of the provider throws, and the outbox retries it.
/// </summary>
public sealed class NoticeEmailsChangedEmailHandler : IOutboxEventHandler<TenantUserNoticeEmailsChangedEvent>
{
    public const string SomeoneOfTheTeam = "Alguien de tu equipo";

    private readonly IApplicationDbContext _context;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;

    public NoticeEmailsChangedEmailHandler(IApplicationDbContext context, IEmailService email, IAppLinks links)
    {
        _context = context;
        _email = email;
        _links = links;
    }

    public async Task HandleAsync(
        TenantUserNoticeEmailsChangedEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId || (domainEvent.RequestEmails is null && domainEvent.QuoteReplyEmails is null))
            return;

        var member = await _context.TenantUsers
            .AsNoTracking()
            .Where(m => m.Id == domainEvent.TenantUserId && m.TenantId == tenantId && m.Status == TenantUserStatus.Active)
            .Join(
                _context.Users.Where(u => u.IsActive && u.EmailConfirmed && u.Email != null),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { u.Email })
            .FirstOrDefaultAsync(cancellationToken);
        if (member is null)
            return;

        var changedBy = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == domainEvent.ChangedByUserId)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(cancellationToken);
        var name = changedBy is null ? "" : $"{changedBy.FirstName} {changedBy.LastName}".Trim();
        var business = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstAsync(cancellationToken);

        var (subject, body) = NoticeEmailsChangedEmail.Compose(
            name.Length == 0 ? SomeoneOfTheTeam : name, business, domainEvent.RequestEmails, domainEvent.QuoteReplyEmails, _links.BaseUrl);
        await _email.SendEmailAsync(member.Email!, subject, body);
    }
}
