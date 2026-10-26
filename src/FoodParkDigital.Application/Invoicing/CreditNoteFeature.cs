using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>What a credit note credits (Fase 28D, ADR 0048): money given back, or a debt the business forgave.</summary>
public enum CreditNoteAdjustment
{
    Refund,
    WriteOff
}

/// <summary>A credit note (CFDI de egreso, Fase 28D, ADR 0048), as the order's invoicing shows it.</summary>
public class CreditNoteDto
{
    public Guid Id { get; set; }
    public Guid OrderInvoiceId { get; set; }
    /// <summary>Serie and folio, «NC-3».</summary>
    public string Number { get; set; } = null!;
    public OrderInvoiceStatus Status { get; set; }
    /// <summary>c_FormaPago: the refund's, or «Condonación» (15).</summary>
    public string PaymentForm { get; set; } = null!;
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>TienditApp issued it on its own once the adjustment was made (nobody asked for it).</summary>
    public bool IssuedAutomatically { get; set; }
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
    /// <summary>Its latest emails to the customer, the newest first (Fase 28E).</summary>
    public List<CfdiEmailDto> Emails { get; set; } = [];

    public static CreditNoteDto From(CreditNote note) => new()
    {
        Id = note.Id,
        OrderInvoiceId = note.OrderInvoiceId,
        Number = note.Number,
        Status = note.Status,
        PaymentForm = note.PaymentForm,
        SubTotal = note.SubTotal,
        TaxTotal = note.TaxTotal,
        Total = note.Total,
        RequestedAt = note.RequestedAt,
        IssuedAutomatically = note.RequestedByUserId is null,
        Uuid = note.Uuid,
        StampedAt = note.StampedAt,
        HasXml = note.XmlFileKey is not null,
        HasPdf = note.PdfFileKey is not null,
        FailureMessage = note.FailureMessage,
        RefusedAt = note.RefusedAt,
        CancellationMotive = note.CancellationMotive,
        CancellationRequestedAt = note.CancellationRequestedAt,
        CancelledAt = note.CancelledAt
    };
}

/// <summary>
/// A refund or a write-off of an order that has an invoice (Fase 28D), with its credit notes: the live one, and the ones
/// refused or cancelled before it.
/// </summary>
public class OrderAdjustmentDto
{
    public CreditNoteAdjustment Kind { get; set; }
    /// <summary>The refund's or the write-off's id.</summary>
    public Guid AdjustmentId { get; set; }
    /// <summary>When the money was given back, or the debt forgiven.</summary>
    public DateTimeOffset At { get; set; }
    public decimal Amount { get; set; }
    /// <summary>How a refund gave the money back; none for a write-off.</summary>
    public OrderPaymentMethod? Method { get; set; }
    /// <summary>Why a write-off forgave it; none for a refund.</summary>
    public OrderWriteOffReason? WriteOffReason { get; set; }
    /// <summary>A refund's reason, or a write-off's notes.</summary>
    public string? Notes { get; set; }
    /// <summary>A write-off that was undone: it no longer takes a credit note.</summary>
    public bool Reversed { get; set; }
    /// <summary>Whether a credit note can be issued for it now («Emitir ahora» or «Emitir otra vez»).</summary>
    public bool CanIssue { get; set; }
    /// <summary>Why it has no credit note and none can be issued now, when that is the case.</summary>
    public string? Problem { get; set; }
    /// <summary>
    /// TienditApp issues its credit note on its own in its next pass: it can be issued and never had one for the live invoice
    /// (a refused or cancelled one is issued again by hand).
    /// </summary>
    public bool AwaitsAutomaticIssue { get; set; }
    /// <summary>Its credit notes, the newest first.</summary>
    public List<CreditNoteDto> CreditNotes { get; set; } = [];
}

/// <summary>
/// Issues the credit note of a refund or a write-off of an order whose invoice is stamped (whoever manages the orders):
/// «Emitir ahora», before TienditApp does it on its own, or «Emitir otra vez» after a refused or cancelled one. Its folio
/// and CFDI are saved first (<see cref="RequestCreditNoteCommand"/>) and the PAC is asked after the commit.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record IssueCreditNoteCommand(CreditNoteAdjustment Adjustment, Guid AdjustmentId) : IRequest<Result<CreditNoteDto>>;

public class IssueCreditNoteCommandValidator : AbstractValidator<IssueCreditNoteCommand>
{
    public IssueCreditNoteCommandValidator()
    {
        RuleFor(x => x.Adjustment).IsInEnum();
        RuleFor(x => x.AdjustmentId).NotEmpty();
    }
}

public class IssueCreditNoteCommandHandler : IRequestHandler<IssueCreditNoteCommand, Result<CreditNoteDto>>
{
    private readonly ISender _sender;
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

    public IssueCreditNoteCommandHandler(
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

    public async Task<Result<CreditNoteDto>> Handle(IssueCreditNoteCommand request, CancellationToken cancellationToken)
    {
        await CreditNotes.EnsureVisibleAdjustmentAsync(_context, request.Adjustment, request.AdjustmentId, cancellationToken);
        if (!_gateway.IsConfigured)
            return Result.Failure<CreditNoteDto>(InvoicingSettings.NotAvailableMessage);

        var requested = await _sender.Send(new RequestCreditNoteCommand(request.Adjustment, request.AdjustmentId), cancellationToken);
        if (requested.IsFailure)
            return Result.Failure<CreditNoteDto>(requested.Error!);

        var note = await _stamper.StampAsync(_context.CreditNotes, requested.Value, cancellationToken);
        return Result.Success(CreditNoteDto.From(note));
    }
}

/// <summary>
/// The credit note is fixed before the PAC is asked (sent by <see cref="IssueCreditNoteCommand"/>): its folio, its amounts
/// and its CFDI, in one transaction that holds the business's invoicing lock. Answers the new credit note's id.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RequestCreditNoteCommand(CreditNoteAdjustment Adjustment, Guid AdjustmentId) : IRequest<Result<Guid>>, ITransactionalRequest;

public class RequestCreditNoteCommandHandler : IRequestHandler<RequestCreditNoteCommand, Result<Guid>>
{
    private readonly CreditNoteRequester _requester;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public RequestCreditNoteCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IInvoicingGateway gateway,
        IInvoicingLock invoicingLock,
        IFolioGenerator folios,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _requester = new CreditNoteRequester(context, tenant, currentUser, gateway, invoicingLock, folios, clock);
    }

    public async Task<Result<Guid>> Handle(RequestCreditNoteCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<Guid>(InvoicingSettings.NotAvailableMessage);
        await CreditNotes.EnsureVisibleAdjustmentAsync(_context, request.Adjustment, request.AdjustmentId, cancellationToken);
        return await _requester.RequestAsync(request.Adjustment, request.AdjustmentId, userId, cancellationToken);
    }
}

/// <summary>
/// The credit note TienditApp issues on its own for a refund or a write-off of an invoiced order that never had one (sent
/// by <see cref="IssuePendingCreditNotesCommand"/>, in the business's scope): the same request, without a person.
/// </summary>
[AllowAnonymousRequest("Lo envía el worker de facturación (sistema), sin usuario, en el alcance del negocio.")]
public record RequestAutomaticCreditNoteCommand(CreditNoteAdjustment Adjustment, Guid AdjustmentId) : IRequest<Result<Guid>>, ITransactionalRequest;

public class RequestAutomaticCreditNoteCommandHandler : IRequestHandler<RequestAutomaticCreditNoteCommand, Result<Guid>>
{
    private readonly CreditNoteRequester _requester;

    public RequestAutomaticCreditNoteCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IInvoicingGateway gateway,
        IInvoicingLock invoicingLock,
        IFolioGenerator folios,
        IDateTimeProvider clock)
    {
        _requester = new CreditNoteRequester(context, tenant, currentUser, gateway, invoicingLock, folios, clock);
    }

    public Task<Result<Guid>> Handle(RequestAutomaticCreditNoteCommand request, CancellationToken cancellationToken)
        => _requester.RequestAsync(request.Adjustment, request.AdjustmentId, requestedByUserId: null, cancellationToken);
}

/// <summary>
/// Asks the PAC again for a credit note that is still «Timbrando» (its answer did not arrive), with the same CFDI and key: it
/// never stamps a second one.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RetryCreditNoteCommand(Guid CreditNoteId) : IRequest<Result<CreditNoteDto>>;

public class RetryCreditNoteCommandValidator : AbstractValidator<RetryCreditNoteCommand>
{
    public RetryCreditNoteCommandValidator()
    {
        RuleFor(x => x.CreditNoteId).NotEmpty();
    }
}

public class RetryCreditNoteCommandHandler : IRequestHandler<RetryCreditNoteCommand, Result<CreditNoteDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

    public RetryCreditNoteCommandHandler(
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

    public async Task<Result<CreditNoteDto>> Handle(RetryCreditNoteCommand request, CancellationToken cancellationToken)
    {
        await CreditNotes.EnsureVisibleNoteAsync(_context, request.CreditNoteId, cancellationToken);
        if (!_gateway.IsConfigured)
            return Result.Failure<CreditNoteDto>(InvoicingSettings.NotAvailableMessage);

        var note = await _stamper.StampAsync(_context.CreditNotes, request.CreditNoteId, cancellationToken);
        return Result.Success(CreditNoteDto.From(note));
    }
}

/// <summary>
/// The owner or an administrator cancels a stamped credit note with the SAT: because it has errors and nothing replaces it
/// (02), or because the refund or the write-off did not happen (03). The PAC is asked first; nothing changes here if it
/// refuses. The SAT may need the customer to accept it: then it stays «Cancelándose» until the SAT answers.
/// </summary>
[RequiresTenantManager]
public record CancelCreditNoteCommand(Guid CreditNoteId, CfdiCancellationMotive Motive) : IRequest<Result<CreditNoteDto>>;

public class CancelCreditNoteCommandValidator : AbstractValidator<CancelCreditNoteCommand>
{
    public CancelCreditNoteCommandValidator()
    {
        RuleFor(x => x.CreditNoteId).NotEmpty();
        RuleFor(x => x.Motive)
            .Must(motive => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut)
            .WithMessage("Cancela la nota de crédito por errores sin relación (02) o porque la operación no se llevó a cabo (03).");
    }
}

public class CancelCreditNoteCommandHandler : IRequestHandler<CancelCreditNoteCommand, Result<CreditNoteDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public CancelCreditNoteCommandHandler(
        IApplicationDbContext context, ICurrentUserService currentUser, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<CreditNoteDto>> Handle(CancelCreditNoteCommand request, CancellationToken cancellationToken)
    {
        var note = await CreditNotes.EnsureVisibleNoteAsync(_context, request.CreditNoteId, cancellationToken);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<CreditNoteDto>(InvoicingSettings.NotAvailableMessage);
        if (note.Status == OrderInvoiceStatus.Cancelling && note.CancellationMotive == request.Motive)
            return Result.Success(CreditNoteDto.From(note));
        if (note.Status != OrderInvoiceStatus.Stamped)
            return Result.Failure<CreditNoteDto>(note.Status == OrderInvoiceStatus.Cancelled
                ? "Esta nota de crédito ya está cancelada."
                : "Solo se cancela una nota de crédito timbrada.");
        // Before the PAC is asked: what the SAT cancels is then written here.
        if (!note.TakesCancellationMotive(request.Motive))
            return Result.Failure<CreditNoteDto>(CreditNote.RefundCancellationMotivesMessage);
        if (!_gateway.IsConfigured)
            return Result.Failure<CreditNoteDto>(InvoicingSettings.NotAvailableMessage);
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is not { HasCertificate: true })
            return Result.Failure<CreditNoteDto>(CancelOrderInvoiceCommandHandler.NoCertificateToCancelMessage);

        // The issuer the credit note was stamped for: the PAC cancels it with the CSD it keeps for that RFC.
        var issuerRfc = CfdiDocument.FromJson(note.Document).Issuer.Rfc;
        CfdiCancellationOutcome outcome;
        try
        {
            outcome = await _gateway.CancelAsync(issuerRfc, note.Uuid!, request.Motive, cancellationToken);
        }
        catch (Exception ex) when (ex is InvoicingGatewayRefusedException or InvoicingGatewayUnavailableException)
        {
            return Result.Failure<CreditNoteDto>(ex.Message);
        }

        // The SAT took it: what is written here must follow, even if someone changed the credit note meanwhile.
        for (var attempt = 1; ; attempt++)
        {
            var tracked = await _context.CreditNotes.FirstAsync(n => n.Id == request.CreditNoteId, cancellationToken);
            var now = _clock.UtcNow;
            if (tracked.Status == OrderInvoiceStatus.Stamped)
                tracked.RequestCancellation(request.Motive, userId, now);
            if (outcome == CfdiCancellationOutcome.Cancelled && tracked.Status == OrderInvoiceStatus.Cancelling)
                tracked.Cancel(now);
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tracked.TenantId,
                entityName: nameof(CreditNote),
                entityId: tracked.Id.ToString(),
                action: "CreditNoteCancellationRequested",
                performedBy: _currentUser.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { tracked.Number, tracked.Uuid, Motive = $"{(int)request.Motive:00}", Outcome = outcome.ToString() })));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success(CreditNoteDto.From(tracked));
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                _context.ChangeTracker.Clear();
            }
        }
    }
}

/// <summary>
/// The stamped XML or the PDF of a credit note (whoever manages the orders). Without a kept PDF, the PAC renders it from the
/// XML. Another business's credit note answers like a missing one.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record GetCreditNoteFileQuery(Guid CreditNoteId, OrderInvoiceFileKind Kind) : IRequest<Result<PrivateFileDownload>>;

public class GetCreditNoteFileQueryHandler : IRequestHandler<GetCreditNoteFileQuery, Result<PrivateFileDownload>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPrivateFileStorage _storage;
    private readonly IInvoicingGateway _gateway;
    private readonly ILogger<GetCreditNoteFileQueryHandler> _logger;

    public GetCreditNoteFileQueryHandler(
        IApplicationDbContext context,
        IPrivateFileStorage storage,
        IInvoicingGateway gateway,
        ILogger<GetCreditNoteFileQueryHandler> logger)
    {
        _context = context;
        _storage = storage;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<PrivateFileDownload>> Handle(GetCreditNoteFileQuery request, CancellationToken cancellationToken)
    {
        var note = await _context.CreditNotes.AsNoTracking()
            .Where(n => n.Id == request.CreditNoteId)
            .Select(n => new { n.OrderId, n.Serie, n.Folio, n.Uuid, n.XmlFileKey, n.PdfFileKey })
            .FirstOrDefaultAsync(cancellationToken);
        if (note?.XmlFileKey is null || !await OrderInvoicing.SeesOrderAsync(_context, note.OrderId, cancellationToken))
            return Result.Failure<PrivateFileDownload>(GetOrderInvoiceFileQueryHandler.NotFoundMessage);

        return await InvoiceFiles.DownloadAsync(
            _storage, _gateway, _logger, request.CreditNoteId, $"{note.Serie}-{note.Folio}_{note.Uuid}",
            note.XmlFileKey, note.PdfFileKey, request.Kind, cancellationToken);
    }
}

/// <summary>
/// Issues the credit notes a business owes (the invoicing worker sends it in the business's scope, Fase 28D): each refund
/// and each write-off that still stands of an order whose invoice is stamped and that never had a credit note for it, the
/// oldest first, as long as the refund has a form of the SAT's. A refused or cancelled credit note is issued again only by
/// a person. Up to <see cref="IssuePendingCreditNotesCommandHandler.MaxPerPass"/> are issued in a pass; the ones that cannot
/// be issued yet are passed over, so they never hold up the newer ones behind them.
/// </summary>
[AllowAnonymousRequest("Lo envía el worker de facturación (sistema), sin usuario, en el alcance del negocio.")]
public record IssuePendingCreditNotesCommand : IRequest<Result<int>>;

public class IssuePendingCreditNotesCommandHandler : IRequestHandler<IssuePendingCreditNotesCommand, Result<int>>
{
    public const int MaxPerPass = 20;
    /// <summary>The adjustments a pass looks at, the ones that fail included.</summary>
    public const int MaxCandidatesPerPass = 100;

    private readonly ISender _sender;
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;
    private readonly ILogger<OrderInvoiceStamper> _logger;

    public IssuePendingCreditNotesCommandHandler(
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
        _logger = logger;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
    }

    /// <returns>Credit notes that ended up stamped.</returns>
    public async Task<Result<int>> Handle(IssuePendingCreditNotesCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Success(0);

        var refunds = await CreditNotes.PendingRefunds(_context.OrderRefunds, _context.Orders, _context.OrderInvoices, _context.CreditNotes)
            .OrderBy(r => r.RefundedAt)
            .ThenBy(r => r.Id)
            .Select(r => new { r.Id, At = r.RefundedAt })
            .Take(MaxCandidatesPerPass)
            .ToListAsync(cancellationToken);
        var writeOffs = await CreditNotes.PendingWriteOffs(_context.OrderWriteOffs, _context.OrderInvoices, _context.CreditNotes)
            .OrderBy(w => w.WrittenOffAt)
            .ThenBy(w => w.Id)
            .Select(w => new { w.Id, At = w.WrittenOffAt })
            .Take(MaxCandidatesPerPass)
            .ToListAsync(cancellationToken);
        var pending = refunds.Select(r => (Adjustment: CreditNoteAdjustment.Refund, r.Id, r.At))
            .Concat(writeOffs.Select(w => (Adjustment: CreditNoteAdjustment.WriteOff, w.Id, w.At)))
            .OrderBy(p => p.At)
            .ThenBy(p => p.Id)
            .Take(MaxCandidatesPerPass)
            .ToList();

        var stamped = 0;
        var issued = 0;
        string? firstProblem = null;
        var problems = 0;
        foreach (var (adjustment, adjustmentId, _) in pending)
        {
            if (issued >= MaxPerPass)
                break;
            try
            {
                var requested = await _sender.Send(new RequestAutomaticCreditNoteCommand(adjustment, adjustmentId), cancellationToken);
                _context.ChangeTracker.Clear();
                if (requested.IsFailure)
                {
                    problems++;
                    firstProblem ??= requested.Error;
                    continue;
                }
                issued++;
                var note = await _stamper.StampAsync(_context.CreditNotes, requested.Value, cancellationToken);
                if (note.Status == OrderInvoiceStatus.Stamped)
                    stamped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One that fails (its file, its XML, the database) does not hold up the others: the next pass asks again.
                _logger.LogError("The credit note of {Adjustment} {AdjustmentId} could not be issued in this pass: {Error}",
                    adjustment, adjustmentId, ex.GetType().Name);
            }
            _context.ChangeTracker.Clear();
        }
        // Until the business can issue them (its CSD, Mi negocio), the next pass tries again: said once per pass.
        if (problems > 0)
            _logger.LogWarning("{Count} credit notes wait for the business: {Problem}", problems, firstProblem);
        return Result.Success(stamped);
    }
}

/// <summary>What issuing a credit note asks for (Fase 28D): the adjustment, its order's invoice and what else it credits.</summary>
internal sealed class CreditNoteRequester(
    IApplicationDbContext context,
    ICurrentTenantService tenant,
    ICurrentUserService currentUser,
    IInvoicingGateway gateway,
    IInvoicingLock invoicingLock,
    IFolioGenerator folios,
    IDateTimeProvider clock)
{
    public async Task<Result<Guid>> RequestAsync(
        CreditNoteAdjustment adjustment, Guid adjustmentId, Guid? requestedByUserId, CancellationToken cancellationToken)
    {
        if (!gateway.IsConfigured)
            return Result.Failure<Guid>(InvoicingSettings.NotAvailableMessage);

        var tenantId = tenant.TenantId;
        // One CFDI at a time per business: what a credit note may credit counts the complements and credit notes before it.
        await invoicingLock.AcquireAsync(tenantId, cancellationToken);
        OrderRefund? refund = null;
        OrderWriteOff? writeOff = null;
        if (adjustment == CreditNoteAdjustment.Refund)
            refund = await context.OrderRefunds.AsNoTracking().FirstOrDefaultAsync(r => r.Id == adjustmentId, cancellationToken);
        else
            writeOff = await context.OrderWriteOffs.AsNoTracking().FirstOrDefaultAsync(w => w.Id == adjustmentId, cancellationToken);
        if (refund is null && writeOff is null)
            return Result.Failure<Guid>(CreditNotes.NotFoundMessage);
        if (writeOff is { IsReversed: true })
            return Result.Failure<Guid>(CreditNote.ReversedWriteOffMessage);
        var orderId = refund?.OrderId ?? writeOff!.OrderId;
        var amount = refund?.Amount ?? writeOff!.Amount;
        // A cancelled sale did not happen: its invoice is cancelled with 03, without credit notes in between.
        if (await context.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId && o.Status == OrderStatus.Cancelled, cancellationToken))
            return Result.Failure<Guid>(CreditNotes.CancelledOrderMessage);

        var invoice = await context.OrderInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == orderId
                && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                    || i.Status == OrderInvoiceStatus.Cancelling), cancellationToken);
        var problem = (refund is null ? null : CreditNotes.FormProblem(refund.Method)) ?? CreditNotes.InvoiceProblem(invoice);
        if (problem is not null)
            return Result.Failure<Guid>(problem);

        // A live credit note of the adjustment, of this invoice or of one cancelled since: never two that stand at once.
        var existing = await CreditNotes.OfAdjustment(context.CreditNotes.AsNoTracking(), adjustment, adjustmentId)
            .Where(n => n.Status == OrderInvoiceStatus.Stamping || n.Status == OrderInvoiceStatus.Stamped
                || n.Status == OrderInvoiceStatus.Cancelling)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return Result.Failure<Guid>(CreditNotes.LiveCreditNoteProblem(existing));
        // TienditApp issues one on its own only for an adjustment that never had one for this invoice: one refused or
        // cancelled meanwhile (by a person, after the worker listed it) is issued again only by a person.
        if (requestedByUserId is null
            && await CreditNotes.OfAdjustment(context.CreditNotes.AsNoTracking(), adjustment, adjustmentId)
                .AnyAsync(n => n.OrderInvoiceId == invoice!.Id, cancellationToken))
            return Result.Failure<Guid>(CreditNotes.IssuedByHandMessage);

        var now = clock.UtcNow;
        var business = await context.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Rfc, t.LegalName, t.TaxRegime, PostalCode = t.Address == null ? null : t.Address.ZipCode })
            .FirstAsync(cancellationToken);
        var issuer = GetOrderCfdiDraftQueryHandler.Issuer(business.Rfc, business.LegalName, business.TaxRegime);
        var postalCode = business.PostalCode?.Trim();
        if (GetOrderCfdiDraftQueryHandler.IssuerBlockers(issuer, postalCode).FirstOrDefault() is { } blocker)
            return Result.Failure<Guid>(blocker);
        var profile = await context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (InvoicingSettings.CertificateProblem(profile, issuer.Rfc, now) is { } certificateProblem)
            return Result.Failure<Guid>(certificateProblem);
        // In the order the order's invoicing shows them (ForOrderAsync): Mi negocio and its CSD first, then the invoice.
        var invoiceDocument = CfdiDocument.FromJson(invoice!.Document);
        if (CreditNotes.DocumentProblem(invoice, invoiceDocument, issuer.Rfc) is { } documentProblem)
            return Result.Failure<Guid>(documentProblem);

        var standing = await CreditNotes.StandingAsync(context, invoice, cancellationToken);
        var room = CreditNotes.RoomFor(invoice, standing, adjustment, adjustmentId);
        if (amount > room.Available)
            return Result.Failure<Guid>(CreditNotes.AboveRoomMessage(invoice.Number, adjustment));

        IReadOnlyList<CfdiTransfer> transfers;
        decimal notSubject;
        try
        {
            (transfers, notSubject) = CreditNoteTaxes.Split(
                invoiceDocument.Transfers.Select(PaymentComplements.Transfer).ToList(), invoice.Total, amount);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(ex.Message);
        }

        var timeZone = await context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var serie = profile!.CreditNoteSerie;
        var folio = await folios.NextInvoiceFolioAsync(tenantId, serie, cancellationToken);
        var paymentForm = refund is null ? SatCatalog.PaymentFormWriteOff : SatCatalog.PaymentComplementForm(refund.Method)!;
        var document = CfdiDocument.ForCreditNote(
            new CfdiDocumentIssuer(issuer.Rfc!, issuer.Name!, issuer.FiscalRegime!),
            invoiceDocument.Receiver,
            postalCode!,
            serie,
            folio,
            TenantDateTimeResolver.LocalNow(now, timeZone),
            paymentForm,
            refund is null ? SatCatalog.WriteOffDescription : SatCatalog.RefundDescription,
            transfers,
            notSubject,
            invoice.Uuid!);

        CreditNote note;
        try
        {
            note = refund is not null
                ? CreditNote.ForRefund(invoice, refund, serie, folio, document.SubTotal, document.TotalTransferred,
                    room.AfterInstallment, document.ToJson(), requestedByUserId, now)
                : CreditNote.ForWriteOff(invoice, writeOff!, serie, folio, document.SubTotal, document.TotalTransferred,
                    room.AfterInstallment, document.ToJson(), requestedByUserId, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(ex.Message);
        }
        context.CreditNotes.Add(note);
        context.AuditLogs.Add(new AuditLog(
            tenantId: tenantId,
            entityName: nameof(CreditNote),
            entityId: note.Id.ToString(),
            action: "CreditNoteRequested",
            performedBy: currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new
            {
                note.Number,
                Invoice = invoice.Number,
                Adjustment = adjustment.ToString(),
                AdjustmentId = adjustmentId,
                note.Total,
                note.PaymentForm,
                Automatic = requestedByUserId is null
            })));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone issued the credit note of the same adjustment at the same time (the index of one live credit note per
            // adjustment kept theirs): this one is undone whole, folio included. Anything else is thrown on.
            await context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await CreditNotes.OfAdjustment(context.CreditNotes.AsNoTracking(), adjustment, adjustmentId).AnyAsync(
                    n => n.Status == OrderInvoiceStatus.Stamping || n.Status == OrderInvoiceStatus.Stamped
                        || n.Status == OrderInvoiceStatus.Cancelling,
                    cancellationToken))
                return Result.Failure<Guid>(CreditNotes.AlreadyIssuingMessage);
            throw;
        }

        return Result.Success(note.Id);
    }
}

/// <summary>What deciding whether a refund or a write-off gets a credit note shares (Fase 28D).</summary>
public static class CreditNotes
{
    public const string NotFoundMessage = "Ese reembolso o esa condonación ya no está en este pedido.";
    public const string NoStampedInvoiceMessage =
        "Este pedido no tiene una factura timbrada: la nota de crédito se emite con ella.";
    public const string AlreadyIssuingMessage =
        "Otra persona está emitiendo esta nota de crédito en este momento: actualiza la página.";
    public const string CancelledOrderMessage =
        "Este pedido está cancelado: su factura se cancela ante el SAT con el motivo 03 (la venta no se llevó a cabo), sin notas " +
        "de crédito.";
    public const string IssuedByHandMessage =
        "Ya tuvo una nota de crédito de esta factura: la siguiente se emite a mano, con «Emitir otra vez».";
    public const string OtherFormMessage =
        "Este reembolso se registró como «Otro», que no es una forma de pago del SAT: su nota de crédito no se emite desde " +
        "TienditApp. Consúltalo con tu contador.";
    public const string CustomerBalanceMessage =
        "Este reembolso se devolvió como saldo a favor del cliente (un anticipo): su CFDI lo ve tu contador y TienditApp no " +
        "emite su nota de crédito.";

    public static string OtherRfcMessage(string invoiceNumber) =>
        $"El RFC de Mi negocio ya no es el de la factura {invoiceNumber}: su nota de crédito la emite el mismo RFC que la timbró.";

    public static string AboveRoomMessage(string invoiceNumber, CreditNoteAdjustment adjustment) => adjustment == CreditNoteAdjustment.Refund
        ? $"El reembolso es mayor que lo que la factura {invoiceNumber} todavía ampara después de sus otras notas de crédito."
        : $"Lo condonado es mayor que lo que la factura {invoiceNumber} todavía debe después de sus cobros con complemento y sus otras notas de crédito.";

    /// <summary>A refund without a form of the SAT's never gets a credit note here.</summary>
    public static string? FormProblem(OrderPaymentMethod method) => SatCatalog.PaymentComplementForm(method) is not null
        ? null
        : method == OrderPaymentMethod.CustomerBalance ? CustomerBalanceMessage : OtherFormMessage;

    /// <summary>Why the order's live invoice takes no credit note now, or null when it is stamped.</summary>
    public static string? InvoiceProblem(OrderInvoice? invoice) => invoice switch
    {
        null => NoStampedInvoiceMessage,
        { Status: OrderInvoiceStatus.Stamping } => $"La factura {invoice.Number} todavía se está timbrando: la nota de crédito se emite cuando quede timbrada.",
        { Status: OrderInvoiceStatus.Cancelling } => $"La factura {invoice.Number} espera su cancelación en el SAT: no se le emiten notas de crédito.",
        _ => null
    };

    /// <summary>Why no credit note of the stamped invoice is issued from TienditApp at all: Mi negocio's RFC changed.</summary>
    public static string? DocumentProblem(OrderInvoice invoice, CfdiDocument document, string? businessRfc)
        => string.Equals(businessRfc, document.Issuer.Rfc, StringComparison.Ordinal) ? null : OtherRfcMessage(invoice.Number);

    public static string LiveCreditNoteProblem(CreditNote live)
    {
        var ofIt = live.IsForRefund ? "este reembolso" : "esta condonación";
        return live.Status switch
        {
            OrderInvoiceStatus.Stamping => $"La nota de crédito {live.Number} de {ofIt} se está timbrando.",
            OrderInvoiceStatus.Cancelling => $"La nota de crédito {live.Number} de {ofIt} espera su cancelación en el SAT.",
            _ => $"{(live.IsForRefund ? "Este reembolso" : "Esta condonación")} ya tiene la nota de crédito {live.Number}: cancélala para emitir otra."
        };
    }

    /// <summary>The credit notes of a refund or of a write-off.</summary>
    public static IQueryable<CreditNote> OfAdjustment(IQueryable<CreditNote> notes, CreditNoteAdjustment adjustment, Guid adjustmentId)
        => adjustment == CreditNoteAdjustment.Refund
            ? notes.Where(n => n.OrderRefundId == adjustmentId)
            : notes.Where(n => n.OrderWriteOffId == adjustmentId);

    /// <summary>
    /// What stands against an invoice when a credit note or a complement is issued (ADR 0047, ADR 0048): each payment that
    /// ever had a complement of it holds its latest one (it was received, and a refused or cancelled one is issued again),
    /// and each refund and each write-off that still stands holds its latest credit note, for the same reason. A write-off
    /// that was undone holds none.
    /// </summary>
    /// <param name="Complements">The complement that holds each payment's installment (only for an invoice in installments).</param>
    /// <param name="RefundNotes">The credit note that holds each refund.</param>
    /// <param name="WriteOffNotes">The credit note that holds each write-off that still stands.</param>
    public sealed record Standing(
        IReadOnlyList<PaymentComplement> Complements,
        IReadOnlyList<CreditNote> RefundNotes,
        IReadOnlyList<CreditNote> WriteOffNotes);

    /// <summary>Where a credit note goes: what it may credit at most, and the installment it comes after.</summary>
    public sealed record Room(decimal Available, int AfterInstallment);

    public static async Task<Standing> StandingAsync(IApplicationDbContext context, OrderInvoice invoice, CancellationToken cancellationToken)
    {
        var complements = invoice.IsPaidInInstallments
            ? await context.PaymentComplements.AsNoTracking().Where(c => c.OrderInvoiceId == invoice.Id).ToListAsync(cancellationToken)
            : [];
        var notes = await context.CreditNotes.AsNoTracking().Where(n => n.OrderInvoiceId == invoice.Id).ToListAsync(cancellationToken);
        var undone = await context.OrderWriteOffs.AsNoTracking()
            .Where(w => w.OrderId == invoice.OrderId && w.ReversedAt != null)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
        return StandingOf(complements, notes, undone);
    }

    /// <param name="complements">Every complement of the invoice, whatever its status.</param>
    /// <param name="notes">Every credit note of the invoice, whatever its status.</param>
    /// <param name="undoneWriteOffs">The write-offs of its order that were undone.</param>
    public static Standing StandingOf(
        IEnumerable<PaymentComplement> complements, IEnumerable<CreditNote> notes, IReadOnlyCollection<Guid> undoneWriteOffs)
    {
        var held = notes
            .Where(n => n.OrderWriteOffId is not { } writeOffId || !undoneWriteOffs.Contains(writeOffId))
            .GroupBy(n => n.OrderRefundId ?? n.OrderWriteOffId)
            .Select(g => g.OrderByDescending(n => n.RequestedAt).First())
            .ToList();
        return new Standing(
            complements.GroupBy(c => c.OrderPaymentId).Select(g => g.OrderByDescending(c => c.RequestedAt).First()).ToList(),
            held.Where(n => n.IsForRefund).ToList(),
            held.Where(n => !n.IsForRefund).ToList());
    }

    /// <summary>
    /// What a credit note of the adjustment may credit at most (ADR 0048). Never more than what the invoice still amounts to
    /// after the other credit notes. A write-off forgives what is owed, so neither more than what the invoice still owes
    /// after the payments that hold an installment and the other write-offs; a refund gives back money that was received, so
    /// the payments do not limit it. It comes after the highest installment issued; issued again, where the adjustment's
    /// earlier credit note of the invoice came, so the complements that counted that one go on counting this one.
    /// </summary>
    public static Room RoomFor(OrderInvoice invoice, Standing standing, CreditNoteAdjustment adjustment, Guid adjustmentId)
    {
        var otherRefunds = standing.RefundNotes.Where(n => n.OrderRefundId != adjustmentId).Sum(n => n.Total);
        var otherWriteOffs = standing.WriteOffNotes.Where(n => n.OrderWriteOffId != adjustmentId).Sum(n => n.Total);
        var available = invoice.Total - otherRefunds - otherWriteOffs;
        if (adjustment == CreditNoteAdjustment.WriteOff)
            available = Math.Min(available, invoice.Total - standing.Complements.Sum(c => c.Amount) - otherWriteOffs);
        var earlier = adjustment == CreditNoteAdjustment.Refund
            ? standing.RefundNotes.FirstOrDefault(n => n.OrderRefundId == adjustmentId)
            : standing.WriteOffNotes.FirstOrDefault(n => n.OrderWriteOffId == adjustmentId);
        return new Room(available, earlier?.AfterInstallment
            ?? (standing.Complements.Count == 0 ? 0 : standing.Complements.Max(c => c.Installment)));
    }

    /// <summary>
    /// The refunds of orders whose invoice is stamped that never had a credit note for that invoice and have a form of the
    /// SAT's: what the worker issues. Not those of a cancelled order: its invoice is cancelled with 03 instead. It takes the
    /// sets to read, so the worker can find the businesses with work first.
    /// </summary>
    public static IQueryable<OrderRefund> PendingRefunds(
        IQueryable<OrderRefund> refunds, IQueryable<Order> orders, IQueryable<OrderInvoice> invoices, IQueryable<CreditNote> notes)
        => refunds.Where(refund => refund.Method != OrderPaymentMethod.Other
            && refund.Method != OrderPaymentMethod.CustomerBalance
            && orders.Any(order => order.TenantId == refund.TenantId && order.Id == refund.OrderId && order.Status != OrderStatus.Cancelled)
            && invoices.Any(invoice => invoice.TenantId == refund.TenantId && invoice.OrderId == refund.OrderId
                && invoice.Status == OrderInvoiceStatus.Stamped
                && !notes.Any(n => n.OrderRefundId == refund.Id && n.OrderInvoiceId == invoice.Id)));

    /// <summary>The write-offs that still stand of orders whose invoice is stamped and that never had a credit note for it.</summary>
    public static IQueryable<OrderWriteOff> PendingWriteOffs(
        IQueryable<OrderWriteOff> writeOffs, IQueryable<OrderInvoice> invoices, IQueryable<CreditNote> notes)
        => writeOffs.Where(writeOff => writeOff.ReversedAt == null
            && invoices.Any(invoice => invoice.TenantId == writeOff.TenantId && invoice.OrderId == writeOff.OrderId
                && invoice.Status == OrderInvoiceStatus.Stamped
                && !notes.Any(n => n.OrderWriteOffId == writeOff.Id && n.OrderInvoiceId == invoice.Id)));

    /// <summary>The refund or write-off, if the business sees its order (as the order's detail does); otherwise it is not there.</summary>
    public static async Task EnsureVisibleAdjustmentAsync(
        IApplicationDbContext context, CreditNoteAdjustment adjustment, Guid adjustmentId, CancellationToken cancellationToken)
    {
        var orderId = adjustment == CreditNoteAdjustment.Refund
            ? await context.OrderRefunds.AsNoTracking().Where(r => r.Id == adjustmentId).Select(r => (Guid?)r.OrderId)
                .FirstOrDefaultAsync(cancellationToken)
            : await context.OrderWriteOffs.AsNoTracking().Where(w => w.Id == adjustmentId).Select(w => (Guid?)w.OrderId)
                .FirstOrDefaultAsync(cancellationToken);
        if (orderId is null || !await OrderInvoicing.SeesOrderAsync(context, orderId.Value, cancellationToken))
            throw new EntityNotFoundException(adjustment == CreditNoteAdjustment.Refund ? nameof(OrderRefund) : nameof(OrderWriteOff), adjustmentId);
    }

    /// <summary>The credit note, if the business sees its order; otherwise it is not there.</summary>
    public static async Task<CreditNote> EnsureVisibleNoteAsync(IApplicationDbContext context, Guid creditNoteId, CancellationToken cancellationToken)
    {
        var note = await context.CreditNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == creditNoteId, cancellationToken);
        if (note is null || !await OrderInvoicing.SeesOrderAsync(context, note.OrderId, cancellationToken))
            throw new EntityNotFoundException(nameof(CreditNote), creditNoteId);
        return note;
    }

    /// <summary>
    /// The refunds and write-offs of an order that has an invoice, with their credit notes and whether one can be issued now;
    /// none for an order that never had one. It says what issuing one would refuse (without the lock, which only orders who
    /// goes first), so the screen never waits for one that TienditApp cannot issue.
    /// </summary>
    public static async Task<List<OrderAdjustmentDto>> ForOrderAsync(
        IApplicationDbContext context,
        Order order,
        IReadOnlyList<OrderInvoice> invoices,
        InvoicingSettingsDto settings,
        CancellationToken cancellationToken)
    {
        if (invoices.Count == 0)
            return [];

        var refunds = await context.OrderRefunds.AsNoTracking()
            .Where(r => r.OrderId == order.Id)
            .OrderBy(r => r.RefundedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        var writeOffs = await context.OrderWriteOffs.AsNoTracking()
            .Where(w => w.OrderId == order.Id)
            .OrderBy(w => w.WrittenOffAt)
            .ThenBy(w => w.Id)
            .ToListAsync(cancellationToken);
        if (refunds.Count == 0 && writeOffs.Count == 0)
            return [];

        var notes = await context.CreditNotes.AsNoTracking()
            .Where(n => n.OrderId == order.Id)
            .OrderByDescending(n => n.RequestedAt)
            .ThenByDescending(n => n.Folio)
            .ToListAsync(cancellationToken);
        var liveInvoice = invoices.FirstOrDefault(i => i.IsLive);
        var invoiceProblem = InvoiceProblem(liveInvoice) ?? InvoicingSettings.ReadinessProblem(settings);
        if (invoiceProblem is null)
            invoiceProblem = DocumentProblem(liveInvoice!, CfdiDocument.FromJson(liveInvoice!.Document), settings.Rfc);
        Standing? standing = null;
        if (invoiceProblem is null)
        {
            var complements = liveInvoice!.IsPaidInInstallments
                ? await context.PaymentComplements.AsNoTracking().Where(c => c.OrderInvoiceId == liveInvoice.Id).ToListAsync(cancellationToken)
                : [];
            standing = StandingOf(
                complements,
                notes.Where(n => n.OrderInvoiceId == liveInvoice.Id),
                writeOffs.Where(w => w.IsReversed).Select(w => w.Id).ToList());
        }

        OrderAdjustmentDto Describe(CreditNoteAdjustment kind, Guid id, decimal amount, string? formProblem, bool reversed)
        {
            var own = notes.Where(n => (kind == CreditNoteAdjustment.Refund ? n.OrderRefundId : n.OrderWriteOffId) == id).ToList();
            var live = own.FirstOrDefault(n => n.IsLive);
            var problem = live is not null
                ? null
                : reversed ? CreditNote.ReversedWriteOffMessage
                : order.Status == OrderStatus.Cancelled ? CancelledOrderMessage
                : formProblem ?? invoiceProblem
                    ?? (amount > RoomFor(liveInvoice!, standing!, kind, id).Available ? AboveRoomMessage(liveInvoice!.Number, kind) : null);
            return new OrderAdjustmentDto
            {
                Kind = kind,
                AdjustmentId = id,
                Amount = amount,
                Reversed = reversed,
                CanIssue = live is null && problem is null,
                Problem = problem,
                AwaitsAutomaticIssue = live is null && problem is null
                    && liveInvoice is not null && own.All(n => n.OrderInvoiceId != liveInvoice.Id),
                CreditNotes = own.Select(CreditNoteDto.From).ToList()
            };
        }

        return refunds
            .Select(refund =>
            {
                var dto = Describe(CreditNoteAdjustment.Refund, refund.Id, refund.Amount, FormProblem(refund.Method), reversed: false);
                dto.At = refund.RefundedAt;
                dto.Method = refund.Method;
                dto.Notes = refund.Reason;
                return dto;
            })
            .Concat(writeOffs.Select(writeOff =>
            {
                var dto = Describe(CreditNoteAdjustment.WriteOff, writeOff.Id, writeOff.Amount, null, writeOff.IsReversed);
                dto.At = writeOff.WrittenOffAt;
                dto.WriteOffReason = writeOff.Reason;
                dto.Notes = writeOff.Notes;
                return dto;
            }))
            .OrderBy(dto => dto.At)
            .ThenBy(dto => dto.AdjustmentId)
            .ToList();
    }

    /// <summary>The credit notes of an invoice that stand with the SAT: while one does, the invoice is not cancelled.</summary>
    public static async Task<List<string>> LiveNumbersAsync(IApplicationDbContext context, Guid orderInvoiceId, CancellationToken cancellationToken)
        => (await context.CreditNotes.AsNoTracking()
                .Where(n => n.OrderInvoiceId == orderInvoiceId
                    && (n.Status == OrderInvoiceStatus.Stamping || n.Status == OrderInvoiceStatus.Stamped
                        || n.Status == OrderInvoiceStatus.Cancelling))
                .OrderBy(n => n.Folio)
                .Select(n => new { n.Serie, n.Folio })
                .ToListAsync(cancellationToken))
            .Select(n => $"{n.Serie}-{n.Folio}")
            .ToList();

    /// <summary>
    /// The payment complements in force of an invoice in installments whose balance before counts a credit note of the
    /// write-off (installments after the one it was asked after, Fase 28C): undoing the write-off takes it out of that
    /// chain, so they are cancelled first and issued again without it.
    /// </summary>
    public static async Task<List<string>> ComplementsCountingWriteOffAsync(
        IApplicationDbContext context, Guid writeOffId, CancellationToken cancellationToken)
    {
        var held = (await context.CreditNotes.AsNoTracking()
                .Where(n => n.OrderWriteOffId == writeOffId)
                .ToListAsync(cancellationToken))
            .GroupBy(n => n.OrderInvoiceId)
            .Select(g => g.OrderByDescending(n => n.RequestedAt).First())
            .ToList();
        var numbers = new List<string>();
        foreach (var note in held)
        {
            numbers.AddRange((await context.PaymentComplements.AsNoTracking()
                    .Where(c => c.OrderInvoiceId == note.OrderInvoiceId && c.Installment > note.AfterInstallment
                        && (c.Status == OrderInvoiceStatus.Stamping || c.Status == OrderInvoiceStatus.Stamped
                            || c.Status == OrderInvoiceStatus.Cancelling))
                    .OrderBy(c => c.Folio)
                    .Select(c => new { c.Serie, c.Folio })
                    .ToListAsync(cancellationToken))
                .Select(c => $"{c.Serie}-{c.Folio}"));
        }
        return numbers;
    }

    /// <summary>The live credit note of a write-off, if it has one: while it stands, the write-off is not undone.</summary>
    public static Task<CreditNote?> LiveOfWriteOffAsync(IApplicationDbContext context, Guid writeOffId, CancellationToken cancellationToken)
        => context.CreditNotes.AsNoTracking()
            .Where(n => n.OrderWriteOffId == writeOffId
                && (n.Status == OrderInvoiceStatus.Stamping || n.Status == OrderInvoiceStatus.Stamped
                    || n.Status == OrderInvoiceStatus.Cancelling))
            .FirstOrDefaultAsync(cancellationToken);
}
