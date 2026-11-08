using FoodParkDigital.Application.Appointments.RequestNotices;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Orders.CancellationNotices;
using FoodParkDigital.Application.Quotes.ReplyNotices;
using FoodParkDigital.Domain.Identity.Enums;
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
        string changedByName, string businessName, bool? requestEmails, bool? quoteReplyEmails, string? appUrl,
        bool? orderCancellationEmails = null)
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
        if (orderCancellationEmails is { } orders)
            changes.Add(orders
                ? "Ahora te llegan los pedidos en línea que sus clientes cancelan."
                : "Ya no te llegan los pedidos en línea que sus clientes cancelan.");

        var subject = $"{who} cambió tus avisos por correo en {business}";
        var profile = app is null ? "Mi perfil de TienditApp" : $"<a href='{app}/profile'>Mi perfil</a>";
        var body = string.Join(
            "\n",
            "<h2>Cambiaron tus avisos por correo</h2>",
            $"<p><strong>{Html(who)}</strong> cambió los avisos que te llegan de {Html(business)}:</p>",
            $"<ul>{string.Concat(changes.Select(change => $"<li>{change}</li>"))}</ul>",
            $"<p>Puedes cambiarlos cuando quieras en {profile}.</p>",
            $"<p style='color:#6c757d;font-size:12px'>Te llega porque cambiaron tus avisos en {Html(business)}. Si estás de acuerdo, no tienes que hacer nada.</p>");
        return (subject, body);
    }

    private static string Html(string value) => NoticeEmailText.Html(value);
}

/// <summary>
/// Sends the person the email of a change to their notices made by someone else (Fase 30A, ADR 0081), from the platform's
/// provider: only while they are still an active member of a business that is not suspended, with an active account and a
/// confirmed email, as the other notices, and only while what they get is still what the change said (a later change sends
/// its own). It speaks only of the notices that may reach them now, as the API would send them (the agenda's to whoever may
/// answer the requests, the answers to quotes to whoever works the quotes, the online orders their customers cancel to whoever
/// may confirm the orders, Fase 30B): one they may not get is not theirs to change in
/// Mi perfil either, and with none left nothing is sent. It says who changed them by the name of their account when they are of the team; the platform's admin acting
/// on the business is «El soporte de TienditApp», and an account that no longer exists, «Alguien de tu equipo». A failure
/// of the provider throws, and the outbox retries it.
/// </summary>
public sealed class NoticeEmailsChangedEmailHandler : IOutboxEventHandler<TenantUserNoticeEmailsChangedEvent>
{
    public const string SomeoneOfTheTeam = "Alguien de tu equipo";
    public const string PlatformSupport = "El soporte de TienditApp";

    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;

    public NoticeEmailsChangedEmailHandler(IApplicationDbContext context, ITenantAccessService access, IEmailService email, IAppLinks links)
    {
        _context = context;
        _access = access;
        _email = email;
        _links = links;
    }

    public async Task HandleAsync(
        TenantUserNoticeEmailsChangedEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId
            || (domainEvent.RequestEmails is null && domainEvent.QuoteReplyEmails is null && domainEvent.OrderCancellationEmails is null))
            return;

        var business = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Name, t.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (business is null || business.Status == TenantStatus.Suspended)
            return;

        var member = await _context.TenantUsers
            .AsNoTracking()
            .Where(m => m.Id == domainEvent.TenantUserId && m.TenantId == tenantId && m.Status == TenantUserStatus.Active)
            .Join(
                _context.Users.Where(u => u.IsActive && u.EmailConfirmed && u.Email != null),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { Membership = m, u.Email, UserId = u.Id })
            .FirstOrDefaultAsync(cancellationToken);
        // Changed again since (and that change sends its own): this one no longer says what they get.
        if (member is null
            || (domainEvent.RequestEmails is { } requests && member.Membership.GetsRequestEmails != requests)
            || (domainEvent.QuoteReplyEmails is { } quoteReplies && member.Membership.GetsQuoteReplyEmails != quoteReplies)
            || (domainEvent.OrderCancellationEmails is { } orders && member.Membership.GetsOrderCancellationEmails != orders))
            return;

        // Only what may reach them now: a notice turned on without the permission it needs would not arrive.
        var access = await _access.GetAccessSnapshotAsync(member.UserId, tenantId, cancellationToken);
        var requestEmails = RequestEmailRecipients.MayAnswer(access) ? domainEvent.RequestEmails : null;
        var quoteReplyEmails = QuoteReplyEmailRecipients.MayWorkQuotes(access) ? domainEvent.QuoteReplyEmails : null;
        var orderCancellationEmails = OrderCancellationEmailRecipients.MayConfirmOrders(access) ? domainEvent.OrderCancellationEmails : null;
        if (requestEmails is null && quoteReplyEmails is null && orderCancellationEmails is null)
            return;

        var (subject, body) = NoticeEmailsChangedEmail.Compose(
            await WhoAsync(domainEvent.ChangedByUserId, tenantId, cancellationToken),
            business.Name,
            requestEmails,
            quoteReplyEmails,
            _links.BaseUrl,
            orderCancellationEmails);
        await _email.SendEmailAsync(member.Email!, subject, body);
    }

    /// <summary>Who changed them, as the person reads it: never the name of someone who is not of the business.</summary>
    private async Task<string> WhoAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var account = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Role })
            .FirstOrDefaultAsync(cancellationToken);
        if (account is null)
            return SomeoneOfTheTeam;
        if (!await _context.TenantUsers.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken))
            return account.Role == UserRole.SuperAdmin ? PlatformSupport : SomeoneOfTheTeam;
        var name = $"{account.FirstName} {account.LastName}".Trim();
        return name.Length == 0 ? SomeoneOfTheTeam : name;
    }
}
