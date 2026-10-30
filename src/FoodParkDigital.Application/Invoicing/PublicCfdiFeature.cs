using System.Globalization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// What the link of a CFDI's WhatsApp message shows (Fase 28M, ADR 0058), without an account: which CFDI it is, of which
/// business, and whether it still stands. Its XML and PDF are downloaded with the same link.
/// </summary>
public class PublicCfdiDto
{
    public string BusinessName { get; set; } = null!;
    /// <summary>The business's logo (its small variant), if it has one.</summary>
    public string? BusinessLogoUrl { get; set; }
    public CfdiEmailDocument Document { get; set; }
    /// <summary>Serie and folio, «F-12».</summary>
    public string Number { get; set; } = null!;
    /// <summary>The folio fiscal.</summary>
    public string Uuid { get; set; } = null!;
    /// <summary>Its total; for a payment complement, the payment it records.</summary>
    public decimal Amount { get; set; }
    /// <summary>The day it was issued, as the CFDI says it (yyyy-MM-dd).</summary>
    public string? IssuedOn { get; set; }
    public string IssuerName { get; set; } = null!;
    public string IssuerRfc { get; set; } = null!;
    public string ReceiverName { get; set; } = null!;
    public string ReceiverRfc { get; set; } = null!;
    /// <summary>
    /// Whether it still stands with the SAT: a CFDI whose cancellation was asked for, or that was cancelled, is no longer
    /// downloaded from the link.
    /// </summary>
    public bool Stands { get; set; }
    /// <summary>Until when the link works.</summary>
    public DateTimeOffset LinkExpiresAt { get; set; }
}

/// <summary>The CFDI of a WhatsApp link; a link unknown, expired or of a business no longer active is not found.</summary>
[AllowAnonymousRequest("La factura que un negocio mandó por WhatsApp, con el enlace imposible de adivinar de su mensaje.")]
public record GetPublicCfdiQuery(string Token) : IRequest<PublicCfdiDto?>;

public class GetPublicCfdiQueryValidator : AbstractValidator<GetPublicCfdiQuery>
{
    public GetPublicCfdiQueryValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(OutgoingMessage.LinkTokenLength);
    }
}

public class GetPublicCfdiQueryHandler : IRequestHandler<GetPublicCfdiQuery, PublicCfdiDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetPublicCfdiQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<PublicCfdiDto?> Handle(GetPublicCfdiQuery request, CancellationToken cancellationToken)
    {
        if (await PublicCfdiLinks.FindAsync(_context, _tenantService, request.Token, _clock.UtcNow, cancellationToken) is not { } found)
            return null;

        var (cfdi, expiresAt) = found;
        var business = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == cfdi.TenantId)
            .Select(t => new { t.Name, t.LogoUrl })
            .FirstAsync(cancellationToken);
        var parsed = CfdiDocument.FromJson(cfdi.Document);
        return new PublicCfdiDto
        {
            BusinessName = business.Name,
            BusinessLogoUrl = LogoImageUrlsDto.FromBaseUrl(business.LogoUrl)?.Small,
            Document = PublicCfdiLinks.DocumentOf(cfdi),
            Number = cfdi.Number,
            Uuid = cfdi.Uuid!,
            Amount = cfdi switch
            {
                PaymentComplement complement => complement.Amount,
                CfdiInvoice invoice => invoice.Total,
                CreditNote note => note.Total,
                _ => 0m
            },
            IssuedOn = DateTime.TryParseExact(parsed.Date, CfdiDocument.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var issued)
                ? issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null,
            IssuerName = parsed.Issuer.Name,
            IssuerRfc = parsed.Issuer.Rfc,
            ReceiverName = parsed.Receiver.Name,
            ReceiverRfc = parsed.Receiver.Rfc,
            Stands = cfdi.Status == OrderInvoiceStatus.Stamped,
            LinkExpiresAt = expiresAt
        };
    }
}

/// <summary>The XML or the PDF of the CFDI of a WhatsApp link, while it stands: the PDF kept, or the one the PAC renders now.</summary>
[AllowAnonymousRequest("El XML o el PDF de la factura que un negocio mandó por WhatsApp, con el enlace de su mensaje.")]
public record GetPublicCfdiFileQuery(string Token, OrderInvoiceFileKind Kind) : IRequest<Result<PrivateFileDownload>>;

public class GetPublicCfdiFileQueryValidator : AbstractValidator<GetPublicCfdiFileQuery>
{
    public GetPublicCfdiFileQueryValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(OutgoingMessage.LinkTokenLength);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public class GetPublicCfdiFileQueryHandler : IRequestHandler<GetPublicCfdiFileQuery, Result<PrivateFileDownload>>
{
    public const string NotStandingMessage = "Este CFDI ya no está vigente: el negocio pidió cancelarlo. Pídele el que lo sustituye.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPrivateFileStorage _storage;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<GetPublicCfdiFileQueryHandler> _logger;

    public GetPublicCfdiFileQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IPrivateFileStorage storage,
        IInvoicingGateway gateway,
        IDateTimeProvider clock,
        ILogger<GetPublicCfdiFileQueryHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _storage = storage;
        _gateway = gateway;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<PrivateFileDownload>> Handle(GetPublicCfdiFileQuery request, CancellationToken cancellationToken)
    {
        if (await PublicCfdiLinks.FindAsync(_context, _tenantService, request.Token, _clock.UtcNow, cancellationToken) is not { } found)
            return Result.Failure<PrivateFileDownload>(PublicCfdiLinks.NotFoundMessage);
        var cfdi = found.Cfdi;
        if (cfdi.Status != OrderInvoiceStatus.Stamped)
            return Result.Failure<PrivateFileDownload>(NotStandingMessage);

        return await InvoiceFiles.DownloadAsync(
            _storage, _gateway, _logger, cfdi.Id, $"{cfdi.Serie}-{cfdi.Folio}_{cfdi.Uuid}", cfdi.XmlFileKey!, cfdi.PdfFileKey,
            request.Kind, cfdi.TenantId, cancellationToken);
    }
}

/// <summary>Finding the CFDI of a WhatsApp link (Fase 28M), across businesses and then inside its own.</summary>
public static class PublicCfdiLinks
{
    public const string NotFoundMessage = "No encontramos esa factura. El enlace pudo haber vencido: pídesela otra vez al negocio.";

    /// <summary>
    /// The CFDI of the link and until when the link works, or null: a link of another shape, unknown, expired, of a business
    /// no longer active, or whose CFDI never got its stamp.
    /// </summary>
    public static async Task<(Cfdi Cfdi, DateTimeOffset ExpiresAt)?> FindAsync(
        IApplicationDbContext context, ICurrentTenantService tenantService, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!Order.IsTrackingToken(token))
            return null;

        // The link names one message of the whole platform before any business is in context: only its business, its CFDI
        // and when it was queued are read here; the CFDI itself is read inside that business.
        var found = await (
                from message in context.OutgoingMessages.IgnoreQueryFilters().AsNoTracking()
                join tenant in context.Tenants.AsNoTracking() on message.TenantId equals tenant.Id
                where message.LinkToken == token && tenant.Status == TenantStatus.Active
                select new { message.TenantId, message.LinkToken, message.CreatedAt, message.CfdiDocument, message.CfdiId })
            .FirstOrDefaultAsync(cancellationToken);
        // The column compares without case, as the database does; the link is the exact one.
        if (found is not { CfdiDocument: { } document, CfdiId: { } cfdiId }
            || !string.Equals(found.LinkToken, token, StringComparison.Ordinal))
            return null;
        var expiresAt = found.CreatedAt + OutgoingMessage.CfdiLinkLifetime;
        if (expiresAt <= now)
            return null;

        tenantService.SetTenant(found.TenantId);
        var cfdi = await CfdiEmails.FindAsync(context, document, cfdiId, cancellationToken);
        return cfdi is { Uuid: not null, XmlFileKey: not null } ? (cfdi, expiresAt) : null;
    }

    public static CfdiEmailDocument DocumentOf(Cfdi cfdi) => cfdi switch
    {
        PaymentComplement => CfdiEmailDocument.PaymentComplement,
        CreditNote => CfdiEmailDocument.CreditNote,
        _ => CfdiEmailDocument.Invoice
    };
}
