using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Quotes.Events;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Quotes;

// The quote reaches the customer from TienditApp (Fase 29D, ADR 0053 point 16): by email, from the platform in the name of
// the business, and by WhatsApp, from the business's own number with its template or TienditApp's text. Each send stays in
// the history of its version with its outcome.

/// <summary>An email of a version of a quote to its customer, as its detail shows it.</summary>
public class QuoteEmailDto
{
    public Guid Id { get; set; }
    public string To { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public QuoteEmailFailure? Failure { get; set; }
    /// <summary>The tries the mail server did not take; the queue tries again while it is neither sent nor refused.</summary>
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LastFailedAttemptAt { get; set; }

    public static QuoteEmailDto From(QuoteEmail email) => new()
    {
        Id = email.Id,
        To = email.To,
        RequestedAt = email.RequestedAt,
        SentAt = email.SentAt,
        FailedAt = email.FailedAt,
        Failure = email.Failure,
        FailedAttempts = email.FailedAttempts,
        LastFailedAttemptAt = email.LastFailedAttemptAt
    };
}

/// <summary>A WhatsApp message of a version of a quote to its customer, as its detail shows it.</summary>
public class QuoteWhatsAppDto
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>In the queue, sent (WhatsApp took it), or not sent.</summary>
    public OutgoingMessageStatus Status { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>When WhatsApp says it reached the customer's phone, and when they read it.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    /// <summary>The tries that failed; the queue tries again while it is pending.</summary>
    public int Attempts { get; set; }
    /// <summary>Why it was not sent, or why the last try failed.</summary>
    public string? Error { get; set; }

    public static QuoteWhatsAppDto From(OutgoingMessage message) => new()
    {
        Id = message.Id,
        Phone = message.Phone,
        RequestedAt = message.CreatedAt,
        Status = message.Status,
        SentAt = message.SentAt,
        DeliveredAt = message.DeliveredAt,
        ReadAt = message.ReadAt,
        Attempts = message.Attempts,
        Error = message.LastError
    };
}

/// <summary>A WhatsApp template the business can send its quotes with.</summary>
public class QuoteWhatsAppTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Body { get; set; } = null!;
    /// <summary>Its name in Meta, when it is approved there: it reaches whoever did not write in 24 hours.</summary>
    public string? ProviderTemplateName { get; set; }
}

/// <summary>What the quotes sent from TienditApp share: their limits, their texts and their history.</summary>
public static partial class QuoteDeliveries
{
    public static readonly TimeSpan Day = TimeSpan.FromDays(1);

    /// <summary>How many of the latest sends of each channel a version's detail shows.</summary>
    public const int Shown = 5;

    /// <summary>
    /// The longest text of a quote's message, written: WhatsApp takes 1,024 characters for a template's body. A template that
    /// comes out longer would lose the link at the end: TienditApp's text goes instead.
    /// </summary>
    public const int MaxTextLength = 900;

    /// <summary>The page of a quote: the link travels in the fragment, which never reaches a server log.</summary>
    public const string LinkPath = "/cotizacion#";

    /// <summary>TienditApp's text when the business has no template for its quotes.</summary>
    public const string DefaultText =
        "Hola {nombre}: {negocio} te manda la cotización {folio} por {monto}, vale hasta el {fecha}. Mírala aquí: {enlace}";

    public const string NotConnectedMessage =
        "Conecta el WhatsApp de tu negocio en Configuración › WhatsApp para mandar cotizaciones desde TienditApp.";

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    // Meta refuses a template parameter with line breaks, tabs or more than four spaces in a row.
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>«COT-000123», or «COT-000123 v2» from its second version on.</summary>
    public static string Folio(string number, int revision) => revision > 1 ? $"{number} v{revision}" : number;

    /// <summary>The link of a version, with the app's address when it is configured.</summary>
    public static string Link(string trackingToken, string? appUrl) => (appUrl ?? string.Empty) + LinkPath + trackingToken;

    /// <summary>The last day it is valid, as the customer reads it: «17 de noviembre de 2026».</summary>
    public static string Date(DateOnly day) => day.ToString("d 'de' MMMM 'de' yyyy", Spanish);

    /// <summary>The latest emails and WhatsApp messages of a version, the newest first.</summary>
    public static async Task<(List<QuoteEmailDto> Emails, List<QuoteWhatsAppDto> WhatsApps)> LatestAsync(
        IApplicationDbContext context, Quote quote, CancellationToken cancellationToken)
    {
        var emails = await context.QuoteEmails.AsNoTracking()
            .Where(e => e.QuoteId == quote.Id && e.TenantId == quote.TenantId)
            .OrderByDescending(e => e.RequestedAt)
            .Take(Shown)
            .ToListAsync(cancellationToken);
        var messages = await context.OutgoingMessages.AsNoTracking()
            .Where(m => m.QuoteId == quote.Id && m.TenantId == quote.TenantId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Shown)
            .ToListAsync(cancellationToken);
        return (emails.Select(QuoteEmailDto.From).ToList(), messages.Select(QuoteWhatsAppDto.From).ToList());
    }

    /// <summary>The templates the business can send its quotes with: active ones, for quotes.</summary>
    public static Task<List<QuoteWhatsAppTemplateDto>> TemplatesAsync(
        IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
        => context.MessageTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsActive && t.Purpose == MessagePurpose.QuoteDocument)
            .OrderBy(t => t.Name)
            .Select(t => new QuoteWhatsAppTemplateDto { Id = t.Id, Name = t.Name, Body = t.Body, ProviderTemplateName = t.ProviderTemplateName })
            .ToListAsync(cancellationToken);

    /// <summary>The template the business chose for its quotes, while it is active and for quotes; null otherwise.</summary>
    public static async Task<MessageTemplate?> TemplateAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
    {
        var templateId = await context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.QuoteWhatsAppTemplateId)
            .FirstOrDefaultAsync(cancellationToken);
        return templateId is null
            ? null
            : await context.MessageTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId && t.IsActive
                    && t.Purpose == MessagePurpose.QuoteDocument, cancellationToken);
    }

    /// <summary>
    /// The placeholders of a quote's text, each on one line: its customer, the business, its folio, what the customer pays
    /// (the total less the visit it takes off), the last day it is valid and its link.
    /// </summary>
    public static Dictionary<string, string> Values(Quote quote, string businessName, decimal toPay, string link) => new()
    {
        [MessageTemplateText.Name] = OneLine(quote.CustomerName),
        [MessageTemplateText.Business] = OneLine(businessName),
        [MessageTemplateText.Folio] = Folio(quote.Number, quote.Revision),
        [MessageTemplateText.Amount] = OrderStatusLabels.FormatMoney(toPay),
        [MessageTemplateText.Date] = quote.ValidUntil is { } until ? Date(until) : string.Empty,
        [MessageTemplateText.Link] = link
    };

    /// <summary>
    /// The message that carries <paramref name="quote"/> to <paramref name="phone"/>: the business's template for its quotes
    /// (an active one) or TienditApp's text, written now with what the quote says.
    /// </summary>
    public static async Task<OutgoingMessage> BuildWhatsAppAsync(
        IApplicationDbContext context, Quote quote, string phone, Guid requestedByUserId, string? appUrl, DateOnly today,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var business = await context.Tenants.AsNoTracking()
            .Where(t => t.Id == quote.TenantId)
            .Select(t => t.Name)
            .FirstAsync(cancellationToken);
        var stillPaid = await QuoteVisitCredits.StillPaidAsync(context, quote, cancellationToken);
        var values = Values(quote, business, quote.ToPay(stillPaid), Link(quote.TrackingToken!, appUrl));
        var template = await TemplateAsync(context, quote.TenantId, cancellationToken);
        if (template is not null && MessageTemplateText.Render(template.Body, values).Length > MaxTextLength)
            template = null;
        var text = template?.Body ?? DefaultText;
        var parameters = template?.ProviderTemplateName is null ? [] : MessageTemplateText.Parameters(text, values);
        return OutgoingMessage.ForQuote(
            quote, phone, values[MessageTemplateText.Name], MessageTemplateText.Render(text, values), template?.ProviderTemplateName,
            template?.LanguageCode ?? MessageTemplate.DefaultLanguageCode, parameters, requestedByUserId, today, now);
    }

    private static string OneLine(string text) => Whitespace().Replace(text, " ").Trim();
}

/// <summary>
/// Sends a version of a quote, sent and valid, by email from TienditApp: «Mandar por correo». The email goes out from the
/// outbox, in the name of the business, with the quote in its body and its link.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record SendQuoteByEmailCommand(Guid Id, string Email) : IRequest<Result<QuoteEmailDto>>, ITransactionalRequest;

/// <summary>
/// Sends a version of a quote, sent and valid, by WhatsApp from the business's number: «Mandar por WhatsApp». It goes out from
/// the queue of WhatsApp messages, which retries it and hears whether it was delivered and read.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record SendQuoteByWhatsAppCommand(Guid Id, string Phone) : IRequest<Result<QuoteWhatsAppDto>>, ITransactionalRequest;

public class SendQuoteByEmailCommandValidator : AbstractValidator<SendQuoteByEmailCommand>
{
    public SendQuoteByEmailCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().WithMessage(QuoteRules.EmailMessage)
            .Must(email => InvoiceEmailAddress.IsValid(email.Trim())).WithMessage(InvoiceEmailAddress.InvalidMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Email), ApplyConditionTo.CurrentValidator);
    }
}

public class SendQuoteByWhatsAppCommandValidator : AbstractValidator<SendQuoteByWhatsAppCommand>
{
    public SendQuoteByWhatsAppCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Escribe el WhatsApp al que se manda.")
            .MaximumLength(30).WithMessage(InvoiceWhatsApp.InvalidMessage);
    }
}

/// <summary>
/// The sends of a version of a quote. The limits are counted under the business's lock of quote sends, so two requests at
/// once do not both pass the last one.
/// </summary>
public class QuoteDeliveryHandler :
    IRequestHandler<SendQuoteByEmailCommand, Result<QuoteEmailDto>>,
    IRequestHandler<SendQuoteByWhatsAppCommand, Result<QuoteWhatsAppDto>>
{
    public const string NotAvailableMessage = "No se pudo identificar quién lo manda: vuelve a iniciar sesión.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IQuoteSendingLock _lock;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;

    public QuoteDeliveryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IPhoneNormalizationService phones,
        IQuoteSendingLock sendingLock,
        IAppLinks links,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _phones = phones;
        _lock = sendingLock;
        _links = links;
        _clock = clock;
    }

    private Guid TenantId => _tenantService.TenantId;

    public async Task<Result<QuoteEmailDto>> Handle(SendQuoteByEmailCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<QuoteEmailDto>(NotAvailableMessage);
        // Another business's quote is not there.
        var quote = await QuotesHandler.FindAsync(_context, TenantId, request.Id, cancellationToken, tracking: false);
        var today = await QuoteCalendar.TodayAsync(_context, TenantId, _clock.UtcNow, cancellationToken);
        if (quote.StatusOn(today) != QuoteStatus.Sent || quote.TrackingToken is null)
            return Result.Failure<QuoteEmailDto>(QuoteRules.NotSendableMessage);

        await _lock.AcquireAsync(TenantId, cancellationToken);
        var now = _clock.UtcNow;
        var since = now - QuoteDeliveries.Day;
        var sent = _context.QuoteEmails.Where(e => e.TenantId == TenantId && e.RequestedAt > since);
        if (await sent.CountAsync(e => e.QuoteId == quote.Id, cancellationToken) >= QuoteRules.MaxSendsPerQuoteVersion)
            return Result.Failure<QuoteEmailDto>(QuoteRules.TooManySendsMessage);
        if (await sent.CountAsync(cancellationToken) >= QuoteRules.MaxSendsPerBusiness)
            return Result.Failure<QuoteEmailDto>(QuoteRules.TooManyBusinessSendsMessage);

        QuoteEmail email;
        try
        {
            email = QuoteEmail.Request(quote, request.Email, userId, today, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteEmailDto>(ex.Message);
        }

        _context.QuoteEmails.Add(email);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(QuoteEmailDto.From(email));
    }

    public async Task<Result<QuoteWhatsAppDto>> Handle(SendQuoteByWhatsAppCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<QuoteWhatsAppDto>(NotAvailableMessage);
        var quote = await QuotesHandler.FindAsync(_context, TenantId, request.Id, cancellationToken, tracking: false);
        var today = await QuoteCalendar.TodayAsync(_context, TenantId, _clock.UtcNow, cancellationToken);
        if (quote.StatusOn(today) != QuoteStatus.Sent || quote.TrackingToken is null)
            return Result.Failure<QuoteWhatsAppDto>(QuoteRules.NotSendableMessage);
        if (!await CampaignRules.IsWhatsAppConnectedAsync(_context, TenantId, cancellationToken))
            return Result.Failure<QuoteWhatsAppDto>(QuoteDeliveries.NotConnectedMessage);
        if (!PhoneNumberRules.TryNormalizeTypedPhone(_phones, request.Phone, out var phone))
            return Result.Failure<QuoteWhatsAppDto>(InvoiceWhatsApp.InvalidMessage);

        await _lock.AcquireAsync(TenantId, cancellationToken);
        var now = _clock.UtcNow;
        var since = now - QuoteDeliveries.Day;
        var sent = _context.OutgoingMessages.Where(m => m.TenantId == TenantId && m.QuoteId != null && m.CreatedAt > since);
        if (await sent.CountAsync(m => m.QuoteId == quote.Id, cancellationToken) >= QuoteRules.MaxSendsPerQuoteVersion)
            return Result.Failure<QuoteWhatsAppDto>(QuoteRules.TooManySendsMessage);
        if (await sent.CountAsync(cancellationToken) >= QuoteRules.MaxSendsPerBusiness)
            return Result.Failure<QuoteWhatsAppDto>(QuoteRules.TooManyBusinessSendsMessage);

        OutgoingMessage message;
        try
        {
            message = await QuoteDeliveries.BuildWhatsAppAsync(_context, quote, phone, userId, _links.BaseUrl, today, now, cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteWhatsAppDto>(ex.Message);
        }

        _context.OutgoingMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(QuoteWhatsAppDto.From(message));
    }
}

/// <summary>
/// Sends the email of a quote that was asked for (Fase 29D), with the quote as it reads now. The mail server not answering
/// throws, so the outbox tries again; refused, or not set up, the email ends; past the last attempt it ends not delivered,
/// and a person can send it again.
/// </summary>
public sealed class QuoteEmailRequestedHandler : IOutboxEventHandler<QuoteEmailRequestedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<QuoteEmailRequestedHandler> _logger;

    public QuoteEmailRequestedHandler(
        IApplicationDbContext context,
        IEmailService email,
        IAppLinks links,
        IDateTimeProvider clock,
        ILogger<QuoteEmailRequestedHandler> logger)
    {
        _context = context;
        _email = email;
        _links = links;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(QuoteEmailRequestedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var email = await _context.QuoteEmails.FirstOrDefaultAsync(e => e.Id == domainEvent.QuoteEmailId, cancellationToken);
        if (email is null || !email.IsPending)
            return;

        try
        {
            await SendAsync(email, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && context.IsLastAttempt)
        {
            // The outbox sets the message aside after this attempt: the email ends, and a person can send it again.
            _logger.LogWarning("The email {QuoteEmailId} was not delivered after {Attempts} attempts: {Error}",
                email.Id, context.Attempt, ex.GetType().Name);
            email.Fail(QuoteEmailFailure.NotDelivered, _clock.UtcNow);
        }
    }

    private async Task SendAsync(QuoteEmail email, CancellationToken cancellationToken)
    {
        var quote = await _context.Quotes.AsNoTracking()
            .Include(q => q.Lines)
            .FirstAsync(q => q.Id == email.QuoteId && q.TenantId == email.TenantId, cancellationToken);
        var business = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == email.TenantId)
            .Select(t => new { t.Name, t.ContactEmail, t.ContactPhone })
            .FirstAsync(cancellationToken);
        var stillPaid = await QuoteVisitCredits.StillPaidAsync(_context, quote, cancellationToken);

        var message = new DocumentEmail(
            email.To,
            Subject(quote, business.Name),
            Body(quote, business.Name, business.ContactEmail, business.ContactPhone, stillPaid, _links.BaseUrl),
            $"{business.Name} vía TienditApp",
            business.ContactEmail,
            []);

        EmailSendOutcome outcome;
        try
        {
            outcome = await _email.SendDocumentsAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The handler's changes are discarded when it throws: the failed try is kept on its own.
            email.AttemptFailed(_clock.UtcNow);
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }

        var now = _clock.UtcNow;
        switch (outcome)
        {
            case EmailSendOutcome.Sent:
                email.Sent(now);
                break;
            case EmailSendOutcome.NotConfigured:
                email.Fail(QuoteEmailFailure.NotConfigured, now);
                break;
            default:
                email.Fail(QuoteEmailFailure.Rejected, now);
                break;
        }
    }

    public static string Subject(Quote quote, string businessName)
        => $"Cotización {QuoteDeliveries.Folio(quote.Number, quote.Revision)} de {businessName.ReplaceLineEndings(" ").Trim()}";

    /// <summary>
    /// The quote as the customer reads it on its page: what it includes, its taxes, the visit it takes off, what is left to
    /// pay, until when it is valid and its link. Everything the business or the customer wrote is encoded.
    /// </summary>
    public static string Body(Quote quote, string businessName, string? replyTo, string? businessPhone, decimal? stillPaidForTheVisit, string? appUrl)
    {
        static string E(string text) => WebUtility.HtmlEncode(text);
        static string Money(decimal amount) => E(OrderStatusLabels.FormatMoney(amount));

        var folio = QuoteDeliveries.Folio(quote.Number, quote.Revision);
        var link = QuoteDeliveries.Link(quote.TrackingToken ?? string.Empty, appUrl);
        var body = new StringBuilder();
        body.Append($"<p>Hola {E(quote.CustomerName)}:</p>");
        body.Append($"<p><strong>{E(businessName)}</strong> te manda la cotización <strong>{E(folio)}</strong>");
        if (quote.ValidUntil is { } until)
            body.Append($", que vale hasta el {E(QuoteDeliveries.Date(until))}");
        body.Append(".</p>");

        body.Append("<table style=\"border-collapse:collapse;width:100%;max-width:560px\">");
        body.Append("<tr><th align=\"left\">Qué incluye</th><th align=\"right\">Cantidad</th><th align=\"right\">Importe</th></tr>");
        foreach (var line in quote.Lines.OrderBy(line => line.Position))
            body.Append($"<tr><td>{E(line.Description)}<br><small>{Money(line.UnitPrice)} c/u</small></td>"
                + $"<td align=\"right\">{E(line.Quantity.ToString("0.###", CultureInfo.InvariantCulture))}</td>"
                + $"<td align=\"right\">{Money(line.Total)}</td></tr>");
        body.Append("</table>");

        var credit = quote.VisitCreditStanding(stillPaidForTheVisit);
        body.Append("<p>");
        body.Append($"Subtotal: {Money(quote.SubTotal)}<br>IVA: {Money(quote.Tax)}<br><strong>Total: {Money(quote.Total)}</strong>");
        if (credit > 0)
            body.Append($"<br>Visita (se descuenta): −{Money(credit)}<br><strong>Por pagar: {Money(quote.ToPay(stillPaidForTheVisit))}</strong>");
        body.Append("</p>");
        body.Append(quote.PricesIncludeTax ? "<p><small>Los precios incluyen IVA.</small></p>" : "<p><small>El IVA se suma a los precios.</small></p>");
        if (!string.IsNullOrWhiteSpace(quote.Note))
            body.Append($"<p>{E(quote.Note).ReplaceLineEndings("<br>")}</p>");

        body.Append($"<p><a href=\"{E(link)}\">Ver la cotización</a></p>");
        if (!string.IsNullOrWhiteSpace(replyTo))
            body.Append("<p>Para aceptarla o preguntar algo, responde a este correo.</p>");
        else if (!string.IsNullOrWhiteSpace(businessPhone))
            body.Append($"<p>Para aceptarla o preguntar algo, comunícate con {E(businessName)} al {E(businessPhone)}.</p>");
        else
            body.Append($"<p>Para aceptarla o preguntar algo, comunícate con {E(businessName)}.</p>");
        return body.ToString();
    }
}
