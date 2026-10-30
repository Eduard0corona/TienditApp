using System.Globalization;
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

/// <summary>A payment complement (CFDI tipo P, Fase 28C, ADR 0047), as the order's invoicing shows it.</summary>
public class PaymentComplementDto
{
    public Guid Id { get; set; }
    public Guid OrderInvoiceId { get; set; }
    public Guid OrderPaymentId { get; set; }
    /// <summary>Serie and folio, «P-3».</summary>
    public string Number { get; set; } = null!;
    public OrderInvoiceStatus Status { get; set; }
    /// <summary>c_FormaPago of the payment.</summary>
    public string PaymentForm { get; set; } = null!;
    public decimal Amount { get; set; }
    /// <summary>NumParcialidad.</summary>
    public int Installment { get; set; }
    /// <summary>What the invoice owed before the payment, and after it.</summary>
    public decimal PreviousBalance { get; set; }
    public decimal RemainingBalance { get; set; }
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>
    /// While it is «Timbrando», until when TienditApp asks the PAC for its stamp; after that it is checked in the SAT's portal
    /// (Fase 28K).
    /// </summary>
    public DateTimeOffset? StampWindowEndsAt { get; set; }
    /// <summary>TienditApp issued it on its own once the payment was registered (nobody asked for it).</summary>
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

    public static PaymentComplementDto From(PaymentComplement complement) => new()
    {
        Id = complement.Id,
        OrderInvoiceId = complement.OrderInvoiceId,
        OrderPaymentId = complement.OrderPaymentId,
        Number = complement.Number,
        Status = complement.Status,
        PaymentForm = complement.PaymentForm,
        Amount = complement.Amount,
        Installment = complement.Installment,
        PreviousBalance = complement.PreviousBalance,
        RemainingBalance = complement.RemainingBalance,
        RequestedAt = complement.RequestedAt,
        StampWindowEndsAt = complement.Status == OrderInvoiceStatus.Stamping ? StuckStamps.WindowEndsAt(complement.RequestedAt) : null,
        IssuedAutomatically = complement.RequestedByUserId is null,
        Uuid = complement.Uuid,
        StampedAt = complement.StampedAt,
        HasXml = complement.XmlFileKey is not null,
        HasPdf = complement.PdfFileKey is not null,
        FailureMessage = complement.FailureMessage,
        RefusedAt = complement.RefusedAt,
        CancellationMotive = complement.CancellationMotive,
        CancellationRequestedAt = complement.CancellationRequestedAt,
        CancelledAt = complement.CancelledAt
    };
}

/// <summary>
/// A payment of an order with an invoice in installments, with its complements (Fase 28C): the live one, and the ones
/// refused or cancelled before it.
/// </summary>
public class OrderPaymentComplementsDto
{
    public Guid OrderPaymentId { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public OrderPaymentMethod Method { get; set; }
    /// <summary>What it paid of the sale, without its tip.</summary>
    public decimal Amount { get; set; }
    /// <summary>The last day the SAT gives to issue its complement: the 5th of the month after the payment.</summary>
    public DateOnly DueBy { get; set; }
    /// <summary>Whether a complement can be issued for it now («Emitir ahora» or «Emitir otra vez»).</summary>
    public bool CanIssue { get; set; }
    /// <summary>Why it has no complement and none can be issued now, when that is the case.</summary>
    public string? Problem { get; set; }
    /// <summary>
    /// TienditApp issues its complement on its own in its next pass: it can be issued and has never had one for the live
    /// invoice (a refused or cancelled one is issued again by hand).
    /// </summary>
    public bool AwaitsAutomaticIssue { get; set; }
    /// <summary>Its complements, the newest first.</summary>
    public List<PaymentComplementDto> Complements { get; set; } = [];
}

/// <summary>
/// Issues the complement of a payment of an order whose invoice is in installments (whoever manages the orders): «Emitir
/// ahora», before TienditApp does it on its own, or «Emitir otra vez» after a refused or cancelled one. Its folio and CFDI
/// are saved first (<see cref="RequestPaymentComplementCommand"/>) and the PAC is asked after the commit.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record IssuePaymentComplementCommand(Guid OrderPaymentId) : IRequest<Result<PaymentComplementDto>>;

public class IssuePaymentComplementCommandValidator : AbstractValidator<IssuePaymentComplementCommand>
{
    public IssuePaymentComplementCommandValidator()
    {
        RuleFor(x => x.OrderPaymentId).NotEmpty();
    }
}

public class IssuePaymentComplementCommandHandler : IRequestHandler<IssuePaymentComplementCommand, Result<PaymentComplementDto>>
{
    private readonly ISender _sender;
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

    public IssuePaymentComplementCommandHandler(
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

    public async Task<Result<PaymentComplementDto>> Handle(IssuePaymentComplementCommand request, CancellationToken cancellationToken)
    {
        await PaymentComplements.EnsureVisiblePaymentAsync(_context, request.OrderPaymentId, cancellationToken);
        if (!_gateway.IsConfigured)
            return Result.Failure<PaymentComplementDto>(InvoicingSettings.NotAvailableMessage);

        var requested = await _sender.Send(new RequestPaymentComplementCommand(request.OrderPaymentId), cancellationToken);
        if (requested.IsFailure)
            return Result.Failure<PaymentComplementDto>(requested.Error!);

        var complement = await _stamper.StampAsync(_context.PaymentComplements, requested.Value, cancellationToken);
        return Result.Success(PaymentComplementDto.From(complement));
    }
}

/// <summary>
/// The complement is fixed before the PAC is asked (sent by <see cref="IssuePaymentComplementCommand"/>): its folio, its
/// installment, the balance before it and its CFDI, in one transaction that holds the business's invoicing lock. Answers
/// the new complement's id.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RequestPaymentComplementCommand(Guid OrderPaymentId) : IRequest<Result<Guid>>, ITransactionalRequest;

public class RequestPaymentComplementCommandHandler : IRequestHandler<RequestPaymentComplementCommand, Result<Guid>>
{
    private readonly PaymentComplementRequester _requester;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public RequestPaymentComplementCommandHandler(
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
        _requester = new PaymentComplementRequester(context, tenant, currentUser, gateway, invoicingLock, folios, clock);
    }

    public async Task<Result<Guid>> Handle(RequestPaymentComplementCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<Guid>(InvoicingSettings.NotAvailableMessage);
        await PaymentComplements.EnsureVisiblePaymentAsync(_context, request.OrderPaymentId, cancellationToken);
        return await _requester.RequestAsync(request.OrderPaymentId, userId, cancellationToken);
    }
}

/// <summary>
/// The complement TienditApp issues on its own for a payment of an invoice in installments that never had one (sent by
/// <see cref="IssuePendingPaymentComplementsCommand"/>, in the business's scope): the same request, without a person.
/// </summary>
[AllowAnonymousRequest("Lo envía el worker de facturación (sistema), sin usuario, en el alcance del negocio.")]
public record RequestAutomaticPaymentComplementCommand(Guid OrderPaymentId) : IRequest<Result<Guid>>, ITransactionalRequest;

public class RequestAutomaticPaymentComplementCommandHandler : IRequestHandler<RequestAutomaticPaymentComplementCommand, Result<Guid>>
{
    private readonly PaymentComplementRequester _requester;

    public RequestAutomaticPaymentComplementCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IInvoicingGateway gateway,
        IInvoicingLock invoicingLock,
        IFolioGenerator folios,
        IDateTimeProvider clock)
    {
        _requester = new PaymentComplementRequester(context, tenant, currentUser, gateway, invoicingLock, folios, clock);
    }

    public Task<Result<Guid>> Handle(RequestAutomaticPaymentComplementCommand request, CancellationToken cancellationToken)
        => _requester.RequestAsync(request.OrderPaymentId, requestedByUserId: null, cancellationToken);
}

/// <summary>
/// Asks the PAC again for a complement that is still «Timbrando» (its answer did not arrive), with the same CFDI and key: it
/// never stamps a second one.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RetryPaymentComplementCommand(Guid ComplementId) : IRequest<Result<PaymentComplementDto>>;

public class RetryPaymentComplementCommandValidator : AbstractValidator<RetryPaymentComplementCommand>
{
    public RetryPaymentComplementCommandValidator()
    {
        RuleFor(x => x.ComplementId).NotEmpty();
    }
}

public class RetryPaymentComplementCommandHandler : IRequestHandler<RetryPaymentComplementCommand, Result<PaymentComplementDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

    public RetryPaymentComplementCommandHandler(
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

    public async Task<Result<PaymentComplementDto>> Handle(RetryPaymentComplementCommand request, CancellationToken cancellationToken)
    {
        await PaymentComplements.EnsureVisibleComplementAsync(_context, request.ComplementId, cancellationToken);
        if (!_gateway.IsConfigured)
            return Result.Failure<PaymentComplementDto>(InvoicingSettings.NotAvailableMessage);
        if (await _stamper.RetryRefusalAsync(_context.PaymentComplements, request.ComplementId, cancellationToken) is { } refusal)
            return Result.Failure<PaymentComplementDto>(refusal);

        var complement = await _stamper.StampAsync(_context.PaymentComplements, request.ComplementId, cancellationToken);
        return Result.Success(PaymentComplementDto.From(complement));
    }
}

/// <summary>
/// The owner or an administrator cancels a stamped complement with the SAT: because it has errors and nothing replaces it
/// (02), or because the payment did not happen (03). The PAC is asked first; nothing changes here if it refuses. The SAT may
/// need the customer to accept it: then it stays «Cancelándose» until the SAT answers.
/// </summary>
[RequiresTenantManager]
public record CancelPaymentComplementCommand(Guid ComplementId, CfdiCancellationMotive Motive) : IRequest<Result<PaymentComplementDto>>;

public class CancelPaymentComplementCommandValidator : AbstractValidator<CancelPaymentComplementCommand>
{
    public CancelPaymentComplementCommandValidator()
    {
        RuleFor(x => x.ComplementId).NotEmpty();
        RuleFor(x => x.Motive)
            .Must(motive => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut)
            .WithMessage("Cancela el complemento por errores sin relación (02) o porque el pago no se llevó a cabo (03).");
    }
}

public class CancelPaymentComplementCommandHandler : IRequestHandler<CancelPaymentComplementCommand, Result<PaymentComplementDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public CancelPaymentComplementCommandHandler(
        IApplicationDbContext context, ICurrentUserService currentUser, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<PaymentComplementDto>> Handle(CancelPaymentComplementCommand request, CancellationToken cancellationToken)
    {
        var complement = await PaymentComplements.EnsureVisibleComplementAsync(_context, request.ComplementId, cancellationToken);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<PaymentComplementDto>(InvoicingSettings.NotAvailableMessage);
        if (complement.Status == OrderInvoiceStatus.Cancelling && complement.CancellationMotive == request.Motive)
            return Result.Success(PaymentComplementDto.From(complement));
        if (complement.Status != OrderInvoiceStatus.Stamped)
            return Result.Failure<PaymentComplementDto>(complement.Status == OrderInvoiceStatus.Cancelled
                ? "Este complemento ya está cancelado."
                : "Solo se cancela un complemento timbrado.");
        if (!_gateway.IsConfigured)
            return Result.Failure<PaymentComplementDto>(InvoicingSettings.NotAvailableMessage);
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is not { HasCertificate: true })
            return Result.Failure<PaymentComplementDto>(CancelOrderInvoiceCommandHandler.NoCertificateToCancelMessage);

        // The issuer the complement was stamped for: the PAC cancels it with the CSD it keeps for that RFC.
        var issuerRfc = CfdiDocument.FromJson(complement.Document).Issuer.Rfc;
        CfdiCancellationOutcome outcome;
        try
        {
            outcome = await _gateway.CancelAsync(issuerRfc, complement.Uuid!, request.Motive, null, cancellationToken);
        }
        catch (Exception ex) when (ex is InvoicingGatewayRefusedException or InvoicingGatewayUnavailableException)
        {
            return Result.Failure<PaymentComplementDto>(ex.Message);
        }

        // The SAT took it: what is written here must follow, even if someone changed the complement meanwhile.
        for (var attempt = 1; ; attempt++)
        {
            var tracked = await _context.PaymentComplements.FirstAsync(c => c.Id == request.ComplementId, cancellationToken);
            var now = _clock.UtcNow;
            if (tracked.Status == OrderInvoiceStatus.Stamped)
                tracked.RequestCancellation(request.Motive, userId, now);
            if (outcome == CfdiCancellationOutcome.Cancelled && tracked.Status == OrderInvoiceStatus.Cancelling)
                tracked.Cancel(now);
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tracked.TenantId,
                entityName: nameof(PaymentComplement),
                entityId: tracked.Id.ToString(),
                action: "PaymentComplementCancellationRequested",
                performedBy: _currentUser.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { tracked.Number, tracked.Uuid, Motive = $"{(int)request.Motive:00}", Outcome = outcome.ToString() })));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success(PaymentComplementDto.From(tracked));
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                _context.ChangeTracker.Clear();
            }
        }
    }
}

/// <summary>
/// The stamped XML or the PDF of a complement (whoever manages the orders). Without a kept PDF, the PAC renders it from the
/// XML. Another business's complement answers like a missing one.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record GetPaymentComplementFileQuery(Guid ComplementId, OrderInvoiceFileKind Kind) : IRequest<Result<PrivateFileDownload>>;

public class GetPaymentComplementFileQueryHandler : IRequestHandler<GetPaymentComplementFileQuery, Result<PrivateFileDownload>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPrivateFileStorage _storage;
    private readonly IInvoicingGateway _gateway;
    private readonly ILogger<GetPaymentComplementFileQueryHandler> _logger;

    public GetPaymentComplementFileQueryHandler(
        IApplicationDbContext context,
        IPrivateFileStorage storage,
        IInvoicingGateway gateway,
        ILogger<GetPaymentComplementFileQueryHandler> logger)
    {
        _context = context;
        _storage = storage;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<PrivateFileDownload>> Handle(GetPaymentComplementFileQuery request, CancellationToken cancellationToken)
    {
        var complement = await _context.PaymentComplements.AsNoTracking()
            .Where(c => c.Id == request.ComplementId)
            .Select(c => new { c.TenantId, c.OrderId, c.Serie, c.Folio, c.Uuid, c.XmlFileKey, c.PdfFileKey })
            .FirstOrDefaultAsync(cancellationToken);
        if (complement?.XmlFileKey is null || !await OrderInvoicing.SeesOrderAsync(_context, complement.OrderId, cancellationToken))
            return Result.Failure<PrivateFileDownload>(GetOrderInvoiceFileQueryHandler.NotFoundMessage);

        return await InvoiceFiles.DownloadAsync(
            _storage, _gateway, _logger, request.ComplementId, $"{complement.Serie}-{complement.Folio}_{complement.Uuid}",
            complement.XmlFileKey, complement.PdfFileKey, request.Kind, complement.TenantId, cancellationToken);
    }
}

/// <summary>
/// Issues the complements a business owes (the invoicing worker sends it in the business's scope, Fase 28C): each payment of
/// an invoice in installments that is stamped and that never had a complement for it, the oldest first, as long as the
/// payment has a form of the SAT's. A refused or cancelled complement is issued again only by a person. Up to
/// <see cref="IssuePendingPaymentComplementsCommandHandler.MaxPerPass"/> are issued in a pass; the ones that cannot be issued
/// yet (the RFC of Mi negocio changed, say) are passed over, so they never hold up the newer ones behind them.
/// </summary>
[AllowAnonymousRequest("Lo envía el worker de facturación (sistema), sin usuario, en el alcance del negocio.")]
public record IssuePendingPaymentComplementsCommand : IRequest<Result<int>>;

public class IssuePendingPaymentComplementsCommandHandler : IRequestHandler<IssuePendingPaymentComplementsCommand, Result<int>>
{
    public const int MaxPerPass = 20;
    /// <summary>The payments a pass looks at, the ones that fail included.</summary>
    public const int MaxCandidatesPerPass = 100;

    private readonly ISender _sender;
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;
    private readonly ILogger<OrderInvoiceStamper> _logger;

    public IssuePendingPaymentComplementsCommandHandler(
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

    /// <returns>Complements that ended up stamped.</returns>
    public async Task<Result<int>> Handle(IssuePendingPaymentComplementsCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Success(0);

        var pending = await PaymentComplements.Pending(_context.OrderPayments, _context.OrderInvoices, _context.PaymentComplements)
            .OrderBy(p => p.PaidAt)
            .ThenBy(p => p.Id)
            .Select(p => p.Id)
            .Take(MaxCandidatesPerPass)
            .ToListAsync(cancellationToken);

        var stamped = 0;
        var issued = 0;
        string? firstProblem = null;
        var problems = 0;
        foreach (var paymentId in pending)
        {
            if (issued >= MaxPerPass)
                break;
            try
            {
                var requested = await _sender.Send(new RequestAutomaticPaymentComplementCommand(paymentId), cancellationToken);
                _context.ChangeTracker.Clear();
                if (requested.IsFailure)
                {
                    problems++;
                    firstProblem ??= requested.Error;
                    continue;
                }
                issued++;
                var complement = await _stamper.StampAsync(_context.PaymentComplements, requested.Value, cancellationToken);
                if (complement.Status == OrderInvoiceStatus.Stamped)
                    stamped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One that fails (its file, its XML, the database) does not hold up the others: the next pass asks again.
                _logger.LogError("The complement of payment {PaymentId} could not be issued in this pass: {Error}", paymentId, ex.GetType().Name);
            }
            _context.ChangeTracker.Clear();
        }
        // Until the business can issue them (its CSD, Mi negocio), the next pass tries again: said once per pass.
        if (problems > 0)
            _logger.LogWarning("{Count} payment complements wait for the business: {Problem}", problems, firstProblem);
        return Result.Success(stamped);
    }
}

/// <summary>What issuing a payment complement asks for (Fase 28C): the payment, its invoice and what was paid before.</summary>
internal sealed class PaymentComplementRequester(
    IApplicationDbContext context,
    ICurrentTenantService tenant,
    ICurrentUserService currentUser,
    IInvoicingGateway gateway,
    IInvoicingLock invoicingLock,
    IFolioGenerator folios,
    IDateTimeProvider clock)
{
    public async Task<Result<Guid>> RequestAsync(Guid orderPaymentId, Guid? requestedByUserId, CancellationToken cancellationToken)
    {
        if (!gateway.IsConfigured)
            return Result.Failure<Guid>(InvoicingSettings.NotAvailableMessage);

        var tenantId = tenant.TenantId;
        // One complement at a time per business: its installment and the balance before it count the ones issued before it.
        await invoicingLock.AcquireAsync(tenantId, cancellationToken);
        var payment = await context.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == orderPaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<Guid>(PaymentComplements.NoInvoiceInInstallmentsMessage);

        // The order's invoice now: a replaced one waits for its cancellation, and the complements go to its replacement.
        var invoice = await context.OrderInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == payment.OrderId && i.ReplacedByInvoiceId == null
                && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                    || i.Status == OrderInvoiceStatus.Cancelling), cancellationToken);
        var problem = PaymentComplements.FormProblem(payment.Method) ?? PaymentComplements.InvoiceProblem(invoice);
        if (problem is not null)
            return Result.Failure<Guid>(problem);

        // A live complement of the payment, of this invoice or of one cancelled since: never two that stand at once.
        var existing = await context.PaymentComplements.AsNoTracking()
            .Where(c => c.OrderPaymentId == payment.Id
                && (c.Status == OrderInvoiceStatus.Stamping || c.Status == OrderInvoiceStatus.Stamped
                    || c.Status == OrderInvoiceStatus.Cancelling))
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return Result.Failure<Guid>(PaymentComplements.LiveComplementProblem(existing));
        var issued = await context.PaymentComplements.AsNoTracking()
            .Where(c => c.OrderInvoiceId == invoice!.Id)
            .ToListAsync(cancellationToken);
        // What the business forgave with a credit note of this invoice lowers what it owes (Fase 28D).
        var writeOffNotes = (await CreditNotes.StandingAsync(context, invoice!, cancellationToken)).WriteOffNotes;

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
        if (PaymentComplements.DocumentProblem(invoice, invoiceDocument, issuer.Rfc) is { } documentProblem)
            return Result.Failure<Guid>(documentProblem);

        var position = PaymentComplements.PositionOf(invoice, issued, writeOffNotes, payment.Id);
        if (PaymentComplements.PositionProblem(invoice, position, payment.Amount) is { } positionProblem)
            return Result.Failure<Guid>(positionProblem);
        var installment = position.Installment;
        var previousBalance = position.PreviousBalance;
        // It settles the invoice when it pays what is left and every other payment's complement and every write-off's credit
        // note stand: then it takes what is left of each base and its IVA. While another one waits to be issued again, it
        // takes its share like any other.
        var settles = position.Owed == payment.Amount && position.OthersStand;
        var covered = issued.Where(c => c.IsLive).SelectMany(c => CfdiDocument.FromJson(c.Document).Payment!.Invoice.Transfers)
            .Concat(writeOffNotes.Where(n => n.IsLive).SelectMany(n => CfdiDocument.FromJson(n.Document).Transfers))
            .Select(PaymentComplements.Transfer)
            .ToList();

        IReadOnlyList<CfdiTransfer> transfers;
        try
        {
            transfers = PaymentComplementTaxes.Prorate(
                invoiceDocument.Transfers.Select(PaymentComplements.Transfer).ToList(),
                invoice.Total,
                payment.Amount,
                settles,
                covered);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(ex.Message);
        }

        var timeZone = await context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var serie = profile!.PaymentSerie;
        var folio = await folios.NextInvoiceFolioAsync(tenantId, serie, cancellationToken);
        var paidTransfers = transfers
            .Select(t => new CfdiDocumentTransfer(t.Base, CfdiTransfer.IvaTaxCode, t.FactorType, t.RateOrQuota, t.IsExempt ? null : t.Amount))
            .ToList();
        var document = CfdiDocument.ForPayment(
            new CfdiDocumentIssuer(issuer.Rfc!, issuer.Name!, issuer.FiscalRegime!),
            invoiceDocument.Receiver,
            postalCode!,
            serie,
            folio,
            TenantDateTimeResolver.LocalNow(now, timeZone),
            new CfdiDocumentPayment(
                TenantDateTimeResolver.LocalNow(payment.PaidAt, timeZone).ToString(CfdiDocument.DateFormat, CultureInfo.InvariantCulture),
                SatCatalog.PaymentComplementForm(payment.Method)!,
                payment.Amount,
                new CfdiDocumentPaidInvoice(
                    invoice.Uuid!,
                    invoice.Serie,
                    invoice.Folio.ToString(CultureInfo.InvariantCulture),
                    installment,
                    previousBalance,
                    payment.Amount,
                    previousBalance - payment.Amount,
                    SatCatalog.Code(paidTransfers.Count > 0 ? CfdiTaxObject.Subject : CfdiTaxObject.NotSubject),
                    paidTransfers)));

        var complement = PaymentComplement.Request(
            invoice, payment, serie, folio, installment, previousBalance, document.ToJson(), requestedByUserId, now);
        context.PaymentComplements.Add(complement);
        context.AuditLogs.Add(new AuditLog(
            tenantId: tenantId,
            entityName: nameof(PaymentComplement),
            entityId: complement.Id.ToString(),
            action: "PaymentComplementRequested",
            performedBy: currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new
            {
                complement.Number,
                Invoice = invoice.Number,
                complement.OrderPaymentId,
                complement.Installment,
                complement.Amount,
                complement.RemainingBalance,
                Automatic = requestedByUserId is null
            })));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone issued the complement of the same payment at the same time (the index of one live complement per
            // payment kept theirs): this one is undone whole, folio included. Anything else is thrown on.
            await context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await context.PaymentComplements.AsNoTracking().AnyAsync(
                    c => c.OrderPaymentId == orderPaymentId
                        && (c.Status == OrderInvoiceStatus.Stamping || c.Status == OrderInvoiceStatus.Stamped
                            || c.Status == OrderInvoiceStatus.Cancelling),
                    cancellationToken))
                return Result.Failure<Guid>(PaymentComplements.AlreadyIssuingMessage);
            throw;
        }

        return Result.Success(complement.Id);
    }
}

/// <summary>What deciding whether a payment gets a complement shares (Fase 28C).</summary>
public static class PaymentComplements
{
    public const string NoInvoiceInInstallmentsMessage =
        "Este pedido no tiene una factura en parcialidades (PPD) timbrada: los complementos de pago se emiten con ella.";
    public const string AlreadyIssuingMessage =
        "Otra persona está emitiendo el complemento de este cobro en este momento: actualiza la página.";
    public const string OtherFormMessage =
        "Este cobro se registró como «Otro», que no es una forma de pago del SAT: su complemento no se emite desde TienditApp. " +
        "Consúltalo con tu contador.";
    public const string CustomerBalanceMessage =
        "Este cobro se pagó con saldo a favor del cliente (un anticipo): su CFDI lo ve tu contador y TienditApp no emite su complemento.";

    public static string OtherRfcMessage(string invoiceNumber) =>
        $"El RFC de Mi negocio ya no es el de la factura {invoiceNumber}: su complemento lo emite el mismo RFC que la timbró.";

    public static string AboveBalanceMessage(string invoiceNumber) =>
        $"El cobro es mayor que lo que la factura {invoiceNumber} todavía debe después de los otros cobros con complemento.";

    public static string TooManyInstallmentsMessage(string invoiceNumber) =>
        $"La factura {invoiceNumber} ya tiene {PaymentComplement.MaxInstallment} parcialidades, las que acepta el SAT.";

    /// <summary>A payment without a form of the SAT's never gets a complement here.</summary>
    public static string? FormProblem(OrderPaymentMethod method) => SatCatalog.PaymentComplementForm(method) is not null
        ? null
        : method == OrderPaymentMethod.CustomerBalance ? CustomerBalanceMessage : OtherFormMessage;

    /// <summary>Why the order's live invoice takes no complement now, or null when it is in installments and stamped.</summary>
    public static string? InvoiceProblem(OrderInvoice? invoice) => invoice switch
    {
        null => NoInvoiceInInstallmentsMessage,
        { IsPaidInInstallments: false } => $"La factura {invoice.Number} se pagó en una exhibición (PUE): sus cobros no llevan complemento.",
        { Status: OrderInvoiceStatus.Stamping } => $"La factura {invoice.Number} todavía se está timbrando: el complemento se emite cuando quede timbrada.",
        { Status: OrderInvoiceStatus.Cancelling } => $"La factura {invoice.Number} espera su cancelación en el SAT: no se le emiten complementos.",
        _ => null
    };

    /// <summary>
    /// Why no complement of the stamped invoice in installments is issued from TienditApp at all, or null when they are: Mi
    /// negocio's RFC is no longer the one that stamped it, or it has an IVA rate the complement has no field for.
    /// </summary>
    public static string? DocumentProblem(OrderInvoice invoice, CfdiDocument document, string? businessRfc)
    {
        if (!string.Equals(businessRfc, document.Issuer.Rfc, StringComparison.Ordinal))
            return OtherRfcMessage(invoice.Number);
        return PaymentComplementTaxes.Accepts(document.Transfers.Select(Transfer)) ? null : PaymentComplementTaxes.RateNotAcceptedMessage;
    }

    /// <summary>
    /// Where a payment's complement goes among the ones of its invoice (ADR 0047): each payment that ever had a complement of
    /// it holds the installment of its latest one, and its amount, even while that one is refused or cancelled, because the
    /// payment was received and its complement is issued again. What the business forgave with a credit note (Fase 28D, ADR
    /// 0048) lowers what the invoice owes, from the installment after the one it was issued after; a refund's credit note
    /// does not, because the money it gave back was received first.
    /// </summary>
    /// <param name="Owed">What the invoice still owes after the other payments that hold an installment and the write-offs.</param>
    /// <param name="Installment">
    /// Its installment: the installments go in the order the payments got their first complement, and one issued again keeps
    /// its number while no live one took it, so the chain stays whole whatever is issued in between.
    /// </param>
    /// <param name="PreviousBalance">Where the payments of the installments before it, and the write-offs, left the invoice.</param>
    /// <param name="OthersStand">
    /// Whether the complement of every other payment that holds an installment, and the credit note of every write-off, stands.
    /// </param>
    public sealed record Position(decimal Owed, int Installment, decimal PreviousBalance, bool OthersStand);

    /// <param name="issued">Every complement of the invoice, whatever its status.</param>
    /// <param name="writeOffNotes">The credit note that holds each write-off of the invoice that still stands.</param>
    public static Position PositionOf(
        OrderInvoice invoice, IEnumerable<PaymentComplement> issued, IReadOnlyList<CreditNote> writeOffNotes, Guid orderPaymentId)
    {
        var all = issued.ToList();
        var held = all
            .GroupBy(c => c.OrderPaymentId)
            .Select(g => g.OrderByDescending(c => c.RequestedAt).First())
            .ToList();
        var earlier = held.FirstOrDefault(c => c.OrderPaymentId == orderPaymentId);
        var others = held.Where(c => c.OrderPaymentId != orderPaymentId).ToList();
        var installment = earlier is not null && all.Where(c => c.IsLive).All(c => c.Installment != earlier.Installment)
            ? earlier.Installment
            : held.Count == 0 ? 1 : held.Max(c => c.Installment) + 1;
        return new Position(
            invoice.Total - others.Sum(c => c.Amount) - writeOffNotes.Sum(n => n.Total),
            installment,
            invoice.Total - others.Where(c => c.Installment < installment).Sum(c => c.Amount)
                - writeOffNotes.Where(n => n.AfterInstallment < installment).Sum(n => n.Total),
            others.All(c => c.IsLive) && writeOffNotes.All(n => n.IsLive));
    }

    /// <summary>Why the payment does not fit where its complement goes, or null when it does.</summary>
    public static string? PositionProblem(OrderInvoice invoice, Position position, decimal amount)
    {
        if (amount > position.Owed)
            return AboveBalanceMessage(invoice.Number);
        return position.Installment > PaymentComplement.MaxInstallment ? TooManyInstallmentsMessage(invoice.Number) : null;
    }

    public static string LiveComplementProblem(PaymentComplement live) => live.Status switch
    {
        OrderInvoiceStatus.Stamping => $"El complemento {live.Number} de este cobro se está timbrando.",
        OrderInvoiceStatus.Cancelling => $"El complemento {live.Number} de este cobro espera su cancelación en el SAT.",
        _ => $"Este cobro ya tiene el complemento {live.Number}: cancélalo para emitir otro."
    };

    /// <summary>The 5th of the month after the payment, in the business's calendar (RMF 2.7.1.32).</summary>
    public static DateOnly DueBy(DateTimeOffset paidAt, string? timeZone)
    {
        var local = TenantDateTimeResolver.LocalNow(paidAt, timeZone);
        return new DateOnly(local.Year, local.Month, 1).AddMonths(1).AddDays(4);
    }

    /// <summary>A transfer of a CFDI as the proration reads it.</summary>
    public static CfdiTransfer Transfer(CfdiDocumentTransfer transfer)
    {
        var exempt = transfer.FactorType == "Exento";
        return new CfdiTransfer(transfer.Base, exempt, exempt ? 0m : (transfer.Rate ?? 0m) * 100m, transfer.Amount ?? 0m);
    }

    /// <summary>
    /// The payments of stamped invoices in installments that never had a complement for that invoice and have a form of the
    /// SAT's: what the worker issues. It takes the sets to read, so the worker can find the businesses with work first.
    /// </summary>
    public static IQueryable<OrderPayment> Pending(
        IQueryable<OrderPayment> payments, IQueryable<OrderInvoice> invoices, IQueryable<PaymentComplement> complements)
        => payments.Where(payment => payment.Method != OrderPaymentMethod.Other
            && payment.Method != OrderPaymentMethod.CustomerBalance
            && invoices.Any(invoice => invoice.TenantId == payment.TenantId && invoice.OrderId == payment.OrderId
                && invoice.Status == OrderInvoiceStatus.Stamped && invoice.ReplacedByInvoiceId == null
                && invoice.PaymentMethod == CfdiInvoice.PaidInInstallments
                && !complements.Any(c => c.OrderPaymentId == payment.Id && c.OrderInvoiceId == invoice.Id)));

    /// <summary>The payment, if the business sees its order (as the order's detail does); otherwise it is not there.</summary>
    public static async Task EnsureVisiblePaymentAsync(IApplicationDbContext context, Guid orderPaymentId, CancellationToken cancellationToken)
    {
        var orderId = await context.OrderPayments.AsNoTracking()
            .Where(p => p.Id == orderPaymentId)
            .Select(p => (Guid?)p.OrderId)
            .FirstOrDefaultAsync(cancellationToken);
        if (orderId is null || !await OrderInvoicing.SeesOrderAsync(context, orderId.Value, cancellationToken))
            throw new EntityNotFoundException(nameof(OrderPayment), orderPaymentId);
    }

    /// <summary>The complement, if the business sees its order; otherwise it is not there.</summary>
    public static async Task<PaymentComplement> EnsureVisibleComplementAsync(
        IApplicationDbContext context, Guid complementId, CancellationToken cancellationToken)
    {
        var complement = await context.PaymentComplements.AsNoTracking().FirstOrDefaultAsync(c => c.Id == complementId, cancellationToken);
        if (complement is null || !await OrderInvoicing.SeesOrderAsync(context, complement.OrderId, cancellationToken))
            throw new EntityNotFoundException(nameof(PaymentComplement), complementId);
        return complement;
    }

    /// <summary>
    /// The payments of an order that has an invoice in installments, with their complements and whether one can be issued
    /// now; none for an order without one. It says what issuing one would refuse (without the lock, which only orders who
    /// goes first), so the screen never waits for one that TienditApp cannot issue.
    /// </summary>
    public static async Task<List<OrderPaymentComplementsDto>> ForOrderAsync(
        IApplicationDbContext context,
        Order order,
        IReadOnlyList<OrderInvoice> invoices,
        InvoicingSettingsDto settings,
        CancellationToken cancellationToken)
    {
        if (!invoices.Any(i => i.IsPaidInInstallments))
            return [];

        var complements = await context.PaymentComplements.AsNoTracking()
            .Where(c => c.OrderId == order.Id)
            .OrderByDescending(c => c.RequestedAt)
            .ThenByDescending(c => c.Folio)
            .ToListAsync(cancellationToken);
        var timeZone = await context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == order.TenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var liveInvoice = invoices.FirstOrDefault(i => i.IsCurrent);
        var invoiceProblem = InvoiceProblem(liveInvoice) ?? InvoicingSettings.ReadinessProblem(settings);
        if (invoiceProblem is null)
            invoiceProblem = DocumentProblem(liveInvoice!, CfdiDocument.FromJson(liveInvoice!.Document), settings.Rfc);
        var ofLiveInvoice = complements.Where(c => c.OrderInvoiceId == liveInvoice?.Id).ToList();
        var writeOffNotes = invoiceProblem is null
            ? (await CreditNotes.StandingAsync(context, liveInvoice!, cancellationToken)).WriteOffNotes
            : [];

        return order.Payments
            .OrderBy(payment => payment.PaidAt)
            .ThenBy(payment => payment.Id)
            .Select(payment =>
            {
                var own = complements.Where(c => c.OrderPaymentId == payment.Id).ToList();
                var live = own.FirstOrDefault(c => c.IsLive);
                var problem = live is not null
                    ? null
                    : FormProblem(payment.Method) ?? invoiceProblem
                        ?? PositionProblem(liveInvoice!, PositionOf(liveInvoice!, ofLiveInvoice, writeOffNotes, payment.Id), payment.Amount);
                return new OrderPaymentComplementsDto
                {
                    OrderPaymentId = payment.Id,
                    PaidAt = payment.PaidAt,
                    Method = payment.Method,
                    Amount = payment.Amount,
                    DueBy = DueBy(payment.PaidAt, timeZone),
                    CanIssue = live is null && problem is null,
                    Problem = problem,
                    AwaitsAutomaticIssue = live is null && problem is null
                        && liveInvoice is not null && own.All(c => c.OrderInvoiceId != liveInvoice.Id),
                    Complements = own.Select(PaymentComplementDto.From).ToList()
                };
            })
            .ToList();
    }

    /// <summary>The complements of an invoice that stand with the SAT: while one does, the invoice is not cancelled.</summary>
    public static async Task<List<string>> LiveNumbersAsync(IApplicationDbContext context, Guid orderInvoiceId, CancellationToken cancellationToken)
        => (await context.PaymentComplements.AsNoTracking()
                .Where(c => c.OrderInvoiceId == orderInvoiceId
                    && (c.Status == OrderInvoiceStatus.Stamping || c.Status == OrderInvoiceStatus.Stamped
                        || c.Status == OrderInvoiceStatus.Cancelling))
                .OrderBy(c => c.Installment)
                .Select(c => new { c.Serie, c.Folio })
                .ToListAsync(cancellationToken))
            .Select(c => $"{c.Serie}-{c.Folio}")
            .ToList();
}
