using System.Globalization;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>A global invoice, as Reportes › Factura global lists it (Fase 28B, ADR 0046).</summary>
public class GlobalInvoiceDto
{
    public Guid Id { get; set; }
    /// <summary>Serie and folio, «F-12».</summary>
    public string Number { get; set; } = null!;
    public OrderInvoiceStatus Status { get; set; }
    public GlobalInvoicePeriodicity Periodicity { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public string Months { get; set; } = null!;
    public int Year { get; set; }
    public int OrderCount { get; set; }
    public string PaymentForm { get; set; } = null!;
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>The folio fiscal, once stamped.</summary>
    public string? Uuid { get; set; }
    public DateTimeOffset? StampedAt { get; set; }
    public bool HasXml { get; set; }
    public bool HasPdf { get; set; }
    /// <summary>Why the PAC refused it, or why the SAT did not cancel it.</summary>
    public string? FailureMessage { get; set; }
    public DateTimeOffset? RefusedAt { get; set; }
    public CfdiCancellationMotive? CancellationMotive { get; set; }
    public DateTimeOffset? CancellationRequestedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public static GlobalInvoiceDto From(GlobalInvoice invoice) => Fill(new GlobalInvoiceDto(), invoice);

    protected static TDto Fill<TDto>(TDto dto, GlobalInvoice invoice) where TDto : GlobalInvoiceDto
    {
        dto.Id = invoice.Id;
        dto.Number = invoice.Number;
        dto.Status = invoice.Status;
        dto.Periodicity = invoice.Periodicity;
        dto.PeriodFrom = invoice.PeriodFrom;
        dto.PeriodTo = invoice.PeriodTo;
        dto.Months = invoice.Months;
        dto.Year = invoice.Year;
        dto.OrderCount = invoice.OrderCount;
        dto.PaymentForm = invoice.PaymentForm;
        dto.SubTotal = invoice.SubTotal;
        dto.TaxTotal = invoice.TaxTotal;
        dto.Total = invoice.Total;
        dto.RequestedAt = invoice.RequestedAt;
        dto.Uuid = invoice.Uuid;
        dto.StampedAt = invoice.StampedAt;
        dto.HasXml = invoice.XmlFileKey is not null;
        dto.HasPdf = invoice.PdfFileKey is not null;
        dto.FailureMessage = invoice.FailureMessage;
        dto.RefusedAt = invoice.RefusedAt;
        dto.CancellationMotive = invoice.CancellationMotive;
        dto.CancellationRequestedAt = invoice.CancellationRequestedAt;
        dto.CancelledAt = invoice.CancelledAt;
        return dto;
    }
}

/// <summary>A global invoice with the sales it covers.</summary>
public class GlobalInvoiceDetailDto : GlobalInvoiceDto
{
    public List<GlobalInvoiceOrderDto> Orders { get; set; } = [];

    /// <param name="creditNotes">The global invoice's credit notes (Fase 28I), whatever their status.</param>
    public static GlobalInvoiceDetailDto WithSales(GlobalInvoice invoice, IReadOnlyCollection<CreditNote> creditNotes)
    {
        var detail = Fill(new GlobalInvoiceDetailDto(), invoice);
        detail.Orders = invoice.Orders
            .OrderBy(order => order.OrderNumber)
            .Select(order => new GlobalInvoiceOrderDto
            {
                OrderId = order.OrderId,
                OrderNumber = order.OrderNumber,
                Total = order.Total,
                CreditNotes = creditNotes
                    .Where(n => n.OrderId == order.OrderId)
                    .OrderByDescending(n => n.RequestedAt)
                    .ThenByDescending(n => n.Folio)
                    .Select(n =>
                    {
                        var dto = CreditNoteDto.From(n);
                        dto.GlobalInvoiceNumber = invoice.Number;
                        return dto;
                    })
                    .ToList()
            })
            .ToList();
        return detail;
    }
}

/// <summary>A sale a global invoice covers.</summary>
public class GlobalInvoiceOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public decimal Total { get; set; }
    /// <summary>
    /// The credit notes of its refunds (Fase 28I) and write-offs (Fase 28J) that credit this global invoice, the newest first:
    /// while one stands with the SAT, the global invoice is not cancelled.
    /// </summary>
    public List<CreditNoteDto> CreditNotes { get; set; } = [];
}

/// <summary>
/// The global invoice of the period of that periodicity that holds <see cref="Day"/>, before it is stamped (owners and
/// administrators): its sales, the ones left out and why, its amounts and what stops it.
/// </summary>
[RequiresTenantManager]
public record GetGlobalInvoiceDraftQuery(GlobalInvoicePeriodicity Periodicity, DateOnly Day) : IRequest<GlobalInvoiceDraftDto>;

public class GetGlobalInvoiceDraftQueryValidator : AbstractValidator<GetGlobalInvoiceDraftQuery>
{
    public GetGlobalInvoiceDraftQueryValidator()
    {
        RuleFor(x => x.Periodicity).IsInEnum().WithMessage(GlobalInvoicing.PeriodicityMessage);
        RuleFor(x => x.Day).Must(GlobalInvoicing.IsReasonableDay).WithMessage(GlobalInvoicing.DayMessage);
    }
}

public class GetGlobalInvoiceDraftQueryHandler : IRequestHandler<GetGlobalInvoiceDraftQuery, GlobalInvoiceDraftDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public GetGlobalInvoiceDraftQueryHandler(
        IApplicationDbContext context, ICurrentTenantService tenant, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<GlobalInvoiceDraftDto> Handle(GetGlobalInvoiceDraftQuery request, CancellationToken cancellationToken)
        => (await GlobalInvoiceDrafts.BuildAsync(
            _context, _tenant.TenantId, _gateway, request.Periodicity, request.Day, _clock.UtcNow, cancellationToken)).Dto;
}

/// <summary>
/// Stamps the global invoice of a period that is over (owners and administrators): its folio, its CFDI and its sales are
/// saved first (<see cref="RequestGlobalInvoiceCommand"/>), and the PAC is asked after the commit. The preview the person
/// reviewed is what is stamped: if the period's sales changed since (which ones, their number or their total), nothing is
/// stamped.
/// </summary>
[RequiresTenantManager]
public record IssueGlobalInvoiceCommand(
    GlobalInvoicePeriodicity Periodicity,
    DateOnly Day,
    int ExpectedSaleCount,
    decimal ExpectedTotal,
    string ExpectedFingerprint) : IRequest<Result<GlobalInvoiceDto>>;

public class IssueGlobalInvoiceCommandValidator : AbstractValidator<IssueGlobalInvoiceCommand>
{
    public IssueGlobalInvoiceCommandValidator()
    {
        RuleFor(x => x.Periodicity).IsInEnum().WithMessage(GlobalInvoicing.PeriodicityMessage);
        RuleFor(x => x.Day).Must(GlobalInvoicing.IsReasonableDay).WithMessage(GlobalInvoicing.DayMessage);
        RuleFor(x => x.ExpectedSaleCount).GreaterThan(0);
        RuleFor(x => x.ExpectedTotal).GreaterThan(0);
        RuleFor(x => x.ExpectedFingerprint).NotEmpty().MaximumLength(GlobalInvoicing.FingerprintLength);
    }
}

public class IssueGlobalInvoiceCommandHandler : IRequestHandler<IssueGlobalInvoiceCommand, Result<GlobalInvoiceDto>>
{
    private readonly ISender _sender;
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

    public IssueGlobalInvoiceCommandHandler(
        ISender sender,
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _sender = sender;
        _context = context;
        _gateway = gateway;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
    }

    public async Task<Result<GlobalInvoiceDto>> Handle(IssueGlobalInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<GlobalInvoiceDto>(InvoicingSettings.NotAvailableMessage);

        var requested = await _sender.Send(
            new RequestGlobalInvoiceCommand(
                request.Periodicity, request.Day, request.ExpectedSaleCount, request.ExpectedTotal, request.ExpectedFingerprint),
            cancellationToken);
        if (requested.IsFailure)
            return Result.Failure<GlobalInvoiceDto>(requested.Error!);

        var invoice = await _stamper.StampAsync(GlobalInvoicing.WithSales(_context), requested.Value, cancellationToken);
        return Result.Success(GlobalInvoiceDto.From(invoice));
    }
}

/// <summary>
/// The global invoice is fixed before the PAC is asked (sent by <see cref="IssueGlobalInvoiceCommand"/>): its folio, its
/// date, its key, the CFDI built from the period's sales and the sales it takes, in one transaction that holds the
/// business's invoicing lock, so no invoice of an order takes one of them meanwhile. Answers the new invoice's id.
/// </summary>
[RequiresTenantManager]
public record RequestGlobalInvoiceCommand(
    GlobalInvoicePeriodicity Periodicity,
    DateOnly Day,
    int ExpectedSaleCount,
    decimal ExpectedTotal,
    string ExpectedFingerprint) : IRequest<Result<Guid>>, ITransactionalRequest;

public class RequestGlobalInvoiceCommandHandler : IRequestHandler<RequestGlobalInvoiceCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IInvoicingLock _lock;
    private readonly IFolioGenerator _folios;
    private readonly IDateTimeProvider _clock;

    public RequestGlobalInvoiceCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IInvoicingGateway gateway,
        IInvoicingLock invoicingLock,
        IFolioGenerator folios,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _gateway = gateway;
        _lock = invoicingLock;
        _folios = folios;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(RequestGlobalInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<Guid>(InvoicingSettings.NotAvailableMessage);

        var tenantId = _tenant.TenantId;
        // From here on no invoice of an order of this business is asked for until this one commits: what the draft leaves
        // out (sales with their own invoice) and takes stays true.
        await _lock.AcquireAsync(tenantId, cancellationToken);
        var now = _clock.UtcNow;
        var draft = await GlobalInvoiceDrafts.BuildAsync(
            _context, tenantId, _gateway, request.Periodicity, request.Day, now, cancellationToken);
        var dto = draft.Dto;
        if (dto.Problem is not null)
            return Result.Failure<Guid>(dto.Problem);
        if (dto.Blockers.Count > 0)
            return Result.Failure<Guid>(dto.Blockers[0]);
        if (dto.SaleCount != request.ExpectedSaleCount
            || dto.Total != request.ExpectedTotal
            || !string.Equals(dto.Fingerprint, request.ExpectedFingerprint, StringComparison.Ordinal))
            return Result.Failure<Guid>(GlobalInvoicing.SalesChangedMessage);

        var timeZone = await _context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstAsync(cancellationToken);
        var serie = profile.Serie;
        var folio = await _folios.NextInvoiceFolioAsync(tenantId, serie, cancellationToken);
        var document = draft.Document(serie, folio, TenantDateTimeResolver.LocalNow(now, timeZone));
        var invoice = GlobalInvoice.Request(
            tenantId, draft.Period, serie, folio, dto.PaymentForm!, document.ToJson(),
            dto.SubTotal, dto.TotalTransferred, dto.Total, draft.Sales, userId, now);
        _context.GlobalInvoices.Add(invoice);
        _context.AuditLogs.Add(new AuditLog(
            tenantId: tenantId,
            entityName: nameof(GlobalInvoice),
            entityId: invoice.Id.ToString(),
            action: "GlobalInvoiceRequested",
            performedBy: _currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new
            {
                invoice.Number,
                Periodicity = draft.Period.PeriodicityCode,
                From = draft.Period.From,
                To = draft.Period.To,
                invoice.OrderCount,
                invoice.Total
            })));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another global invoice took one of these sales at the same time (the index of one live global invoice per sale
            // kept it): this one is undone whole, folio included. Anything else is thrown on.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            var saleIds = draft.Sales.Select(sale => sale.OrderId).ToList();
            if (await _context.GlobalInvoiceOrders.AsNoTracking().AnyAsync(o => !o.Released && saleIds.Contains(o.OrderId), cancellationToken))
                return Result.Failure<Guid>(GlobalInvoicing.SalesChangedMessage);
            throw;
        }

        return Result.Success(invoice.Id);
    }
}

/// <summary>
/// Asks the PAC again for a global invoice that is still «Timbrando», with the same CFDI and key: it never stamps a second
/// one.
/// </summary>
[RequiresTenantManager]
public record RetryGlobalInvoiceCommand(Guid InvoiceId) : IRequest<Result<GlobalInvoiceDto>>;

public class RetryGlobalInvoiceCommandHandler : IRequestHandler<RetryGlobalInvoiceCommand, Result<GlobalInvoiceDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

    public RetryGlobalInvoiceCommandHandler(
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _context = context;
        _gateway = gateway;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
    }

    public async Task<Result<GlobalInvoiceDto>> Handle(RetryGlobalInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!await _context.GlobalInvoices.AnyAsync(i => i.Id == request.InvoiceId, cancellationToken))
            throw new EntityNotFoundException(nameof(GlobalInvoice), request.InvoiceId);
        if (!_gateway.IsConfigured)
            return Result.Failure<GlobalInvoiceDto>(InvoicingSettings.NotAvailableMessage);

        var invoice = await _stamper.StampAsync(GlobalInvoicing.WithSales(_context), request.InvoiceId, cancellationToken);
        return Result.Success(GlobalInvoiceDto.From(invoice));
    }
}

/// <summary>
/// The owner or an administrator cancels a stamped global invoice with the SAT: because it has errors and nothing replaces
/// it (02), or because a customer asked for the invoice of one of its sales (04: then the sale is invoiced to the customer
/// and the period's global invoice is issued again without it). The general public accepts nothing, so the SAT cancels it
/// at once; its sales are free again only once it does.
/// </summary>
[RequiresTenantManager]
public record CancelGlobalInvoiceCommand(Guid InvoiceId, CfdiCancellationMotive Motive) : IRequest<Result<GlobalInvoiceDto>>;

public class CancelGlobalInvoiceCommandValidator : AbstractValidator<CancelGlobalInvoiceCommand>
{
    public CancelGlobalInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Motive)
            .Must(motive => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NominativeInGlobalInvoice)
            .WithMessage(GlobalInvoicing.CancellationMotivesMessage);
    }
}

public class CancelGlobalInvoiceCommandHandler : IRequestHandler<CancelGlobalInvoiceCommand, Result<GlobalInvoiceDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public CancelGlobalInvoiceCommandHandler(
        IApplicationDbContext context, ICurrentUserService currentUser, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<GlobalInvoiceDto>> Handle(CancelGlobalInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _context.GlobalInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(GlobalInvoice), request.InvoiceId);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<GlobalInvoiceDto>(InvoicingSettings.NotAvailableMessage);
        // Before the SAT is asked: once it cancels, the motive is no longer ours to refuse.
        if (!invoice.TakesCancellationMotive(request.Motive))
            return Result.Failure<GlobalInvoiceDto>(GlobalInvoicing.CancellationMotivesMessage);
        if (invoice.Status == OrderInvoiceStatus.Cancelling && invoice.CancellationMotive == request.Motive)
            return Result.Success(GlobalInvoiceDto.From(invoice));
        if (invoice.Status != OrderInvoiceStatus.Stamped)
            return Result.Failure<GlobalInvoiceDto>(invoice.Status == OrderInvoiceStatus.Cancelled
                ? "Esta factura ya está cancelada."
                : "Solo se cancela una factura timbrada.");
        if (!_gateway.IsConfigured)
            return Result.Failure<GlobalInvoiceDto>(InvoicingSettings.NotAvailableMessage);
        // The SAT does not cancel one related to credit notes in force: those of its sales' refunds and write-offs go first
        // (Fases 28I and 28J).
        if (await CreditNotes.LiveNumbersOfGlobalInvoiceAsync(_context, invoice.Id, cancellationToken) is { Count: > 0 } notes)
            return Result.Failure<GlobalInvoiceDto>(GlobalInvoicing.LiveCreditNotesMessage(notes));
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is not { HasCertificate: true })
            return Result.Failure<GlobalInvoiceDto>(CancelOrderInvoiceCommandHandler.NoCertificateToCancelMessage);

        var issuerRfc = CfdiDocument.FromJson(invoice.Document).Issuer.Rfc;
        CfdiCancellationOutcome outcome;
        try
        {
            outcome = await _gateway.CancelAsync(issuerRfc, invoice.Uuid!, request.Motive, null, cancellationToken);
        }
        catch (Exception ex) when (ex is InvoicingGatewayRefusedException or InvoicingGatewayUnavailableException)
        {
            return Result.Failure<GlobalInvoiceDto>(ex.Message);
        }

        // The SAT took it: what is written here must follow, even if someone changed the invoice meanwhile.
        for (var attempt = 1; ; attempt++)
        {
            var tracked = await GlobalInvoicing.WithSales(_context).FirstAsync(i => i.Id == request.InvoiceId, cancellationToken);
            var now = _clock.UtcNow;
            if (tracked.Status == OrderInvoiceStatus.Stamped)
                tracked.RequestCancellation(request.Motive, userId, now);
            if (outcome == CfdiCancellationOutcome.Cancelled && tracked.Status == OrderInvoiceStatus.Cancelling)
                tracked.Cancel(now);
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tracked.TenantId,
                entityName: nameof(GlobalInvoice),
                entityId: tracked.Id.ToString(),
                action: "GlobalInvoiceCancellationRequested",
                performedBy: _currentUser.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { tracked.Number, tracked.Uuid, Motive = $"{(int)request.Motive:00}", Outcome = outcome.ToString() })));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success(GlobalInvoiceDto.From(tracked));
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                _context.ChangeTracker.Clear();
            }
        }
    }
}

/// <summary>
/// The business's global invoices, the newest period first (owners and administrators). The search finds one by its folio
/// (F-12, or 12), its UUID, or the folio of a sale it covers (ORD-000123): which global invoice took that sale.
/// </summary>
[RequiresTenantManager]
public record GetGlobalInvoicesQuery(int PageNumber = 1, int PageSize = 20, string? Search = null)
    : IRequest<PaginatedList<GlobalInvoiceDto>>;

public class GetGlobalInvoicesQueryValidator : AbstractValidator<GetGlobalInvoicesQuery>
{
    public const int MaxSearchLength = 100;

    public GetGlobalInvoicesQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(MaxSearchLength);
    }
}

public class GetGlobalInvoicesQueryHandler : IRequestHandler<GetGlobalInvoicesQuery, PaginatedList<GlobalInvoiceDto>>
{
    private readonly IApplicationDbContext _context;

    public GetGlobalInvoicesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<GlobalInvoiceDto>> Handle(GetGlobalInvoicesQuery request, CancellationToken cancellationToken)
    {
        var invoices = _context.GlobalInvoices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // UUIDs, series and folios of sales are kept in capitals.
            var term = request.Search.Trim().ToUpperInvariant();
            var dash = term.LastIndexOf('-');
            var serie = dash > 0 ? term[..dash] : null;
            long? folio = long.TryParse(term[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
            invoices = invoices.Where(i =>
                (folio != null && i.Folio == folio && (serie == null || i.Serie == serie))
                || i.Uuid == term
                || i.Orders.Any(o => o.OrderNumber == term));
        }

        var page = await PaginatedList<GlobalInvoice>.CreateAsync(
            invoices
                .OrderByDescending(i => i.PeriodFrom)
                .ThenByDescending(i => i.RequestedAt)
                .ThenByDescending(i => i.Folio),
            request.PageNumber,
            request.PageSize,
            cancellationToken);
        var (pageNumber, pageSize) = Paging.Normalize(request.PageNumber, request.PageSize);
        return new PaginatedList<GlobalInvoiceDto>(
            page.Items.Select(GlobalInvoiceDto.From).ToList(), page.TotalCount, pageNumber, pageSize);
    }
}

/// <summary>A global invoice with the sales it covers (owners and administrators).</summary>
[RequiresTenantManager]
public record GetGlobalInvoiceQuery(Guid InvoiceId) : IRequest<GlobalInvoiceDetailDto>;

public class GetGlobalInvoiceQueryHandler : IRequestHandler<GetGlobalInvoiceQuery, GlobalInvoiceDetailDto>
{
    private readonly IApplicationDbContext _context;

    public GetGlobalInvoiceQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GlobalInvoiceDetailDto> Handle(GetGlobalInvoiceQuery request, CancellationToken cancellationToken)
    {
        var invoice = await GlobalInvoicing.WithSales(_context).AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(GlobalInvoice), request.InvoiceId);
        var creditNotes = await _context.CreditNotes.AsNoTracking()
            .Where(n => n.GlobalInvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);

        return GlobalInvoiceDetailDto.WithSales(invoice, creditNotes);
    }
}

/// <summary>
/// The stamped XML or the PDF of a global invoice (owners and administrators). Without a kept PDF, the PAC renders it from
/// the XML. Another business's invoice answers like a missing one.
/// </summary>
[RequiresTenantManager]
public record GetGlobalInvoiceFileQuery(Guid InvoiceId, OrderInvoiceFileKind Kind) : IRequest<Result<PrivateFileDownload>>;

public class GetGlobalInvoiceFileQueryHandler : IRequestHandler<GetGlobalInvoiceFileQuery, Result<PrivateFileDownload>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPrivateFileStorage _storage;
    private readonly IInvoicingGateway _gateway;
    private readonly ILogger<GetOrderInvoiceFileQueryHandler> _logger;

    public GetGlobalInvoiceFileQueryHandler(
        IApplicationDbContext context,
        IPrivateFileStorage storage,
        IInvoicingGateway gateway,
        ILogger<GetOrderInvoiceFileQueryHandler> logger)
    {
        _context = context;
        _storage = storage;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<PrivateFileDownload>> Handle(GetGlobalInvoiceFileQuery request, CancellationToken cancellationToken)
    {
        var invoice = await _context.GlobalInvoices.AsNoTracking()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => new { i.Serie, i.Folio, i.Uuid, i.XmlFileKey, i.PdfFileKey })
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice?.XmlFileKey is null)
            return Result.Failure<PrivateFileDownload>(GetOrderInvoiceFileQueryHandler.NotFoundMessage);

        return await InvoiceFiles.DownloadAsync(
            _storage, _gateway, _logger, request.InvoiceId, $"{invoice.Serie}-{invoice.Folio}_{invoice.Uuid}",
            invoice.XmlFileKey, invoice.PdfFileKey, request.Kind, cancellationToken);
    }
}

/// <summary>What the global invoice's use cases share.</summary>
public static class GlobalInvoicing
{
    public const string PeriodicityMessage = "Elige si la factura global es diaria, semanal, quincenal, mensual o bimestral.";
    public const string DayMessage = "Elige un día del periodo.";
    public const string CancellationMotivesMessage =
        "Cancela la factura global por errores sin relación (02) o porque un cliente pidió la factura de una de sus ventas (04).";
    /// <summary>The fingerprint of a preview: a SHA-256 in hexadecimal.</summary>
    public const int FingerprintLength = 64;

    public const string SalesChangedMessage =
        "Las ventas del periodo cambiaron desde que viste la vista previa: revísala otra vez antes de timbrar.";

    public static string LiveCreditNotesMessage(IReadOnlyList<string> notes) =>
        $"Cancela primero las notas de crédito de esta factura global ({string.Join(", ", notes)}): el SAT no cancela una " +
        "factura mientras tenga notas de crédito vigentes relacionadas.";

    /// <summary>A day since CFDI 4.0 (2022): a period that has not ended yet is refused with its reason, not here.</summary>
    public static bool IsReasonableDay(DateOnly day) => day.Year is >= 2022 and <= 9998;

    /// <summary>Global invoices with their sales: refusing or cancelling one frees them.</summary>
    public static IQueryable<GlobalInvoice> WithSales(IApplicationDbContext context)
        => context.GlobalInvoices.Include(i => i.Orders);
}
