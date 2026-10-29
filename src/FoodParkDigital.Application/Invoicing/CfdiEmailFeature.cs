using System.Globalization;
using System.Net;
using System.Text;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Fiscal.Events;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>An email of a CFDI to the customer, as Pedidos shows it (Fase 28E, ADR 0049).</summary>
public class CfdiEmailDto
{
    public Guid Id { get; set; }
    public CfdiEmailDocument Document { get; set; }
    public Guid CfdiId { get; set; }
    public string To { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>TienditApp sent it on its own when the CFDI was stamped; otherwise a person asked for it.</summary>
    public bool ByTienditApp { get; set; }
    /// <summary>The customer asked for it on the page of the ticket (Fase 28F), not a person of the business.</summary>
    public bool ByCustomer { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>It went with the XML only: the PAC did not give its PDF.</summary>
    public bool SentWithoutPdf { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public CfdiEmailFailure? Failure { get; set; }
    /// <summary>The tries the mail server did not take; the queue tries again while it is neither sent nor refused.</summary>
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LastFailedAttemptAt { get; set; }

    public static CfdiEmailDto From(CfdiEmail email) => new()
    {
        Id = email.Id,
        Document = email.Document,
        CfdiId = email.CfdiId,
        To = email.To,
        RequestedAt = email.RequestedAt,
        ByTienditApp = email.RequestedByUserId is null,
        ByCustomer = email.RequestedByUserId == OrderInvoice.SelfInvoiceRequester,
        SentAt = email.SentAt,
        SentWithoutPdf = email.SentWithoutPdf,
        FailedAt = email.FailedAt,
        Failure = email.Failure,
        FailedAttempts = email.FailedAttempts,
        LastFailedAttemptAt = email.LastFailedAttemptAt
    };
}

/// <summary>
/// The emails of the CFDI of an order's invoice to its customer (Fase 28E, ADR 0049): the invoice, its payment complements
/// and its credit notes, with their XML and PDF.
/// </summary>
public static class CfdiEmails
{
    /// <summary>The most emails of one CFDI a business asks for in a day: a typo fixed, not a mailing.</summary>
    public const int MaxPerCfdiPerDay = 5;
    /// <summary>The most emails of CFDI a business asks for in a day, on its own and by hand: the platform's sender is shared.</summary>
    public const int MaxPerBusinessPerDay = 300;
    /// <summary>How many of the latest emails of each CFDI the order's invoicing shows.</summary>
    public const int Shown = 3;

    public static readonly TimeSpan Day = TimeSpan.FromDays(1);

    public static string TooManyForCfdiMessage(string number)
        => $"{number} ya se envió {MaxPerCfdiPerDay} veces hoy: revisa el correo con el cliente antes de enviarlo otra vez.";

    public const string TooManyForBusinessMessage =
        "Tu negocio ya envió muchos correos de facturas hoy: vuelve a intentarlo mañana o descarga el XML y el PDF.";

    /// <summary>
    /// The email of a CFDI of an order's invoice that was just stamped, when its invoice has an email: added to be saved with
    /// the stamp, so another attempt that stamped it first (and asked for its own) leaves this one unsaved. It never throws:
    /// an address that no longer passes (a rule tightened later) skips the email, never the stamp.
    /// </summary>
    public static async Task QueueOnStampAsync(IApplicationDbContext context, Cfdi cfdi, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var address = cfdi switch
        {
            OrderInvoice invoice => invoice.ReceiverEmail,
            PaymentComplement complement => await EmailOfAsync(context, complement.OrderInvoiceId, cancellationToken),
            // A global invoice's credit note is to the general public (Fase 28I): nobody to send it to.
            CreditNote { OrderInvoiceId: { } invoiceId } => await EmailOfAsync(context, invoiceId, cancellationToken),
            _ => null
        };
        if (address is not null && cfdi.Status == OrderInvoiceStatus.Stamped && InvoiceEmailAddress.IsValid(address))
            context.CfdiEmails.Add(CfdiEmail.Request(cfdi, address, null, now));
    }

    /// <summary>The latest emails of each CFDI of these invoices of an order, the newest first.</summary>
    public static async Task<Dictionary<Guid, List<CfdiEmailDto>>> LatestAsync(
        IApplicationDbContext context, IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        if (invoiceIds.Count == 0)
            return [];
        var emails = await context.CfdiEmails.AsNoTracking()
            .Where(e => invoiceIds.Contains(e.OrderInvoiceId))
            .ToListAsync(cancellationToken);
        return emails
            .GroupBy(e => e.CfdiId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.RequestedAt).Take(Shown).Select(CfdiEmailDto.From).ToList());
    }

    /// <summary>The CFDI an email carries, if it is the business's (the query filter keeps the others out).</summary>
    public static async Task<Cfdi?> FindAsync(
        IApplicationDbContext context, CfdiEmailDocument document, Guid cfdiId, CancellationToken cancellationToken)
        => document switch
        {
            CfdiEmailDocument.Invoice => await context.OrderInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == cfdiId, cancellationToken),
            CfdiEmailDocument.PaymentComplement => await context.PaymentComplements.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cfdiId, cancellationToken),
            CfdiEmailDocument.CreditNote => await context.CreditNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == cfdiId, cancellationToken),
            _ => null
        };

    /// <summary>The order a CFDI of an order's invoice belongs to.</summary>
    public static Guid OrderOf(Cfdi cfdi) => cfdi switch
    {
        OrderInvoice invoice => invoice.OrderId,
        PaymentComplement complement => complement.OrderId,
        CreditNote note => note.OrderId,
        _ => throw new ArgumentException("Only the CFDI of an order's invoice go by email.", nameof(cfdi))
    };

    private static Task<string?> EmailOfAsync(IApplicationDbContext context, Guid invoiceId, CancellationToken cancellationToken)
        => context.OrderInvoices.AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => i.ReceiverEmail)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>
/// Sends a stamped CFDI of an order's invoice by email again, to its email or another one (whoever manages the orders):
/// «Enviar por correo». With <see cref="UseForInvoice"/>, what comes next of the invoice (its complements and credit notes)
/// goes to that email too. It goes out from the queue, like the one TienditApp sends when it is stamped. The limits are
/// counted under the business's invoicing lock, so two requests at once do not both pass the last one.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record SendCfdiByEmailCommand(CfdiEmailDocument Document, Guid CfdiId, string Email, bool UseForInvoice)
    : IRequest<Result<CfdiEmailDto>>, ITransactionalRequest;

public class SendCfdiByEmailCommandValidator : AbstractValidator<SendCfdiByEmailCommand>
{
    public SendCfdiByEmailCommandValidator()
    {
        RuleFor(x => x.Document).IsInEnum();
        RuleFor(x => x.CfdiId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().WithMessage("Escribe el correo al que se envía.")
            .Must(email => InvoiceEmailAddress.IsValid(email.Trim())).WithMessage(InvoiceEmailAddress.InvalidMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Email), ApplyConditionTo.CurrentValidator);
    }
}

public class SendCfdiByEmailCommandHandler : IRequestHandler<SendCfdiByEmailCommand, Result<CfdiEmailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingLock _lock;
    private readonly IDateTimeProvider _clock;

    public SendCfdiByEmailCommandHandler(
        IApplicationDbContext context, ICurrentUserService currentUser, IInvoicingLock invoicingLock, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _lock = invoicingLock;
        _clock = clock;
    }

    public async Task<Result<CfdiEmailDto>> Handle(SendCfdiByEmailCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<CfdiEmailDto>(InvoicingSettings.NotAvailableMessage);

        // Another business's CFDI, or one of an order this person does not see, is not there.
        var cfdi = await CfdiEmails.FindAsync(_context, request.Document, request.CfdiId, cancellationToken);
        if (cfdi is null || !await OrderInvoicing.SeesOrderAsync(_context, CfdiEmails.OrderOf(cfdi), cancellationToken))
            throw new EntityNotFoundException(request.Document.ToString(), request.CfdiId);
        if (cfdi.Status != OrderInvoiceStatus.Stamped)
            return Result.Failure<CfdiEmailDto>(CfdiEmail.NotStampedMessage);

        await _lock.AcquireAsync(cfdi.TenantId, cancellationToken);
        var now = _clock.UtcNow;
        var since = now - CfdiEmails.Day;
        if (await _context.CfdiEmails.CountAsync(e => e.CfdiId == cfdi.Id && e.RequestedAt > since, cancellationToken)
            >= CfdiEmails.MaxPerCfdiPerDay)
            return Result.Failure<CfdiEmailDto>(CfdiEmails.TooManyForCfdiMessage(cfdi.Number));
        if (await _context.CfdiEmails.CountAsync(e => e.RequestedAt > since, cancellationToken) >= CfdiEmails.MaxPerBusinessPerDay)
            return Result.Failure<CfdiEmailDto>(CfdiEmails.TooManyForBusinessMessage);

        CfdiEmail email;
        try
        {
            email = CfdiEmail.Request(cfdi, request.Email, userId, now);
            if (request.UseForInvoice)
            {
                var invoice = await _context.OrderInvoices.FirstAsync(i => i.Id == email.OrderInvoiceId, cancellationToken);
                invoice.SendTo(email.To);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<CfdiEmailDto>(ex.Message);
        }

        _context.CfdiEmails.Add(email);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CfdiEmailDto.From(email));
    }
}

/// <summary>
/// Sends an email of a CFDI from the outbox, after the change that asked for it: reads its XML and PDF and sends them with
/// what the customer needs to recognize it. A CFDI no longer stamped is not sent.
/// <list type="bullet">
/// <item>The PDF is the one kept when it was stamped. The email TienditApp asks for with the stamp waits one attempt for
/// it (the stamp keeps it right after); otherwise the PAC renders it from the stamped XML, as downloading it does. Without
/// it, it waits a couple of attempts and then sends the XML alone, which is the CFDI.</item>
/// <item>A mail server that does not take it now counts the try and throws, for the outbox to retry; one that refuses the
/// address for good, or no provider, ends it. On the outbox's last attempt any failure ends it as not delivered, so it
/// never stays pending.</item>
/// </list>
/// </summary>
public sealed class CfdiEmailRequestedHandler : IOutboxEventHandler<CfdiEmailRequestedEvent>
{
    /// <summary>The tries that wait for the PDF; from this one on, the XML goes alone.</summary>
    public const int AttemptsWaitingForPdf = 2;

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    private readonly IApplicationDbContext _context;
    private readonly IPrivateFileStorage _storage;
    private readonly IInvoicingGateway _gateway;
    private readonly IEmailService _email;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CfdiEmailRequestedHandler> _logger;

    public CfdiEmailRequestedHandler(
        IApplicationDbContext context,
        IPrivateFileStorage storage,
        IInvoicingGateway gateway,
        IEmailService email,
        IDateTimeProvider clock,
        ILogger<CfdiEmailRequestedHandler> logger)
    {
        _context = context;
        _storage = storage;
        _gateway = gateway;
        _email = email;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(CfdiEmailRequestedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var email = await _context.CfdiEmails.FirstOrDefaultAsync(e => e.Id == domainEvent.CfdiEmailId, cancellationToken);
        if (email is null || !email.IsPending)
            return;

        try
        {
            await SendAsync(email, context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && context.IsLastAttempt)
        {
            // The outbox sets the message aside after this attempt: the email ends, and a person can send it again.
            _logger.LogWarning("The email {CfdiEmailId} was not delivered after {Attempts} attempts: {Error}",
                email.Id, context.Attempt, ex.GetType().Name);
            email.Fail(CfdiEmailFailure.NotDelivered, _clock.UtcNow);
        }
    }

    private async Task SendAsync(CfdiEmail email, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var cfdi = await CfdiEmails.FindAsync(_context, email.Document, email.CfdiId, cancellationToken);
        if (cfdi is null || cfdi.Status != OrderInvoiceStatus.Stamped || cfdi.XmlFileKey is null || cfdi.Uuid is null)
        {
            _logger.LogInformation("The email {CfdiEmailId} was not sent: its CFDI no longer stands.", email.Id);
            email.Fail(CfdiEmailFailure.NoLongerStamped, _clock.UtcNow);
            return;
        }

        var xml = await ReadAsync(cfdi.XmlFileKey, cancellationToken)
            ?? throw new InvalidOperationException($"The XML of CFDI {cfdi.Id} is not in the private files.");
        var pdf = cfdi.PdfFileKey is null ? null : await ReadAsync(cfdi.PdfFileKey, cancellationToken);
        if (pdf is null && email.RequestedByUserId is null && context.Attempt == 1 && !context.IsLastAttempt)
            // Stamped a moment ago: the stamp keeps its PDF right after, and the next attempt takes it instead of asking the
            // PAC for it twice.
            throw new InvalidOperationException($"The PDF of CFDI {cfdi.Id} is being kept: the email waits for it.");
        pdf ??= await RenderPdfAsync(cfdi, xml, cancellationToken);
        if (pdf is null && context.Attempt <= AttemptsWaitingForPdf && !context.IsLastAttempt)
            throw new InvalidOperationException($"The PAC did not give the PDF of CFDI {cfdi.Id}: the email waits for it.");

        var business = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == cfdi.TenantId)
            .Select(t => new { t.Name, t.ContactEmail })
            .FirstAsync(cancellationToken);
        var fileName = $"{cfdi.Serie}-{cfdi.Folio}_{cfdi.Uuid}";
        var attachments = new List<EmailAttachment> { new($"{fileName}.xml", "application/xml", xml) };
        if (pdf is not null)
            attachments.Add(new($"{fileName}.pdf", "application/pdf", pdf));
        var message = new DocumentEmail(
            email.To,
            Subject(email.Document, cfdi.Number, business.Name),
            Body(email.Document, cfdi, business.Name, withPdf: pdf is not null),
            $"{business.Name} vía TienditApp",
            business.ContactEmail,
            attachments);

        EmailSendOutcome outcome;
        try
        {
            outcome = await _email.SendDocumentsAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            email.AttemptFailed(_clock.UtcNow);
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }

        var now = _clock.UtcNow;
        switch (outcome)
        {
            case EmailSendOutcome.Sent:
                email.Sent(withoutPdf: pdf is null, now);
                break;
            case EmailSendOutcome.NotConfigured:
                email.Fail(CfdiEmailFailure.NotConfigured, now);
                break;
            default:
                email.Fail(CfdiEmailFailure.Rejected, now);
                break;
        }
    }

    public static string Subject(CfdiEmailDocument document, string number, string businessName)
        => $"{Kind(document)} {number} de {businessName.ReplaceLineEndings(" ").Trim()}";

    /// <summary>What the customer needs to recognize it and check it with the SAT; the business's text is encoded.</summary>
    public static string Body(CfdiEmailDocument document, Cfdi cfdi, string businessName, bool withPdf)
    {
        var parsed = CfdiDocument.FromJson(cfdi.Document);
        var business = WebUtility.HtmlEncode(businessName.ReplaceLineEndings(" ").Trim());
        var amount = cfdi switch
        {
            PaymentComplement complement => $" por el pago de {OrderStatusLabels.FormatMoney(complement.Amount)}",
            CfdiInvoice invoice => $" por {OrderStatusLabels.FormatMoney(invoice.Total)}",
            CreditNote note => $" por {OrderStatusLabels.FormatMoney(note.Total)}",
            _ => string.Empty
        };
        var date = DateTime.TryParseExact(parsed.Date, CfdiDocument.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var issued)
            ? $", del {issued.ToString("d 'de' MMMM 'de' yyyy", Spanish)}"
            : string.Empty;
        var files = withPdf
            ? "<p>Adjuntamos el XML, que es el comprobante fiscal, y su PDF.</p>"
            : $"<p>Adjuntamos el XML, que es el comprobante fiscal. El PDF no estuvo disponible: pídeselo a {business} si lo necesitas.</p>";
        return $"""
            <p>Hola:</p>
            <p>{business} te envía {Article(document)} {WebUtility.HtmlEncode(cfdi.Number)}{amount}{date}.</p>
            <ul>
              <li>Emisor: {WebUtility.HtmlEncode(parsed.Issuer.Name)} (RFC {WebUtility.HtmlEncode(parsed.Issuer.Rfc)})</li>
              <li>Receptor: {WebUtility.HtmlEncode(parsed.Receiver.Name)} (RFC {WebUtility.HtmlEncode(parsed.Receiver.Rfc)})</li>
              <li>Folio fiscal: {WebUtility.HtmlEncode(cfdi.Uuid ?? string.Empty)}</li>
            </ul>
            {files}
            <p>Puedes verificarlo en el portal del SAT: <a href="https://verificacfdi.facturaelectronica.sat.gob.mx">verificacfdi.facturaelectronica.sat.gob.mx</a>.</p>
            <p>Si no esperabas este correo, respóndelo para avisarle a {business}.</p>
            """;
    }

    private static string Kind(CfdiEmailDocument document) => document switch
    {
        CfdiEmailDocument.PaymentComplement => "Complemento de pago",
        CfdiEmailDocument.CreditNote => "Nota de crédito",
        _ => "Factura"
    };

    private static string Article(CfdiEmailDocument document) => document switch
    {
        CfdiEmailDocument.PaymentComplement => "el complemento de pago",
        CfdiEmailDocument.CreditNote => "la nota de crédito",
        _ => "la factura"
    };

    private async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        await using var content = await _storage.OpenReadAsync(key, cancellationToken);
        if (content is null)
            return null;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    // The PAC renders it now from the stamped XML, as downloading it does; it is not kept.
    private async Task<byte[]?> RenderPdfAsync(Cfdi cfdi, byte[] stampedXml, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return null;
        try
        {
            return await _gateway.RenderPdfAsync(Encoding.UTF8.GetString(stampedXml), cancellationToken);
        }
        catch (Exception ex) when (ex is InvoicingGatewayUnavailableException or InvoicingGatewayRefusedException)
        {
            _logger.LogWarning("The PAC did not render the PDF of CFDI {CfdiId} for its email: {Error}", cfdi.Id, ex.GetType().Name);
            return null;
        }
    }
}
