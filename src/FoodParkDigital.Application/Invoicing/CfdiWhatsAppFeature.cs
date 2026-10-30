using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>A WhatsApp message of a CFDI to the customer, as Pedidos shows it (Fase 28M, ADR 0058).</summary>
public class CfdiWhatsAppDto
{
    public Guid Id { get; set; }
    public CfdiEmailDocument Document { get; set; }
    public Guid CfdiId { get; set; }
    public string Phone { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>TienditApp sent it on its own when the CFDI was stamped; otherwise a person asked for it.</summary>
    public bool ByTienditApp { get; set; }
    /// <summary>In the queue, sent (WhatsApp took it), not sent, or skipped (the CFDI stopped standing).</summary>
    public OutgoingMessageStatus Status { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>When WhatsApp says it reached the customer's phone, and when they read it.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    /// <summary>The tries that failed; the queue tries again while it is pending.</summary>
    public int Attempts { get; set; }
    /// <summary>Why it was not sent, or why the last try failed.</summary>
    public string? Error { get; set; }

    public static CfdiWhatsAppDto From(OutgoingMessage message) => new()
    {
        Id = message.Id,
        Document = message.CfdiDocument ?? CfdiEmailDocument.Invoice,
        CfdiId = message.CfdiId ?? Guid.Empty,
        Phone = message.Phone,
        RequestedAt = message.CreatedAt,
        ByTienditApp = message.RequestedByUserId is null,
        Status = message.Status,
        SentAt = message.SentAt,
        DeliveredAt = message.DeliveredAt,
        ReadAt = message.ReadAt,
        Attempts = message.Attempts,
        Error = message.LastError
    };
}

/// <summary>
/// The WhatsApp messages of the CFDI of an order's invoice to its customer (Fase 28M, ADR 0058): the invoice, its payment
/// complements and its credit notes, each with its PDF and a link to download its XML. They go through the queue of WhatsApp
/// messages, which retries them and hears from the webhook whether they were delivered and read.
/// </summary>
public static partial class CfdiWhatsApps
{
    /// <summary>The most messages of one CFDI a business asks for in a day: a wrong number fixed, not a mailing.</summary>
    public const int MaxPerCfdiPerDay = 5;
    /// <summary>The most messages of CFDI a business asks for in a day, on its own and by hand.</summary>
    public const int MaxPerBusinessPerDay = 300;
    /// <summary>How many of the latest messages of each CFDI the order's invoicing shows.</summary>
    public const int Shown = 3;

    /// <summary>
    /// The page that downloads the XML and the PDF: the link travels in the fragment, which never reaches a server log. The
    /// message keeps it without the app's address, which is added when it goes out.
    /// </summary>
    public const string LinkPath = "/factura#";

    /// <summary>TienditApp's text when the business has no template for its invoices: it goes as the PDF's caption.</summary>
    public const string DefaultText =
        "Hola {nombre}: {negocio} te envía {comprobante} por {monto}, del {fecha}. Descarga el XML y el PDF aquí: {enlace}";

    public const string NotConnectedMessage =
        "Conecta el WhatsApp de tu negocio en Configuración › WhatsApp para enviar facturas por WhatsApp.";

    public static string TooManyForCfdiMessage(string number)
        => $"{number} ya se envió {MaxPerCfdiPerDay} veces hoy por WhatsApp: revisa el número con el cliente antes de enviarlo otra vez.";

    public const string TooManyForBusinessMessage =
        "Tu negocio ya envió muchas facturas por WhatsApp hoy: vuelve a intentarlo mañana o descarga el XML y el PDF.";

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    // Meta refuses a template parameter with line breaks, tabs or more than four spaces in a row.
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// The message of a CFDI of an order's invoice that was just stamped, when its invoice has a WhatsApp: added to be saved
    /// with the stamp, so another attempt that stamped it first (and asked for its own) leaves this one unsaved. It never
    /// throws: a number that no longer passes skips the message, never the stamp.
    /// </summary>
    public static async Task QueueOnStampAsync(IApplicationDbContext context, Cfdi cfdi, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var phone = cfdi switch
        {
            OrderInvoice invoice => invoice.ReceiverWhatsApp,
            PaymentComplement complement => await WhatsAppOfAsync(context, complement.OrderInvoiceId, cancellationToken),
            // A global invoice's credit note is to the general public (Fase 28I): nobody to send it to.
            CreditNote { OrderInvoiceId: { } invoiceId } => await WhatsAppOfAsync(context, invoiceId, cancellationToken),
            _ => null
        };
        if (phone is null || cfdi.Status != OrderInvoiceStatus.Stamped || !InvoiceWhatsApp.IsPhone(phone))
            return;
        try
        {
            context.OutgoingMessages.Add(await BuildAsync(context, cfdi, phone, null, now, cancellationToken));
        }
        catch (DomainException)
        {
            // What the message needs no longer holds: the stamp goes on without it.
        }
    }

    /// <summary>
    /// The message that carries the stamped <paramref name="cfdi"/> to <paramref name="phone"/>: the business's template for
    /// its invoices (an active one, approved in Meta with a document header) or TienditApp's text, written now with the
    /// CFDI's data and a new link.
    /// </summary>
    public static async Task<OutgoingMessage> BuildAsync(
        IApplicationDbContext context, Cfdi cfdi, string phone, Guid? requestedByUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var business = await context.Tenants.AsNoTracking()
            .Where(t => t.Id == cfdi.TenantId)
            .Select(t => t.Name)
            .FirstAsync(cancellationToken);
        var template = await TemplateAsync(context, cancellationToken);
        var token = OrderTracking.NewToken();
        var values = Values(cfdi, business, LinkPath + token);
        var text = template?.Body ?? DefaultText;
        var parameters = template?.ProviderTemplateName is null ? [] : MessageTemplateText.Parameters(text, values);
        return OutgoingMessage.ForCfdi(
            cfdi, phone, values[MessageTemplateText.Name], MessageTemplateText.Render(text, values), template?.ProviderTemplateName,
            template?.LanguageCode ?? MessageTemplate.DefaultLanguageCode, parameters, token, requestedByUserId, now);
    }

    /// <summary>The template the business chose for its invoices, while it is active and for invoices; null otherwise.</summary>
    public static async Task<MessageTemplate?> TemplateAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        var templateId = await context.InvoicingProfiles.AsNoTracking()
            .Select(p => p.WhatsAppTemplateId)
            .FirstOrDefaultAsync(cancellationToken);
        return templateId is null
            ? null
            : await context.MessageTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive && t.Purpose == MessagePurpose.CfdiDocument, cancellationToken);
    }

    /// <summary>
    /// The placeholders of a CFDI's text, each on one line: the receiver of the CFDI, the business, «la factura F-123», its
    /// total (the payment of a complement), the day it was issued and the link.
    /// </summary>
    public static Dictionary<string, string> Values(Cfdi cfdi, string businessName, string link)
    {
        var parsed = CfdiDocument.FromJson(cfdi.Document);
        var amount = cfdi switch
        {
            PaymentComplement complement => complement.Amount,
            CfdiInvoice invoice => invoice.Total,
            CreditNote note => note.Total,
            _ => 0m
        };
        var date = DateTime.TryParseExact(parsed.Date, CfdiDocument.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var issued)
            ? issued.ToString("d 'de' MMMM 'de' yyyy", Spanish)
            : string.Empty;
        return new Dictionary<string, string>
        {
            [MessageTemplateText.Name] = OneLine(parsed.Receiver.Name),
            [MessageTemplateText.Business] = OneLine(businessName),
            [MessageTemplateText.Document] = $"{Article(cfdi)} {cfdi.Number}",
            [MessageTemplateText.Amount] = OrderStatusLabels.FormatMoney(amount),
            [MessageTemplateText.Date] = date,
            [MessageTemplateText.Link] = link
        };
    }

    /// <summary>
    /// The message's text or parameter with the app's address before its link («https://…/factura#…»), as it goes out. Without
    /// a configured address it stays as it was kept.
    /// </summary>
    public static string WithAppUrl(string text, string linkToken, string? appUrl)
        => appUrl is null ? text : text.Replace(LinkPath + linkToken, appUrl + LinkPath + linkToken, StringComparison.Ordinal);

    /// <summary>The latest messages of each CFDI of these invoices of an order, the newest first.</summary>
    public static async Task<Dictionary<Guid, List<CfdiWhatsAppDto>>> LatestAsync(
        IApplicationDbContext context, IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        if (invoiceIds.Count == 0)
            return [];
        var messages = await context.OutgoingMessages.AsNoTracking()
            .Where(m => m.Purpose == MessagePurpose.CfdiDocument && m.OrderInvoiceId != null && invoiceIds.Contains(m.OrderInvoiceId.Value))
            .ToListAsync(cancellationToken);
        return messages
            .GroupBy(m => m.CfdiId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(m => m.CreatedAt).Take(Shown).Select(CfdiWhatsAppDto.From).ToList());
    }

    private static string OneLine(string text) => Whitespace().Replace(text, " ").Trim();

    private static string Article(Cfdi cfdi) => cfdi switch
    {
        PaymentComplement => "el complemento de pago",
        CreditNote => "la nota de crédito",
        _ => "la factura"
    };

    private static Task<string?> WhatsAppOfAsync(IApplicationDbContext context, Guid invoiceId, CancellationToken cancellationToken)
        => context.OrderInvoices.AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => i.ReceiverWhatsApp)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>
/// Sends a stamped CFDI of an order's invoice by WhatsApp again, to its WhatsApp or another number (whoever manages the
/// orders): «Enviar por WhatsApp». With <see cref="UseForInvoice"/>, what comes next of the invoice (its complements and
/// credit notes) goes to that number too. It goes out from the queue, like the one TienditApp sends when it is stamped. The
/// limits are counted under the business's invoicing lock, so two requests at once do not both pass the last one.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record SendCfdiByWhatsAppCommand(CfdiEmailDocument Document, Guid CfdiId, string Phone, bool UseForInvoice)
    : IRequest<Result<CfdiWhatsAppDto>>, ITransactionalRequest;

public class SendCfdiByWhatsAppCommandValidator : AbstractValidator<SendCfdiByWhatsAppCommand>
{
    public SendCfdiByWhatsAppCommandValidator()
    {
        RuleFor(x => x.Document).IsInEnum();
        RuleFor(x => x.CfdiId).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Escribe el WhatsApp al que se envía.")
            .MaximumLength(30).WithMessage(InvoiceWhatsApp.InvalidMessage);
    }
}

public class SendCfdiByWhatsAppCommandHandler : IRequestHandler<SendCfdiByWhatsAppCommand, Result<CfdiWhatsAppDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IInvoicingLock _lock;
    private readonly IDateTimeProvider _clock;

    public SendCfdiByWhatsAppCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IPhoneNormalizationService phones,
        IInvoicingLock invoicingLock,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _phones = phones;
        _lock = invoicingLock;
        _clock = clock;
    }

    public async Task<Result<CfdiWhatsAppDto>> Handle(SendCfdiByWhatsAppCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<CfdiWhatsAppDto>(InvoicingSettings.NotAvailableMessage);

        // Another business's CFDI, or one of an order this person does not see, is not there.
        var cfdi = await CfdiEmails.FindAsync(_context, request.Document, request.CfdiId, cancellationToken);
        if (cfdi is null || !await OrderInvoicing.SeesOrderAsync(_context, CfdiEmails.OrderOf(cfdi), cancellationToken))
            throw new EntityNotFoundException(request.Document.ToString(), request.CfdiId);
        if (cfdi.Status != OrderInvoiceStatus.Stamped)
            return Result.Failure<CfdiWhatsAppDto>(OutgoingMessage.CfdiNotStampedMessage);
        if (cfdi is CreditNote { OrderInvoiceId: null })
            return Result.Failure<CfdiWhatsAppDto>(OutgoingMessage.GlobalCreditNoteMessage);
        if (!await CampaignRules.IsWhatsAppConnectedAsync(_context, cfdi.TenantId, cancellationToken))
            return Result.Failure<CfdiWhatsAppDto>(CfdiWhatsApps.NotConnectedMessage);
        if (!PhoneNumberRules.TryNormalizeTypedPhone(_phones, request.Phone, out var phone))
            return Result.Failure<CfdiWhatsAppDto>(InvoiceWhatsApp.InvalidMessage);

        await _lock.AcquireAsync(cfdi.TenantId, cancellationToken);
        var now = _clock.UtcNow;
        var since = now - CfdiEmails.Day;
        var sent = _context.OutgoingMessages.Where(m => m.Purpose == MessagePurpose.CfdiDocument && m.CreatedAt > since);
        if (await sent.CountAsync(m => m.CfdiId == cfdi.Id, cancellationToken) >= CfdiWhatsApps.MaxPerCfdiPerDay)
            return Result.Failure<CfdiWhatsAppDto>(CfdiWhatsApps.TooManyForCfdiMessage(cfdi.Number));
        if (await sent.CountAsync(cancellationToken) >= CfdiWhatsApps.MaxPerBusinessPerDay)
            return Result.Failure<CfdiWhatsAppDto>(CfdiWhatsApps.TooManyForBusinessMessage);

        OutgoingMessage message;
        try
        {
            message = await CfdiWhatsApps.BuildAsync(_context, cfdi, phone, userId, now, cancellationToken);
            if (request.UseForInvoice)
            {
                var invoice = await _context.OrderInvoices.FirstAsync(i => i.Id == message.OrderInvoiceId, cancellationToken);
                invoice.SendWhatsAppTo(message.Phone);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<CfdiWhatsAppDto>(ex.Message);
        }

        _context.OutgoingMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CfdiWhatsAppDto.From(message));
    }
}

/// <summary>
/// Sends a CFDI's message from the queue (Fase 28M), checked first: a CFDI no longer stamped is not sent. The PDF is the one
/// kept when it was stamped; the message TienditApp asks for with the stamp waits one try for it (the stamp keeps it right
/// after), and otherwise the PAC renders it from the stamped XML, as downloading it does. Without it, the try fails and the
/// queue retries it. The link of its text gets the app's address now.
/// </summary>
public sealed class CfdiWhatsAppSender
{
    public const string NoLongerStampedReason = "El CFDI ya no está vigente: no se envió.";
    public const string PdfPendingReason = "El PDF del CFDI se está guardando.";
    public const string PdfUnavailableReason = "El PAC no dio el PDF del CFDI: envíalo otra vez más tarde.";

    private readonly IApplicationDbContext _context;
    private readonly IPrivateFileStorage _storage;
    private readonly IInvoicingGateway _gateway;
    private readonly IWhatsAppDocumentSender _whatsApp;
    private readonly IAppLinks _links;
    private readonly ILogger<CfdiWhatsAppSender> _logger;

    public CfdiWhatsAppSender(
        IApplicationDbContext context,
        IPrivateFileStorage storage,
        IInvoicingGateway gateway,
        IWhatsAppDocumentSender whatsApp,
        IAppLinks links,
        ILogger<CfdiWhatsAppSender> logger)
    {
        _context = context;
        _storage = storage;
        _gateway = gateway;
        _whatsApp = whatsApp;
        _links = links;
        _logger = logger;
    }

    public async Task<OutgoingSendOutcome> SendAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        var cfdi = message.CfdiDocument is { } document && message.CfdiId is { } cfdiId
            ? await CfdiEmails.FindAsync(_context, document, cfdiId, cancellationToken)
            : null;
        if (cfdi is not { Status: OrderInvoiceStatus.Stamped, XmlFileKey: not null, Uuid: not null } || message.LinkToken is null)
            return new OutgoingSendOutcome(OutgoingSendDecision.Skipped, Reason: NoLongerStampedReason);

        var pdf = cfdi.PdfFileKey is null ? null : await ReadAsync(cfdi.PdfFileKey, cancellationToken);
        if (pdf is null && message.RequestedByUserId is null && message.Attempts == 0)
            // Stamped a moment ago: the stamp keeps its PDF right after, and the next try takes it instead of asking the PAC
            // for it twice.
            return new OutgoingSendOutcome(OutgoingSendDecision.TransientFailure, Reason: PdfPendingReason);
        pdf ??= await RenderPdfAsync(cfdi, cancellationToken);
        if (pdf is null)
            return new OutgoingSendOutcome(OutgoingSendDecision.TransientFailure, Reason: PdfUnavailableReason);

        var result = await _whatsApp.SendDocumentAsync(
            message.TenantId,
            message.Phone,
            new WhatsAppDocument(pdf, "application/pdf", $"{cfdi.Serie}-{cfdi.Folio}_{cfdi.Uuid}.pdf"),
            CfdiWhatsApps.WithAppUrl(message.Body, message.LinkToken, _links.BaseUrl),
            message.ProviderTemplateName,
            message.LanguageCode,
            message.GetParameters().Select(p => CfdiWhatsApps.WithAppUrl(p, message.LinkToken, _links.BaseUrl)).ToArray(),
            cancellationToken);
        if (result.IsSuccess)
            return new OutgoingSendOutcome(OutgoingSendDecision.Sent, result.ProviderMessageId);
        return new OutgoingSendOutcome(
            result.IsTransient ? OutgoingSendDecision.TransientFailure : OutgoingSendDecision.Rejected,
            Reason: result.Error);
    }

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
    private async Task<byte[]?> RenderPdfAsync(Cfdi cfdi, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return null;
        var xml = await ReadAsync(cfdi.XmlFileKey!, cancellationToken);
        if (xml is null)
            return null;
        try
        {
            return await _gateway.RenderPdfAsync(Encoding.UTF8.GetString(xml), cfdi.TenantId, cancellationToken);
        }
        catch (Exception ex) when (ex is InvoicingGatewayUnavailableException or InvoicingGatewayRefusedException)
        {
            _logger.LogWarning("The PAC did not render the PDF of CFDI {CfdiId} for its WhatsApp: {Error}", cfdi.Id, ex.GetType().Name);
            return null;
        }
    }
}
