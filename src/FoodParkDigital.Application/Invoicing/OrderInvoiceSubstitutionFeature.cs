using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// The owner or an administrator replaces a stamped invoice with errors (Fase 28G, ADR 0051): a new invoice of the same
/// order, with the fiscal data and the email written now and the CFDI Pedidos would stamp now, carries the old one's UUID
/// with the relation «Sustitución de los CFDI previos» (04). Its folio and CFDI are saved first, with the old one no longer
/// the order's invoice; the PAC stamps it after the commit; and once it is stamped the SAT is asked to cancel the old one
/// with motive 01 and the new one's UUID. When the PAC or the SAT is not ready, the invoicing worker asks again.
/// </summary>
[RequiresTenantManager]
public record SubstituteOrderInvoiceCommand(Guid InvoiceId, CfdiReceiverInput Receiver, bool SaveToCustomer, string? ReceiverEmail = null)
    : IRequest<Result<OrderInvoiceDto>>, IRequiresContentPermissions
{
    /// <summary>Keeping the fiscal data in the customer changes the customer: it needs ManageCustomers, as Clientes does.</summary>
    public IEnumerable<TenantPermission> RequiredPermissions() => OrderInvoicing.SavingToCustomer(SaveToCustomer);
}

public class SubstituteOrderInvoiceCommandValidator : AbstractValidator<SubstituteOrderInvoiceCommand>
{
    public SubstituteOrderInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Receiver).NotNull().WithMessage("Captura los datos fiscales del cliente.")
            .SetValidator(new CfdiReceiverInputValidator());
        RuleFor(x => x.ReceiverEmail).Must(OrderInvoicing.IsValidEmail).WithMessage(InvoiceEmailAddress.InvalidMessage);
    }
}

public class SubstituteOrderInvoiceCommandHandler : IRequestHandler<SubstituteOrderInvoiceCommand, Result<OrderInvoiceDto>>
{
    private readonly ISender _sender;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;
    private readonly ReplacedInvoiceCanceller _canceller;

    public SubstituteOrderInvoiceCommandHandler(
        ISender sender,
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _sender = sender;
        _currentUser = currentUser;
        _gateway = gateway;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
        _canceller = new ReplacedInvoiceCanceller(context, gateway, clock);
    }

    public async Task<Result<OrderInvoiceDto>> Handle(SubstituteOrderInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<OrderInvoiceDto>(InvoicingSettings.NotAvailableMessage);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<OrderInvoiceDto>(InvoicingSettings.NotAvailableMessage);

        var requested = await _sender.Send(
            new RequestOrderInvoiceSubstitutionCommand(request.InvoiceId, request.Receiver, request.SaveToCustomer, request.ReceiverEmail),
            cancellationToken);
        if (requested.IsFailure)
            return Result.Failure<OrderInvoiceDto>(requested.Error!);

        var replacement = await _stamper.StampAsync(requested.Value, cancellationToken);
        // Stamped: the old one is cancelled now, if the SAT already has the new one; otherwise the worker asks again (its
        // reason stays on the old one).
        if (replacement.Status == OrderInvoiceStatus.Stamped)
            await _canceller.CancelAsync(request.InvoiceId, userId, _currentUser.UserName ?? "system", cancellationToken);
        var dto = OrderInvoiceDto.From(replacement);
        dto.ReplacesInvoiceId = request.InvoiceId;
        return Result.Success(dto);
    }
}

/// <summary>
/// The invoice that replaces another is fixed before the PAC is asked (sent by <see cref="SubstituteOrderInvoiceCommand"/>),
/// in one transaction under the invoicing lock: the old one stops being the order's invoice and the new one gets its folio,
/// its key and its CFDI. Answers the new invoice's id.
/// </summary>
[RequiresTenantManager]
public record RequestOrderInvoiceSubstitutionCommand(
    Guid InvoiceId, CfdiReceiverInput Receiver, bool SaveToCustomer, string? ReceiverEmail = null)
    : IRequest<Result<Guid>>, ITransactionalRequest, IRequiresContentPermissions
{
    public IEnumerable<TenantPermission> RequiredPermissions() => OrderInvoicing.SavingToCustomer(SaveToCustomer);
}

public class RequestOrderInvoiceSubstitutionCommandValidator : AbstractValidator<RequestOrderInvoiceSubstitutionCommand>
{
    public RequestOrderInvoiceSubstitutionCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Receiver).NotNull().SetValidator(new CfdiReceiverInputValidator());
        RuleFor(x => x.ReceiverEmail).Must(OrderInvoicing.IsValidEmail).WithMessage(InvoiceEmailAddress.InvalidMessage);
    }
}

public class RequestOrderInvoiceSubstitutionCommandHandler : IRequestHandler<RequestOrderInvoiceSubstitutionCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly OrderInvoiceRequester _requester;

    public RequestOrderInvoiceSubstitutionCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IInvoicingLock invoicingLock,
        IFolioGenerator folios,
        IPhoneNormalizationService phones,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _phones = phones;
        _requester = new OrderInvoiceRequester(context, invoicingLock, folios, clock);
    }

    public async Task<Result<Guid>> Handle(RequestOrderInvoiceSubstitutionCommand request, CancellationToken cancellationToken)
    {
        var orderId = await _context.OrderInvoices.AsNoTracking()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => (Guid?)i.OrderId)
            .FirstOrDefaultAsync(cancellationToken);
        if (orderId is null || !await OrderInvoicing.SeesOrderAsync(_context, orderId.Value, cancellationToken))
            throw new EntityNotFoundException(nameof(OrderInvoice), request.InvoiceId);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<Guid>(InvoicingSettings.NotAvailableMessage);

        var requested = await _requester.RequestAsync(
            _tenant.TenantId,
            new OrderInvoiceRequest(orderId.Value, request.Receiver, request.ReceiverEmail, userId, _currentUser.UserName ?? "system")
            {
                Replaces = request.InvoiceId,
                BeforeSave = (order, invoice) => request.SaveToCustomer
                    ? OrderInvoicing.SaveToCustomerAsync(_context, _phones, _currentUser, request.Receiver, order, invoice, cancellationToken)
                    : Task.CompletedTask
            },
            cancellationToken);
        return requested.IsSuccess ? Result.Success(requested.Value.Id) : Result.Failure<Guid>(requested.Error!);
    }
}

/// <summary>
/// The owner or an administrator asks the SAT again to cancel with motive 01 an invoice whose replacement is stamped, when
/// the first request did not go through (the SAT did not have the replacement yet, or the PAC did not answer) and the worker
/// stopped asking, or to ask sooner.
/// </summary>
[RequiresTenantManager]
public record CancelReplacedOrderInvoiceCommand(Guid InvoiceId) : IRequest<Result<OrderInvoiceDto>>;

public class CancelReplacedOrderInvoiceCommandHandler : IRequestHandler<CancelReplacedOrderInvoiceCommand, Result<OrderInvoiceDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ReplacedInvoiceCanceller _canceller;

    public CancelReplacedOrderInvoiceCommandHandler(
        IApplicationDbContext context, ICurrentUserService currentUser, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _canceller = new ReplacedInvoiceCanceller(context, gateway, clock);
    }

    public async Task<Result<OrderInvoiceDto>> Handle(CancelReplacedOrderInvoiceCommand request, CancellationToken cancellationToken)
    {
        var orderId = await _context.OrderInvoices.AsNoTracking()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => (Guid?)i.OrderId)
            .FirstOrDefaultAsync(cancellationToken);
        if (orderId is null || !await OrderInvoicing.SeesOrderAsync(_context, orderId.Value, cancellationToken))
            throw new EntityNotFoundException(nameof(OrderInvoice), request.InvoiceId);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<OrderInvoiceDto>(InvoicingSettings.NotAvailableMessage);

        var cancelled = await _canceller.CancelAsync(request.InvoiceId, userId, _currentUser.UserName ?? "system", cancellationToken);
        return cancelled.IsSuccess
            ? Result.Success(OrderInvoiceDto.From(cancelled.Value))
            : Result.Failure<OrderInvoiceDto>(cancelled.Error!);
    }
}

/// <summary>
/// Asks the SAT to cancel with motive 01 an invoice another one replaces, once that one is stamped (Fase 28G), naming it as
/// the folio de sustitución. The PAC is asked first; when it refuses or does not answer, the replaced invoice keeps the reason
/// to be asked again. The SAT may need the customer to accept it: then it stays «Cancelándose» until the SAT answers, as any
/// other cancellation.
/// </summary>
internal sealed class ReplacedInvoiceCanceller
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public ReplacedInvoiceCanceller(IApplicationDbContext context, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _gateway = gateway;
        _clock = clock;
    }

    /// <returns>The replaced invoice as it ends up, or why its cancellation was not asked for.</returns>
    public async Task<Result<OrderInvoice>> CancelAsync(Guid invoiceId, Guid userId, string performedBy, CancellationToken cancellationToken)
    {
        var replaced = await _context.OrderInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (replaced?.ReplacedByInvoiceId is not { } replacementId)
            return Result.Failure<OrderInvoice>(OrderInvoiceSubstitutions.NotReplacedMessage);
        if (replaced.Status == OrderInvoiceStatus.Cancelled
            || (replaced.Status == OrderInvoiceStatus.Cancelling && replaced.CancellationMotive == CfdiCancellationMotive.ErrorsWithReplacement))
            return Result.Success(replaced);
        if (replaced.Status != OrderInvoiceStatus.Stamped)
            return Result.Failure<OrderInvoice>($"La factura {replaced.Number} espera su cancelación en el SAT.");
        var replacement = await _context.OrderInvoices.AsNoTracking().FirstAsync(i => i.Id == replacementId, cancellationToken);
        if (replacement.Status != OrderInvoiceStatus.Stamped || replacement.Uuid is null)
            return Result.Failure<OrderInvoice>(OrderInvoiceSubstitutions.ReplacementNotStampedMessage(replacement.Number));
        if (!_gateway.IsConfigured)
            return Result.Failure<OrderInvoice>(InvoicingSettings.NotAvailableMessage);
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is not { HasCertificate: true })
            return Result.Failure<OrderInvoice>(CancelOrderInvoiceCommandHandler.NoCertificateToCancelMessage);

        // The issuer the invoice was stamped for: the PAC cancels it with the CSD it keeps for that RFC.
        var issuerRfc = CfdiDocument.FromJson(replaced.Document).Issuer.Rfc;
        CfdiCancellationOutcome outcome;
        try
        {
            outcome = await _gateway.CancelAsync(
                issuerRfc, replaced.Uuid!, CfdiCancellationMotive.ErrorsWithReplacement, replacement.Uuid, cancellationToken);
        }
        catch (Exception ex) when (ex is InvoicingGatewayRefusedException or InvoicingGatewayUnavailableException)
        {
            await KeepFailureAsync(invoiceId, ex.Message, cancellationToken);
            return Result.Failure<OrderInvoice>(ex.Message);
        }

        // The SAT took it: what is written here must follow, even if someone changed the invoice meanwhile.
        for (var attempt = 1; ; attempt++)
        {
            var tracked = await _context.OrderInvoices.FirstAsync(i => i.Id == invoiceId, cancellationToken);
            var now = _clock.UtcNow;
            if (tracked.Status == OrderInvoiceStatus.Stamped)
                tracked.RequestCancellation(CfdiCancellationMotive.ErrorsWithReplacement, userId, now);
            if (outcome == CfdiCancellationOutcome.Cancelled && tracked.Status == OrderInvoiceStatus.Cancelling)
                tracked.Cancel(now);
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tracked.TenantId,
                entityName: nameof(OrderInvoice),
                entityId: tracked.Id.ToString(),
                action: "OrderInvoiceCancellationRequested",
                performedBy: performedBy,
                metadata: JsonSerializer.Serialize(new
                {
                    tracked.Number, tracked.Uuid, Motive = "01", ReplacedBy = replacement.Number, ReplacementUuid = replacement.Uuid,
                    Outcome = outcome.ToString()
                })));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success(tracked);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                _context.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// The replaced invoices whose replacement is stamped and that were never asked to be cancelled, while the SAT may still
    /// be receiving their replacement (the invoicing worker, in its cancellations pass).
    /// </summary>
    /// <returns>How many the SAT took.</returns>
    public async Task<int> CancelPendingAsync(int max, CancellationToken cancellationToken)
    {
        var since = _clock.UtcNow - OrderInvoiceSubstitutions.CancellationRetryWindow;
        var pending = await OrderInvoiceSubstitutions.WaitingForCancellation(_context.OrderInvoices.AsNoTracking(), since)
            .Join(_context.OrderInvoices.AsNoTracking(), i => i.ReplacedByInvoiceId, r => (Guid?)r.Id,
                (i, r) => new { i.Id, ReplacedBy = r.RequestedByUserId, r.StampedAt })
            .OrderBy(w => w.StampedAt)
            .Take(max)
            .ToListAsync(cancellationToken);

        var cancelled = 0;
        foreach (var waiting in pending)
        {
            // Asked for by whoever replaced it.
            if ((await CancelAsync(waiting.Id, waiting.ReplacedBy, "invoicing", cancellationToken)).IsSuccess)
                cancelled++;
            _context.ChangeTracker.Clear();
        }
        return cancelled;
    }

    private async Task KeepFailureAsync(Guid invoiceId, string message, CancellationToken cancellationToken)
    {
        var tracked = await _context.OrderInvoices.FirstAsync(i => i.Id == invoiceId, cancellationToken);
        if (tracked.Status != OrderInvoiceStatus.Stamped || tracked.ReplacedByInvoiceId is null)
            return;
        tracked.KeepAfterFailedReplacementCancellation(message);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone changed it meanwhile: the next attempt says why again.
            _context.ChangeTracker.Clear();
        }
    }
}

/// <summary>What replacing an invoice with errors asks of it and of its order (Fase 28G).</summary>
public static class OrderInvoiceSubstitutions
{
    /// <summary>
    /// How long after its replacement was stamped the worker keeps asking the SAT to cancel a replaced invoice (the SAT
    /// receives a CFDI within 72 hours); after that only a person does.
    /// </summary>
    public static readonly TimeSpan CancellationRetryWindow = TimeSpan.FromHours(72);

    public const string NotCurrentMessage = "Solo se sustituye la factura vigente del pedido.";
    public const string NotReplacedMessage = "Esta factura no tiene otra que la sustituya.";

    public static string ChangedMessage(string number) =>
        $"La factura {number} cambió mientras la sustituías: actualiza la página e inténtalo otra vez.";

    public static string ReplacementNotStampedMessage(string number) =>
        $"La factura {number}, que la sustituye, todavía no está timbrada: se cancela cuando quede timbrada.";

    public static string LiveComplementsMessage(IReadOnlyList<string> complements) =>
        $"Cancela primero los complementos de pago de esta factura ({string.Join(", ", complements)}): el SAT no cancela una " +
        "factura en parcialidades, ni la sustituye, mientras sus pagos tengan complementos vigentes.";

    public static string LiveCreditNotesMessage(IReadOnlyList<string> notes) =>
        $"Cancela primero las notas de crédito de esta factura ({string.Join(", ", notes)}): el SAT no cancela una factura, ni la " +
        "sustituye, mientras tenga notas de crédito vigentes relacionadas.";

    public static string ReplacedFirstMessage(string replaced) =>
        $"Esta factura sustituye a la {replaced}, que todavía no se cancela: sustitúyela cuando el SAT cancele la {replaced}.";

    public static string OtherIssuerMessage(string number) =>
        $"La factura {number} se timbró con otro RFC del negocio: cancélala (02) y factura otra vez.";

    /// <summary>
    /// Why this invoice cannot be replaced now (the order's own problems are checked apart): it is the order's stamped invoice,
    /// without complements or credit notes in force, and of the RFC the business stamps with now.
    /// </summary>
    internal static async Task<string?> ProblemAsync(
        IApplicationDbContext context, OrderInvoice? invoice, string? issuerRfc, CancellationToken cancellationToken)
    {
        if (invoice is null)
            return NotCurrentMessage;
        var problem = invoice.Status switch
        {
            _ when invoice.ReplacedByInvoiceId is not null => OrderInvoice.AlreadyReplacedMessage,
            OrderInvoiceStatus.Stamping => $"La factura {invoice.Number} todavía se está timbrando.",
            OrderInvoiceStatus.Cancelling => $"La factura {invoice.Number} espera su cancelación en el SAT: no se sustituye.",
            OrderInvoiceStatus.Stamped => null,
            _ => "Solo se sustituye una factura timbrada."
        };
        if (problem is not null)
            return problem;
        // A replacement whose old invoice still stands waits for it: the old one's cancellation names this one.
        if (await context.OrderInvoices.AsNoTracking()
                .Where(i => i.ReplacedByInvoiceId == invoice.Id
                    && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                        || i.Status == OrderInvoiceStatus.Cancelling))
                .Select(i => new { i.Serie, i.Folio })
                .FirstOrDefaultAsync(cancellationToken) is { } replaced)
            return ReplacedFirstMessage($"{replaced.Serie}-{replaced.Folio}");
        if (invoice.IsPaidInInstallments
            && await PaymentComplements.LiveNumbersAsync(context, invoice.Id, cancellationToken) is { Count: > 0 } complements)
            return LiveComplementsMessage(complements);
        if (await CreditNotes.LiveNumbersAsync(context, invoice.Id, cancellationToken) is { Count: > 0 } notes)
            return LiveCreditNotesMessage(notes);
        return issuerRfc is not null && CfdiDocument.FromJson(invoice.Document).Issuer.Rfc != issuerRfc
            ? OtherIssuerMessage(invoice.Number)
            : null;
    }

    /// <summary>
    /// The replaced invoices still stamped and never asked to be cancelled, whose replacement was stamped since
    /// <paramref name="since"/>: the worker asks the SAT to cancel them with motive 01. One whose cancellation the customer
    /// refused, or whose replacement is older, waits for a person.
    /// </summary>
    public static IQueryable<OrderInvoice> WaitingForCancellation(IQueryable<OrderInvoice> invoices, DateTimeOffset since)
        => invoices.Where(i => i.ReplacedByInvoiceId != null && i.Status == OrderInvoiceStatus.Stamped && i.CancellationRequestedAt == null
            && invoices.Any(r => r.Id == i.ReplacedByInvoiceId && r.TenantId == i.TenantId
                && r.Status == OrderInvoiceStatus.Stamped && r.StampedAt > since));
}
