using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// The CFDI that stayed «Timbrando» past the PAC's window (Fase 28K, ADR 0056). SW Sapien gives back the stamp of a repeated
/// key for 72 hours from its first use, and TienditApp asks for it until 71; after that only a person can tell whether it was
/// stamped: the PAC has no search without the folio fiscal, and its account is the platform's, so the business looks in the
/// SAT's portal (Consultar facturas emitidas), where every stamped CFDI of its RFC is. «Volver a pedir el timbre» is no
/// longer offered: the CFDI's date is too old to stamp, and a new one could make a second CFDI of the same sale.
/// </summary>
public static class StuckStamps
{
    public const string PastWindowMessage =
        "Se pidió hace más de 71 horas y el PAC ya no devuelve su timbre: revisa en el portal del SAT si se timbró, desde Reportes › CFDI emitidos.";
    public const string NotStuckMessage = "Ya no está «Timbrando»: no hay nada que revisar.";
    public const string InWindowMessage =
        "TienditApp todavía le pide su timbre al PAC: lo hace sola hasta 71 horas después de pedirlo.";
    public const string UuidMessage = "Escribe el folio fiscal (UUID) completo, como aparece en el portal del SAT.";
    public const string UuidNotFoundMessage =
        "El PAC de TienditApp no tiene ese folio fiscal: revisa que lo copiaste completo.";
    public const string SatMayNotShowItYetMessage =
        "El SAT tarda hasta 72 horas en mostrar un CFDI timbrado: di que no se timbró cuando pasen, si para entonces no aparece en su portal.";
    public const string NotStampedReason = "No se timbró: se revisó en el portal del SAT.";

    /// <summary>
    /// How long the SAT may take to show a CFDI the PAC stamped (as <see cref="OrderInvoiceSubstitutions.CancellationRetryWindow"/>):
    /// a stamp made in TienditApp's last request shows in the SAT's portal by then.
    /// </summary>
    public static readonly TimeSpan SatReceptionWindow = TimeSpan.FromHours(72);

    /// <summary>The folio fiscal of a stamp a person found: 32 hexadecimal digits in groups of 8, 4, 4, 4 and 12.</summary>
    public static readonly Regex UuidPattern = new(
        "^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$", RegexOptions.CultureInvariant);

    public static string OtherCfdiMessage(string number)
        => $"Ese folio fiscal es de otro comprobante, no de {number}: revisa su serie y folio en el portal del SAT.";

    /// <summary>Until when TienditApp asks the PAC for the CFDI's stamp by itself.</summary>
    public static DateTimeOffset WindowEndsAt(DateTimeOffset requestedAt)
        => requestedAt + StampPendingInvoicesCommandHandler.RetryWindow;

    /// <summary>
    /// From when a person may say a stuck CFDI was not stamped: any stamp of its requests shows in the SAT's portal by then,
    /// so not finding it there means there is none.
    /// </summary>
    public static DateTimeOffset NotStampedFrom(DateTimeOffset requestedAt) => WindowEndsAt(requestedAt) + SatReceptionWindow;

    /// <summary>A CFDI still «Timbrando» whose stamp the PAC no longer gives back: a person checks it in the SAT's portal.</summary>
    public static bool IsStuck(OrderInvoiceStatus status, DateTimeOffset requestedAt, DateTimeOffset now)
        => status == OrderInvoiceStatus.Stamping && now > WindowEndsAt(requestedAt);

    /// <summary>
    /// What tells the stamped XML apart from the CFDI TienditApp asked for, or null when it is the same one: its serie and
    /// folio, its date (to the second it was asked for: two businesses of the same RFC have their own folios), its type, its
    /// total, and the RFC of its issuer and of its receiver.
    /// </summary>
    public static string? Mismatch(CfdiDocument document, string stampedXml)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(stampedXml).Root ?? throw new FormatException();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or FormatException)
        {
            return "XML";
        }

        string? Attribute(XElement? element, string name) => element?.Attribute(name)?.Value.Trim();
        XElement? Child(string name) => root.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        var total = decimal.TryParse(Attribute(root, "Total"), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : (decimal?)null;

        if (root.Name.LocalName != "Comprobante")
            return "Comprobante";
        if (!string.Equals(Attribute(root, "Serie"), document.Serie, StringComparison.OrdinalIgnoreCase))
            return "Serie";
        if (Attribute(root, "Folio") != document.Folio)
            return "Folio";
        if (Attribute(root, "Fecha") != document.Date)
            return "Fecha";
        if (Attribute(root, "TipoDeComprobante") != document.VoucherType)
            return "TipoDeComprobante";
        if (total != document.Total)
            return "Total";
        if (!string.Equals(Attribute(Child("Emisor"), "Rfc"), document.Issuer.Rfc, StringComparison.OrdinalIgnoreCase))
            return "Emisor";
        if (!string.Equals(Attribute(Child("Receptor"), "Rfc"), document.Receiver.Rfc, StringComparison.OrdinalIgnoreCase))
            return "Receptor";
        return null;
    }
}

/// <summary>
/// Every CFDI of the business that stayed «Timbrando» past the PAC's window, the oldest first, whatever their month: what
/// Reportes › CFDI emitidos asks to check in the SAT's portal (Fase 28K).
/// </summary>
[RequiresTenantManager]
public record GetCfdisToReviewQuery : IRequest<List<IssuedCfdiDto>>;

public class GetCfdisToReviewQueryHandler : IRequestHandler<GetCfdisToReviewQuery, List<IssuedCfdiDto>>
{
    /// <summary>More than this many is not a list a person goes through: the oldest are shown first.</summary>
    public const int MaxShown = 100;

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public GetCfdisToReviewQueryHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<List<IssuedCfdiDto>> Handle(GetCfdisToReviewQuery request, CancellationToken cancellationToken)
    {
        // The date a CFDI carries is the one it was asked for with, so the window is measured on it.
        var since = new DateTimeOffset(IssuedCfdis.FirstYear, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var until = _clock.UtcNow - StampPendingInvoicesCommandHandler.RetryWindow;
        var span = new IssuedCfdiMonth(DateOnly.FromDateTime(since.UtcDateTime), DateOnly.FromDateTime(until.UtcDateTime), since, until,
            TimeZoneInfo.Utc);
        var rows = await IssuedCfdis.Rows(_context, span, null, OrderInvoiceStatus.Stamping)
            .OrderBy(r => r.IssuedAt)
            .ThenBy(r => r.Id)
            .Take(MaxShown)
            .ToListAsync(cancellationToken);
        return rows.Select(IssuedCfdis.ToDto).ToList();
    }
}

/// <summary>
/// The owner or an administrator found in the SAT's portal the stamp of a CFDI that stayed «Timbrando» past the window, and
/// gives its folio fiscal (Fase 28K): TienditApp brings its XML from the PAC, checks that it is this CFDI (serie, folio,
/// type, total, issuer and receiver) and keeps it as when the PAC answers. A stamp of another CFDI, or one the PAC does not
/// have, changes nothing.
/// </summary>
[RequiresTenantManager]
public record ConfirmStuckCfdiStampedCommand(IssuedCfdiKind Kind, Guid CfdiId, string Uuid) : IRequest<Result<IssuedCfdiDto>>;

public class ConfirmStuckCfdiStampedCommandValidator : AbstractValidator<ConfirmStuckCfdiStampedCommand>
{
    public ConfirmStuckCfdiStampedCommandValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Uuid).NotEmpty().WithMessage(StuckStamps.UuidMessage)
            .Must(uuid => StuckStamps.UuidPattern.IsMatch(uuid?.Trim() ?? string.Empty)).WithMessage(StuckStamps.UuidMessage);
    }
}

public class ConfirmStuckCfdiStampedCommandHandler : IRequestHandler<ConfirmStuckCfdiStampedCommand, Result<IssuedCfdiDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly OrderInvoiceStamper _stamper;
    private readonly ReplacedInvoiceCanceller _canceller;
    private readonly ILogger<OrderInvoiceStamper> _logger;

    public ConfirmStuckCfdiStampedCommandHandler(
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _context = context;
        _gateway = gateway;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
        _canceller = new ReplacedInvoiceCanceller(context, gateway, clock);
    }

    public async Task<Result<IssuedCfdiDto>> Handle(ConfirmStuckCfdiStampedCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<IssuedCfdiDto>(InvoicingSettings.NotAvailableMessage);

        // As when the PAC answers: the CFDI's row version keeps a second answer (or person) from writing over the first.
        return request.Kind switch
        {
            IssuedCfdiKind.OrderInvoice => await ConfirmAsync(_context.OrderInvoices, request, cancellationToken),
            IssuedCfdiKind.GlobalInvoice => await ConfirmAsync(GlobalInvoicing.WithSales(_context), request, cancellationToken),
            IssuedCfdiKind.PaymentComplement => await ConfirmAsync(_context.PaymentComplements, request, cancellationToken),
            _ => await ConfirmAsync(_context.CreditNotes, request, cancellationToken)
        };
    }

    private async Task<Result<IssuedCfdiDto>> ConfirmAsync<TInvoice>(
        IQueryable<TInvoice> invoices, ConfirmStuckCfdiStampedCommand request, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var invoice = await invoices.FirstOrDefaultAsync(i => i.Id == request.CfdiId, cancellationToken)
            ?? throw new EntityNotFoundException(typeof(TInvoice).Name, request.CfdiId);
        if (StuckCfdis.Refusal(invoice, _clock.UtcNow) is { } refusal)
            return Result.Failure<IssuedCfdiDto>(refusal);

        var uuid = request.Uuid.Trim().ToUpperInvariant();
        StampedCfdi? stamped;
        try
        {
            stamped = await _gateway.FindStampedAsync(uuid, cancellationToken);
        }
        catch (Exception ex) when (ex is InvoicingGatewayUnavailableException or InvoicingGatewayRefusedException)
        {
            return Result.Failure<IssuedCfdiDto>(ex.Message);
        }
        if (stamped is null)
            return Result.Failure<IssuedCfdiDto>(StuckStamps.UuidNotFoundMessage);
        if (StuckStamps.Mismatch(CfdiDocument.FromJson(invoice.Document), stamped.Xml) is { } differs)
        {
            _logger.LogWarning("The stamp given for CFDI {CfdiId} is of another CFDI ({Differs})", invoice.Id, differs);
            return Result.Failure<IssuedCfdiDto>(StuckStamps.OtherCfdiMessage(invoice.Number));
        }

        invoice = await _stamper.KeepStampAsync(invoices, invoice, stamped, cancellationToken);
        // Someone else said meanwhile that it was not stamped: what they wrote stands, and this person is told.
        if (invoice.Status != OrderInvoiceStatus.Stamped || invoice.Uuid != stamped.Uuid)
            return Result.Failure<IssuedCfdiDto>(StuckStamps.NotStuckMessage);

        _logger.LogInformation("A person brought back from the SAT's portal the stamp of CFDI {CfdiId}", invoice.Id);
        await StuckCfdis.AuditAsync(_context, _currentUser, invoice, "CfdiStampConfirmed", cancellationToken);
        if (invoice is OrderInvoice replacement)
            await CancelReplacedAsync(replacement, cancellationToken);
        return Result.Success(await StuckCfdis.DtoAsync(_context, request.Kind, invoice, cancellationToken));
    }

    /// <summary>
    /// A replacement (Fase 28G) stamped this way goes on to the cancellation (01) of the invoice it replaces, as when its
    /// stamp is asked for again: its stamp is older than the 72 hours the worker keeps asking for it.
    /// </summary>
    private async Task CancelReplacedAsync(OrderInvoice replacement, CancellationToken cancellationToken)
    {
        var replacedId = await _context.OrderInvoices.AsNoTracking()
            .Where(i => i.ReplacedByInvoiceId == replacement.Id && i.Status == OrderInvoiceStatus.Stamped)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (replacedId is null)
            return;
        // When the SAT does not take it, the replaced one keeps the reason and «Cancelar con relación» asks again.
        var userId = Guid.TryParse(_currentUser.UserId, out var current) ? current : replacement.RequestedByUserId;
        await _canceller.CancelAsync(replacedId.Value, userId, _currentUser.UserName ?? "invoicing", cancellationToken);
    }
}

/// <summary>
/// The owner or an administrator checked in the SAT's portal that a CFDI that stayed «Timbrando» past the window was not
/// stamped (Fase 28K): it becomes «Rechazada», with who checked it, and what it covered can be invoiced again. A refused
/// replacement gives the order back the invoice it was to replace, as when the PAC refuses it (Fase 28G).
/// </summary>
[RequiresTenantManager]
public record ConfirmStuckCfdiNotStampedCommand(IssuedCfdiKind Kind, Guid CfdiId) : IRequest<Result<IssuedCfdiDto>>;

public class ConfirmStuckCfdiNotStampedCommandValidator : AbstractValidator<ConfirmStuckCfdiNotStampedCommand>
{
    public ConfirmStuckCfdiNotStampedCommandValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public class ConfirmStuckCfdiNotStampedCommandHandler : IRequestHandler<ConfirmStuckCfdiNotStampedCommand, Result<IssuedCfdiDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly OrderInvoiceStamper _stamper;
    private readonly ILogger<OrderInvoiceStamper> _logger;

    public ConfirmStuckCfdiNotStampedCommandHandler(
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
    }

    public async Task<Result<IssuedCfdiDto>> Handle(ConfirmStuckCfdiNotStampedCommand request, CancellationToken cancellationToken)
        => request.Kind switch
        {
            IssuedCfdiKind.OrderInvoice => await RefuseAsync(_context.OrderInvoices, request, cancellationToken),
            IssuedCfdiKind.GlobalInvoice => await RefuseAsync(GlobalInvoicing.WithSales(_context), request, cancellationToken),
            IssuedCfdiKind.PaymentComplement => await RefuseAsync(_context.PaymentComplements, request, cancellationToken),
            _ => await RefuseAsync(_context.CreditNotes, request, cancellationToken)
        };

    private async Task<Result<IssuedCfdiDto>> RefuseAsync<TInvoice>(
        IQueryable<TInvoice> invoices, ConfirmStuckCfdiNotStampedCommand request, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var invoice = await invoices.FirstOrDefaultAsync(i => i.Id == request.CfdiId, cancellationToken)
            ?? throw new EntityNotFoundException(typeof(TInvoice).Name, request.CfdiId);
        var now = _clock.UtcNow;
        if (StuckCfdis.Refusal(invoice, now) is { } refusal)
            return Result.Failure<IssuedCfdiDto>(refusal);
        // Not finding it in the SAT's portal says nothing until the SAT shows any stamp the PAC made.
        if (now < StuckStamps.NotStampedFrom(invoice.RequestedAt))
            return Result.Failure<IssuedCfdiDto>(StuckStamps.SatMayNotShowItYetMessage);

        invoice = await _stamper.RefuseAsync(invoices, invoice, StuckStamps.NotStampedReason, cancellationToken);
        // Someone else brought its stamp back meanwhile: that stands, and this person is told.
        if (invoice.Status != OrderInvoiceStatus.Refused || invoice.FailureMessage != StuckStamps.NotStampedReason)
            return Result.Failure<IssuedCfdiDto>(StuckStamps.NotStuckMessage);

        _logger.LogInformation("A person checked in the SAT's portal that CFDI {CfdiId} was not stamped", invoice.Id);
        await StuckCfdis.AuditAsync(_context, _currentUser, invoice, "CfdiNotStampedConfirmed", cancellationToken);
        return Result.Success(await StuckCfdis.DtoAsync(_context, request.Kind, invoice, cancellationToken));
    }
}

internal static class StuckCfdis
{
    /// <summary>Why a CFDI cannot be resolved from the SAT's portal now, or null when it can.</summary>
    public static string? Refusal(Cfdi invoice, DateTimeOffset now)
        => invoice.Status != OrderInvoiceStatus.Stamping ? StuckStamps.NotStuckMessage
            : !StuckStamps.IsStuck(invoice.Status, invoice.RequestedAt, now) ? StuckStamps.InWindowMessage
            : null;

    /// <summary>Who resolved it and how: a fiscal decision a person made, kept in the business's audit.</summary>
    public static async Task AuditAsync(
        IApplicationDbContext context, ICurrentUserService currentUser, Cfdi invoice, string action, CancellationToken cancellationToken)
    {
        context.AuditLogs.Add(new AuditLog(
            tenantId: invoice.TenantId,
            entityName: invoice.GetType().Name,
            entityId: invoice.Id.ToString(),
            action: action,
            performedBy: currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new { invoice.Number, invoice.Uuid })));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The CFDI as Reportes › CFDI emitidos shows it, after it was resolved.</summary>
    public static async Task<IssuedCfdiDto> DtoAsync(
        IApplicationDbContext context, IssuedCfdiKind kind, Cfdi invoice, CancellationToken cancellationToken)
    {
        var day = invoice.IssuedAt;
        var span = new IssuedCfdiMonth(DateOnly.FromDateTime(day.UtcDateTime), DateOnly.FromDateTime(day.UtcDateTime), day,
            day.AddTicks(1), TimeZoneInfo.Utc);
        var row = await IssuedCfdis.Rows(context, span, kind, null).FirstAsync(r => r.Id == invoice.Id, cancellationToken);
        return IssuedCfdis.ToDto(row);
    }
}
