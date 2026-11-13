using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Quotes;

// The PDF of a quote (Fase 31M, ADR 0093): the business downloads any of its versions, the customer the one of their link,
// and it goes attached to the quote's email and, sent without a template, as the document of its WhatsApp message.

/// <summary>Renders the PDF of a version of a quote. The PDF is TienditApp's own: no library with a license to decide.</summary>
public interface IQuotePdfRenderer
{
    /// <summary>
    /// The PDF of <paramref name="quote"/>, with the logo of its business when it has one that can be read: without it, it
    /// goes without the logo, never late or missing because of it.
    /// </summary>
    Task<byte[]> RenderAsync(QuotePdfModel quote, CancellationToken cancellationToken);
}

/// <summary>
/// What the PDF of a version says, as the customer reads it on its page: of the customer, only their name; of the business,
/// its name and its phone (its contact email starts as its owner's: the page does not show it either). Every amount is the
/// server's; <see cref="Status"/> is the status on the business's day (a sent one whose validity passed reads expired).
/// Since the Fase 31N (ADR 0094), the deposit the version asks to accept it from its link, and whether it was paid.
/// </summary>
public sealed record QuotePdfModel(
    Guid TenantId,
    string BusinessName,
    string? BusinessPhone,
    string Number,
    int Revision,
    QuoteStatus Status,
    string CustomerName,
    DateOnly? SentOn,
    DateOnly? ValidUntil,
    int ValidityDays,
    DateOnly? RepliedOn,
    IReadOnlyList<QuotePdfLine> Lines,
    decimal SubTotal,
    decimal Tax,
    decimal Total,
    decimal VisitCredit,
    decimal ToPay,
    bool PricesIncludeTax,
    string? Note,
    decimal? DepositAmount = null,
    bool DepositPaid = false);

/// <summary>A line of the PDF: what it includes, how many, at how much each and its amount.</summary>
public sealed record QuotePdfLine(string Description, decimal Quantity, decimal UnitPrice, decimal Total);

/// <summary>How a version becomes the model of its PDF, and the PDF's name.</summary>
public static class QuotePdfs
{
    public const string ContentType = "application/pdf";
    public const string NotFoundMessage = "La cotización no existe.";

    /// <summary>«cotizacion-COT-000123.pdf», or «cotizacion-COT-000123-v2.pdf» from its second version on.</summary>
    public static string FileName(string number, int revision)
        => $"cotizacion-{SafeName(number)}{(revision > 1 ? $"-v{revision}" : string.Empty)}.pdf";

    /// <summary>
    /// The model of <paramref name="quote"/> on the business's <paramref name="today"/>: what it takes off for the visit
    /// while its sale still has <paramref name="stillPaidForTheVisit"/> paid, and the day it was sent and answered in the
    /// business's time zone, as its validity.
    /// </summary>
    /// <param name="depositPaid">Whether a payment of its deposit accepted it (<see cref="QuoteDeposits.PaidAsync"/>).</param>
    public static QuotePdfModel Model(
        Quote quote, string businessName, string? businessPhone, string? timeZone, DateOnly today, decimal? stillPaidForTheVisit,
        bool depositPaid = false)
    {
        var status = quote.StatusOn(today);
        var replied = status is QuoteStatus.Accepted or QuoteStatus.Rejected;
        return new QuotePdfModel(
            quote.TenantId,
            businessName,
            string.IsNullOrWhiteSpace(businessPhone) ? null : businessPhone.Trim(),
            quote.Number,
            quote.Revision,
            status,
            quote.CustomerName,
            quote.SentAt is { } sentAt ? QuoteCalendar.Today(timeZone, sentAt) : null,
            quote.ValidUntil,
            quote.ValidityDays,
            replied && quote.ClosedAt is { } closedAt ? QuoteCalendar.Today(timeZone, closedAt) : null,
            quote.Lines.OrderBy(line => line.Position)
                .Select(line => new QuotePdfLine(line.Description, line.Quantity, line.UnitPrice, line.Total))
                .ToList(),
            quote.SubTotal,
            quote.Tax,
            quote.Total,
            quote.VisitCreditStanding(stillPaidForTheVisit),
            quote.ToPay(stillPaidForTheVisit),
            quote.PricesIncludeTax,
            quote.Note,
            quote.DepositAmount,
            depositPaid);
    }

    /// <summary>The model of a version of the business, read as it is now.</summary>
    public static async Task<QuotePdfModel> ModelAsync(
        IApplicationDbContext context, Quote quote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var business = await context.Tenants.AsNoTracking()
            .Where(t => t.Id == quote.TenantId)
            .Select(t => new { t.Name, t.ContactPhone })
            .FirstAsync(cancellationToken);
        var timeZone = (await TenantSalesSettings.ReadAsync(context, quote.TenantId, cancellationToken)).TimeZone;
        var stillPaid = await QuoteVisitCredits.StillPaidAsync(context, quote, cancellationToken);
        return Model(quote, business.Name, business.ContactPhone, timeZone, QuoteCalendar.Today(timeZone, now), stillPaid,
            await QuoteDeposits.PaidAsync(context, quote, cancellationToken));
    }

    /// <summary>How long a message waits for its PDF (its logo comes from the storage) before it goes without it.</summary>
    public static readonly TimeSpan MessageRenderTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The PDF to send with a message, or null when it could not be rendered in <paramref name="timeout"/> (by default
    /// <see cref="MessageRenderTimeout"/>): the message goes without it (with its link), never late or missing because of it.
    /// </summary>
    public static async Task<byte[]?> RenderForMessageAsync(
        IQuotePdfRenderer renderer, IApplicationDbContext context, Quote quote, DateTimeOffset now, ILogger logger,
        CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var model = await ModelAsync(context, quote, now, cancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? MessageRenderTimeout);
        try
        {
            return await renderer.RenderAsync(model, limit.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The PDF of quote {QuoteId} could not be rendered ({Error}): its message goes without it",
                quote.Id, ex.GetType().Name);
            return null;
        }
    }

    // A folio is the business's prefix and digits; anything else a file name should not carry becomes a hyphen.
    private static string SafeName(string number)
        => new(number.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
}

/// <summary>The PDF of a version of a quote of the business, as it is now: any version, a draft too.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record GetQuotePdfQuery(Guid Id) : IRequest<Result<PrivateFileDownload>>;

public class GetQuotePdfQueryValidator : AbstractValidator<GetQuotePdfQuery>
{
    public GetQuotePdfQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class GetQuotePdfQueryHandler : IRequestHandler<GetQuotePdfQuery, Result<PrivateFileDownload>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IQuotePdfRenderer _renderer;
    private readonly IDateTimeProvider _clock;

    public GetQuotePdfQueryHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IQuotePdfRenderer renderer, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _renderer = renderer;
        _clock = clock;
    }

    public async Task<Result<PrivateFileDownload>> Handle(GetQuotePdfQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var quote = await _context.Quotes.AsNoTracking()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.Id && q.TenantId == tenantId, cancellationToken);
        if (quote is null)
            return Result.Failure<PrivateFileDownload>(QuotePdfs.NotFoundMessage);

        var pdf = await _renderer.RenderAsync(await QuotePdfs.ModelAsync(_context, quote, _clock.UtcNow, cancellationToken), cancellationToken);
        return Result.Success(new PrivateFileDownload(
            new MemoryStream(pdf), QuotePdfs.ContentType, QuotePdfs.FileName(quote.Number, quote.Revision)));
    }
}

/// <summary>
/// The PDF of the quote of a link (Fase 31M), as its page shows it: that version only, while its link opens, with only the
/// customer's name. A link unknown, changed, expired or of a business that is not active answers the same (null).
/// </summary>
[AllowAnonymousRequest("El PDF de la cotización del enlace imposible de adivinar que el negocio le mandó al cliente, con lo mismo que muestra su página.")]
public record GetPublicQuotePdfQuery(string Token) : IRequest<PrivateFileDownload?>;

public class GetPublicQuotePdfQueryValidator : AbstractValidator<GetPublicQuotePdfQuery>
{
    public GetPublicQuotePdfQueryValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
    }
}

public class GetPublicQuotePdfQueryHandler : IRequestHandler<GetPublicQuotePdfQuery, PrivateFileDownload?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IQuotePdfRenderer _renderer;
    private readonly IDateTimeProvider _clock;

    public GetPublicQuotePdfQueryHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IQuotePdfRenderer renderer, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _renderer = renderer;
        _clock = clock;
    }

    public async Task<PrivateFileDownload?> Handle(GetPublicQuotePdfQuery request, CancellationToken cancellationToken)
    {
        var found = await PublicQuotes.FindAsync(_context, request.Token, cancellationToken);
        if (found is null)
            return null;

        _tenantService.SetTenant(found.TenantId);
        var quote = await _context.Quotes.AsNoTracking()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == found.QuoteId && q.TenantId == found.TenantId, cancellationToken);
        var business = await PublicQuotes.BusinessAsync(_context, found.TenantId, cancellationToken);
        if (quote is null || business is null)
            return null;

        var now = _clock.UtcNow;
        var today = QuoteCalendar.Today(business.TimeZone, now);
        // The same versions its page opens: sent at some point, until 30 days after its validity.
        if (!quote.LinkOpensOn(today) || quote.SentAt is null)
            return null;

        var stillPaid = await QuoteVisitCredits.StillPaidAsync(_context, quote, cancellationToken);
        var pdf = await _renderer.RenderAsync(
            QuotePdfs.Model(quote, business.Name, business.ContactPhone, business.TimeZone, today, stillPaid,
                await QuoteDeposits.PaidAsync(_context, quote, cancellationToken)),
            cancellationToken);
        return new PrivateFileDownload(new MemoryStream(pdf), QuotePdfs.ContentType, QuotePdfs.FileName(quote.Number, quote.Revision));
    }
}

/// <summary>
/// The quote's WhatsApp message with its PDF (Fase 31M, ADR 0093): sent without a template (to whoever wrote in the last 24
/// hours), the PDF goes as a document with the message's text as its caption. With a template it goes as it did: the
/// business's template may have no document header, and Meta refuses one sent to it.
/// </summary>
public sealed class QuoteWhatsAppDocuments
{
    /// <summary>The longest caption WhatsApp takes for a document; a longer text goes alone.</summary>
    public const int MaxCaptionLength = 1024;

    private readonly IApplicationDbContext _context;
    private readonly IQuotePdfRenderer _renderer;
    private readonly IWhatsAppDocumentSender _sender;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<QuoteWhatsAppDocuments> _logger;

    public QuoteWhatsAppDocuments(
        IApplicationDbContext context,
        IQuotePdfRenderer renderer,
        IWhatsAppDocumentSender sender,
        IDateTimeProvider clock,
        ILogger<QuoteWhatsAppDocuments> logger)
    {
        _context = context;
        _renderer = renderer;
        _sender = sender;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Sends <paramref name="message"/> of <paramref name="quote"/> (read with its lines) with its PDF; null when it does not
    /// go this way (a template, a caption too long, a PDF that could not be rendered or that WhatsApp did not take): then it
    /// goes as before. Once the PDF was taken, how the message went is its outcome: sending the text after it could reach the
    /// customer twice.
    /// </summary>
    public async Task<MessageSendResult?> SendAsync(OutgoingMessage message, Quote quote, CancellationToken cancellationToken)
    {
        if (message.ProviderTemplateName is not null || message.Body.Length > MaxCaptionLength)
            return null;
        var pdf = await QuotePdfs.RenderForMessageAsync(_renderer, _context, quote, _clock.UtcNow, _logger, cancellationToken);
        if (pdf is null)
            return null;

        var sent = await _sender.SendDocumentAsync(
            message.TenantId,
            message.Phone,
            new WhatsAppDocument(pdf, QuotePdfs.ContentType, QuotePdfs.FileName(quote.Number, quote.Revision)),
            message.Body,
            templateName: null,
            message.LanguageCode,
            [],
            cancellationToken);
        if (!sent.NotUploaded)
            return sent;
        // WhatsApp did not take the PDF, so nothing reached the customer: the text goes alone, and only once.
        _logger.LogWarning("The PDF of quote {QuoteId} was not uploaded to WhatsApp: its message goes without it", quote.Id);
        return null;
    }
}
