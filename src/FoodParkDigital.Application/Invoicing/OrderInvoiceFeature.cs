using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>An invoice of an order, as Pedidos shows it (Fase 28A, ADR 0045).</summary>
public class OrderInvoiceDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    /// <summary>Serie and folio, «F-12».</summary>
    public string Number { get; set; } = null!;
    public OrderInvoiceStatus Status { get; set; }
    public CfdiReceiverDto Receiver { get; set; } = null!;
    public string PaymentForm { get; set; } = null!;
    public string PaymentMethod { get; set; } = null!;
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>The folio fiscal, once stamped.</summary>
    public string? Uuid { get; set; }
    public DateTimeOffset? StampedAt { get; set; }
    public bool HasXml { get; set; }
    /// <summary>Its PDF is kept; without it, downloading the PDF asks the PAC for it.</summary>
    public bool HasPdf { get; set; }
    /// <summary>Why the PAC refused it, or why the SAT did not cancel it.</summary>
    public string? FailureMessage { get; set; }
    public DateTimeOffset? RefusedAt { get; set; }
    public CfdiCancellationMotive? CancellationMotive { get; set; }
    public DateTimeOffset? CancellationRequestedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    /// <summary>Where its CFDI go by email (Fase 28E): it, its complements and its credit notes; none, nowhere.</summary>
    public string? ReceiverEmail { get; set; }
    /// <summary>Its latest emails to the customer, the newest first.</summary>
    public List<CfdiEmailDto> Emails { get; set; } = [];
    /// <summary>The customer requested it online with the ticket (Fase 28F), not a person of the business.</summary>
    public bool RequestedByCustomer { get; set; }
    /// <summary>The invoice that replaces this one because it has errors (Fase 28G), and its number.</summary>
    public Guid? ReplacedByInvoiceId { get; set; }
    public string? ReplacedByNumber { get; set; }
    /// <summary>The invoice with errors this one replaces, and its number.</summary>
    public Guid? ReplacesInvoiceId { get; set; }
    public string? ReplacesNumber { get; set; }
    /// <summary>The order's stamped invoice can be replaced now by an owner or an administrator.</summary>
    public bool CanSubstitute { get; set; }
    /// <summary>Why the order's stamped invoice cannot be replaced now; null when it can, or for any other invoice.</summary>
    public string? SubstitutionProblem { get; set; }

    public static OrderInvoiceDto From(OrderInvoice invoice) => new()
    {
        Id = invoice.Id,
        OrderId = invoice.OrderId,
        Number = invoice.Number,
        Status = invoice.Status,
        Receiver = CfdiReceiverDto.From(invoice.Receiver),
        PaymentForm = invoice.PaymentForm,
        PaymentMethod = invoice.PaymentMethod,
        SubTotal = invoice.SubTotal,
        TaxTotal = invoice.TaxTotal,
        Total = invoice.Total,
        RequestedAt = invoice.RequestedAt,
        Uuid = invoice.Uuid,
        StampedAt = invoice.StampedAt,
        HasXml = invoice.XmlFileKey is not null,
        HasPdf = invoice.PdfFileKey is not null,
        FailureMessage = invoice.FailureMessage,
        RefusedAt = invoice.RefusedAt,
        CancellationMotive = invoice.CancellationMotive,
        CancellationRequestedAt = invoice.CancellationRequestedAt,
        CancelledAt = invoice.CancelledAt,
        ReceiverEmail = invoice.ReceiverEmail,
        RequestedByCustomer = invoice.RequestedByCustomer,
        ReplacedByInvoiceId = invoice.ReplacedByInvoiceId
    };

    /// <summary>The order's invoices with the numbers of the ones that replace each other (Fase 28G).</summary>
    public static List<OrderInvoiceDto> FromAll(IReadOnlyList<OrderInvoice> invoices)
    {
        var dtos = invoices.Select(From).ToList();
        var byId = dtos.ToDictionary(dto => dto.Id);
        foreach (var replaced in dtos.Where(dto => dto.ReplacedByInvoiceId is not null))
        {
            if (!byId.TryGetValue(replaced.ReplacedByInvoiceId!.Value, out var replacement))
                continue;
            replaced.ReplacedByNumber = replacement.Number;
            replacement.ReplacesInvoiceId = replaced.Id;
            replacement.ReplacesNumber = replaced.Number;
        }
        return dtos;
    }
}

/// <summary>What the detail of an order shows of its invoicing.</summary>
public class OrderInvoicingDto
{
    /// <summary>Whether the platform invoices (it has its PAC).</summary>
    public bool Available { get; set; }
    /// <summary>The PAC's test environment: invoices without fiscal effect.</summary>
    public bool Sandbox { get; set; }
    /// <summary>Whether the order can be invoiced now.</summary>
    public bool CanInvoice { get; set; }
    /// <summary>Why not (the business's settings or the order), when it cannot.</summary>
    public string? Problem { get; set; }
    /// <summary>The receiver to propose: the customer's fiscal data, or the one of the order's last invoice.</summary>
    public CfdiReceiverDto? ProposedReceiver { get; set; }
    /// <summary>
    /// The email to propose for its CFDI (Fase 28E), as the receiver is: the customer's «Correo para facturas», the one of the
    /// order's last invoice, or the customer's email.
    /// </summary>
    public string? ProposedEmail { get; set; }
    /// <summary>The customer with the order's phone, whose fiscal data the invoice can keep.</summary>
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    /// <summary>The order's invoices, the newest first (cancelled and refused ones included).</summary>
    public List<OrderInvoiceDto> Invoices { get; set; } = [];
    /// <summary>
    /// Its payments with their complements, when it has an invoice in installments (Fase 28C); none otherwise.
    /// </summary>
    public List<OrderPaymentComplementsDto> Payments { get; set; } = [];
    /// <summary>
    /// Its refunds and write-offs with their credit notes, when it has had an invoice (Fase 28D); none otherwise.
    /// </summary>
    public List<OrderAdjustmentDto> Adjustments { get; set; } = [];
}

/// <summary>The invoicing of an order: whether it can be invoiced, whom to, and its invoices.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record GetOrderInvoicingQuery(Guid OrderId) : IRequest<OrderInvoicingDto>;

public class GetOrderInvoicingQueryHandler : IRequestHandler<GetOrderInvoicingQuery, OrderInvoicingDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IInvoicingGateway _gateway;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;

    public GetOrderInvoicingQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        IInvoicingGateway gateway,
        IPhoneNormalizationService phones,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _gateway = gateway;
        _phones = phones;
        _clock = clock;
    }

    public async Task<OrderInvoicingDto> Handle(GetOrderInvoicingQuery request, CancellationToken cancellationToken)
    {
        var order = await OrderInvoicing.LoadOrderAsync(_context, request.OrderId, cancellationToken);
        var invoices = await _context.OrderInvoices.AsNoTracking()
            .Where(i => i.OrderId == order.Id)
            .OrderByDescending(i => i.RequestedAt)
            .ThenByDescending(i => i.Folio)
            .ToListAsync(cancellationToken);
        var customer = await OrderInvoicing.CustomerAsync(_context, _phones, order.CustomerPhone, tracked: false, cancellationToken);

        var settings = await InvoicingSettings.ReadAsync(_context, _tenant.TenantId, _gateway, _clock.UtcNow, cancellationToken);
        var problem = InvoicingSettings.ReadinessProblem(settings)
            ?? OrderInvoicing.LiveInvoiceProblem(invoices.FirstOrDefault(i => i.IsCurrent))
            ?? await OrderInvoicing.GlobalInvoiceProblemAsync(_context, order.Id, cancellationToken)
            ?? await OrderInvoicing.OrderProblemAsync(_context, order, cancellationToken);

        var last = invoices.FirstOrDefault();
        var payments = await PaymentComplements.ForOrderAsync(_context, order, invoices, settings, cancellationToken);
        var adjustments = await CreditNotes.ForOrderAsync(_context, order, invoices, settings, cancellationToken);
        var invoiceDtos = OrderInvoiceDto.FromAll(invoices);
        // The order's stamped invoice: whether an owner or an administrator can replace it now (Fase 28G).
        if (invoices.FirstOrDefault(i => i.IsCurrent) is { Status: OrderInvoiceStatus.Stamped } current)
        {
            var dto = invoiceDtos.First(i => i.Id == current.Id);
            dto.SubstitutionProblem = InvoicingSettings.ReadinessProblem(settings)
                ?? await OrderInvoiceSubstitutions.ProblemAsync(_context, current, settings.Rfc, cancellationToken)
                ?? await OrderInvoicing.OrderProblemAsync(_context, order, cancellationToken);
            dto.CanSubstitute = dto.SubstitutionProblem is null;
        }
        var emails = await CfdiEmails.LatestAsync(_context, invoices.Select(i => i.Id).ToList(), cancellationToken);
        foreach (var invoice in invoiceDtos)
            invoice.Emails = emails.GetValueOrDefault(invoice.Id) ?? [];
        foreach (var complement in payments.SelectMany(p => p.Complements))
            complement.Emails = emails.GetValueOrDefault(complement.Id) ?? [];
        foreach (var note in adjustments.SelectMany(a => a.CreditNotes))
            note.Emails = emails.GetValueOrDefault(note.Id) ?? [];
        return new OrderInvoicingDto
        {
            Available = settings.Available,
            Sandbox = settings.Sandbox,
            CanInvoice = problem is null,
            Problem = problem,
            ProposedReceiver = customer?.FiscalData is { } fiscalData
                ? CfdiReceiverDto.From(fiscalData)
                : last is null ? null : CfdiReceiverDto.From(last.Receiver),
            ProposedEmail = customer?.InvoiceEmail
                ?? last?.ReceiverEmail
                ?? (InvoiceEmailAddress.IsValid(customer?.Email?.Trim() ?? string.Empty) ? customer!.Email!.Trim() : null),
            CustomerId = customer?.Id,
            CustomerName = customer?.Name,
            Invoices = invoiceDtos,
            Payments = payments,
            Adjustments = adjustments
        };
    }
}

/// <summary>
/// Stamps the invoice of an order to a customer with an RFC (whoever manages the orders): paid in full in one exhibition, or
/// in installments (PPD, Fase 28C) when it is not, and then each payment gets its complement. Its folio and CFDI are saved
/// first (<see cref="RequestOrderInvoiceCommand"/>), and the PAC is asked after the commit, never inside the transaction.
/// When the PAC does not answer, the invoice stays «Timbrando» and is asked again with the same key. With an email, the
/// customer gets its XML and PDF once it is stamped, and those of its complements and credit notes (Fase 28E).
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record IssueOrderInvoiceCommand(Guid OrderId, CfdiReceiverInput Receiver, bool SaveToCustomer, string? ReceiverEmail = null)
    : IRequest<Result<OrderInvoiceDto>>, IRequiresContentPermissions
{
    /// <summary>Keeping the fiscal data in the customer changes the customer: it needs ManageCustomers, as Clientes does.</summary>
    public IEnumerable<TenantPermission> RequiredPermissions() => OrderInvoicing.SavingToCustomer(SaveToCustomer);
}

public class IssueOrderInvoiceCommandValidator : AbstractValidator<IssueOrderInvoiceCommand>
{
    public IssueOrderInvoiceCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Receiver).NotNull().WithMessage("Captura los datos fiscales del cliente.")
            .SetValidator(new CfdiReceiverInputValidator());
        RuleFor(x => x.ReceiverEmail).Must(OrderInvoicing.IsValidEmail).WithMessage(InvoiceEmailAddress.InvalidMessage);
    }
}

public class IssueOrderInvoiceCommandHandler : IRequestHandler<IssueOrderInvoiceCommand, Result<OrderInvoiceDto>>
{
    private readonly ISender _sender;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

    public IssueOrderInvoiceCommandHandler(
        ISender sender,
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _sender = sender;
        _gateway = gateway;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
    }

    public async Task<Result<OrderInvoiceDto>> Handle(IssueOrderInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<OrderInvoiceDto>(InvoicingSettings.NotAvailableMessage);

        var requested = await _sender.Send(
            new RequestOrderInvoiceCommand(request.OrderId, request.Receiver, request.SaveToCustomer, request.ReceiverEmail), cancellationToken);
        if (requested.IsFailure)
            return Result.Failure<OrderInvoiceDto>(requested.Error!);

        var invoice = await _stamper.StampAsync(requested.Value, cancellationToken);
        return Result.Success(OrderInvoiceDto.From(invoice));
    }
}

/// <summary>
/// The invoice is fixed before the PAC is asked (sent by <see cref="IssueOrderInvoiceCommand"/>): its folio, its date, its
/// key and the CFDI built from the order's draft, in one transaction, so a refused request gives the folio back. Answers
/// the new invoice's id.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RequestOrderInvoiceCommand(Guid OrderId, CfdiReceiverInput Receiver, bool SaveToCustomer, string? ReceiverEmail = null)
    : IRequest<Result<Guid>>, ITransactionalRequest, IRequiresContentPermissions
{
    public IEnumerable<TenantPermission> RequiredPermissions() => OrderInvoicing.SavingToCustomer(SaveToCustomer);
}

public class RequestOrderInvoiceCommandValidator : AbstractValidator<RequestOrderInvoiceCommand>
{
    public RequestOrderInvoiceCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Receiver).NotNull().SetValidator(new CfdiReceiverInputValidator());
        RuleFor(x => x.ReceiverEmail).Must(OrderInvoicing.IsValidEmail).WithMessage(InvoiceEmailAddress.InvalidMessage);
    }
}

public class RequestOrderInvoiceCommandHandler : IRequestHandler<RequestOrderInvoiceCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly OrderInvoiceRequester _requester;

    public RequestOrderInvoiceCommandHandler(
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

    public async Task<Result<Guid>> Handle(RequestOrderInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<Guid>(InvoicingSettings.NotAvailableMessage);

        var requested = await _requester.RequestAsync(
            _tenant.TenantId,
            new OrderInvoiceRequest(request.OrderId, request.Receiver, request.ReceiverEmail, userId, _currentUser.UserName ?? "system")
            {
                BeforeSave = (order, invoice) => request.SaveToCustomer
                    ? OrderInvoicing.SaveToCustomerAsync(_context, _phones, _currentUser, request.Receiver, order, invoice, cancellationToken)
                    : Task.CompletedTask
            },
            cancellationToken);
        return requested.IsSuccess ? Result.Success(requested.Value.Id) : Result.Failure<Guid>(requested.Error!);
    }
}

/// <summary>
/// Asks the PAC again for an invoice that is still «Timbrando» (its answer did not arrive), with the same CFDI and key: it
/// never stamps a second one. Stamped, the invoice it replaces is cancelled with motive 01 right away (Fase 28G), as asked
/// for by whoever replaced it, instead of waiting for the worker's next pass.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RetryOrderInvoiceCommand(Guid InvoiceId) : IRequest<Result<OrderInvoiceDto>>;

public class RetryOrderInvoiceCommandHandler : IRequestHandler<RetryOrderInvoiceCommand, Result<OrderInvoiceDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;
    private readonly ReplacedInvoiceCanceller _canceller;

    public RetryOrderInvoiceCommandHandler(
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _context = context;
        _gateway = gateway;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
        _canceller = new ReplacedInvoiceCanceller(context, gateway, clock);
    }

    public async Task<Result<OrderInvoiceDto>> Handle(RetryOrderInvoiceCommand request, CancellationToken cancellationToken)
    {
        var orderId = await _context.OrderInvoices.AsNoTracking()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => (Guid?)i.OrderId)
            .FirstOrDefaultAsync(cancellationToken);
        if (orderId is null || !await OrderInvoicing.SeesOrderAsync(_context, orderId.Value, cancellationToken))
            throw new EntityNotFoundException(nameof(OrderInvoice), request.InvoiceId);
        if (!_gateway.IsConfigured)
            return Result.Failure<OrderInvoiceDto>(InvoicingSettings.NotAvailableMessage);

        var invoice = await _stamper.StampAsync(request.InvoiceId, cancellationToken);
        if (invoice is { Status: OrderInvoiceStatus.Stamped } stamped
            && await _context.OrderInvoices.AsNoTracking()
                .Where(i => i.ReplacedByInvoiceId == stamped.Id && i.Status == OrderInvoiceStatus.Stamped)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync(cancellationToken) is { } replacedId)
        {
            // When the SAT does not have it yet, the replaced one keeps the reason and the worker asks again.
            await _canceller.CancelAsync(replacedId, stamped.RequestedByUserId, "invoicing", cancellationToken);
        }
        return Result.Success(OrderInvoiceDto.From(invoice));
    }
}

/// <summary>
/// The owner or an administrator cancels a stamped invoice with the SAT: because it has errors and nothing replaces it (02),
/// or because the sale did not happen (03). The PAC is asked first; nothing changes here if it refuses. The SAT may need the
/// customer to accept it: then it stays «Cancelándose» until the SAT answers.
/// </summary>
[RequiresTenantManager]
public record CancelOrderInvoiceCommand(Guid InvoiceId, CfdiCancellationMotive Motive) : IRequest<Result<OrderInvoiceDto>>;

public class CancelOrderInvoiceCommandValidator : AbstractValidator<CancelOrderInvoiceCommand>
{
    public CancelOrderInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Motive)
            .Must(motive => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut)
            .WithMessage("Cancela por errores sin relación (02) o porque la venta no se llevó a cabo (03).");
    }
}

public class CancelOrderInvoiceCommandHandler : IRequestHandler<CancelOrderInvoiceCommand, Result<OrderInvoiceDto>>
{
    public const string NoCertificateToCancelMessage =
        "Para cancelar, el negocio necesita su certificado de sello digital (CSD) en Configuración › Facturación.";

    public static string LiveComplementsMessage(IReadOnlyList<string> complements) =>
        $"Cancela primero los complementos de pago de esta factura ({string.Join(", ", complements)}): el SAT no cancela una " +
        "factura en parcialidades mientras sus pagos tengan complementos vigentes.";

    public static string ReplacedFirstMessage(string replaced) =>
        $"Esta factura sustituye a la {replaced}, que todavía no se cancela: cancela primero la {replaced}, con relación (01) o sin ella (02).";

    public static string LiveCreditNotesMessage(IReadOnlyList<string> notes) =>
        $"Cancela primero las notas de crédito de esta factura ({string.Join(", ", notes)}): el SAT no cancela una factura " +
        "mientras tenga notas de crédito vigentes relacionadas.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public CancelOrderInvoiceCommandHandler(
        IApplicationDbContext context, ICurrentUserService currentUser, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<OrderInvoiceDto>> Handle(CancelOrderInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _context.OrderInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken);
        if (invoice is null || !await OrderInvoicing.SeesOrderAsync(_context, invoice.OrderId, cancellationToken))
            throw new EntityNotFoundException(nameof(OrderInvoice), request.InvoiceId);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<OrderInvoiceDto>(InvoicingSettings.NotAvailableMessage);
        if (invoice.Status == OrderInvoiceStatus.Cancelling && invoice.CancellationMotive == request.Motive)
            return Result.Success(OrderInvoiceDto.From(invoice));
        if (invoice.Status != OrderInvoiceStatus.Stamped)
            return Result.Failure<OrderInvoiceDto>(invoice.Status == OrderInvoiceStatus.Cancelled
                ? "Esta factura ya está cancelada."
                : "Solo se cancela una factura timbrada.");
        if (!_gateway.IsConfigured)
            return Result.Failure<OrderInvoiceDto>(InvoicingSettings.NotAvailableMessage);
        // The SAT does not cancel an invoice whose payments still have complements in force (Fase 28C): they go first.
        if (invoice.IsPaidInInstallments
            && await PaymentComplements.LiveNumbersAsync(_context, invoice.Id, cancellationToken) is { Count: > 0 } complements)
            return Result.Failure<OrderInvoiceDto>(LiveComplementsMessage(complements));
        // Nor one related to credit notes in force (Fase 28D).
        if (await CreditNotes.LiveNumbersAsync(_context, invoice.Id, cancellationToken) is { Count: > 0 } notes)
            return Result.Failure<OrderInvoiceDto>(LiveCreditNotesMessage(notes));
        // The one it replaces goes first (Fase 28G): its cancellation with motive 01 names this one.
        if (await _context.OrderInvoices.AsNoTracking()
                .Where(i => i.ReplacedByInvoiceId == invoice.Id
                    && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                        || i.Status == OrderInvoiceStatus.Cancelling))
                .Select(i => new { i.Serie, i.Folio })
                .FirstOrDefaultAsync(cancellationToken) is { } replaced)
            return Result.Failure<OrderInvoiceDto>(ReplacedFirstMessage($"{replaced.Serie}-{replaced.Folio}"));
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is not { HasCertificate: true })
            return Result.Failure<OrderInvoiceDto>(NoCertificateToCancelMessage);

        // The issuer the invoice was stamped for: the PAC cancels it with the CSD it keeps for that RFC.
        var issuerRfc = CfdiDocument.FromJson(invoice.Document).Issuer.Rfc;
        CfdiCancellationOutcome outcome;
        try
        {
            outcome = await _gateway.CancelAsync(issuerRfc, invoice.Uuid!, request.Motive, null, cancellationToken);
        }
        catch (InvoicingGatewayRefusedException ex)
        {
            return Result.Failure<OrderInvoiceDto>(ex.Message);
        }
        catch (InvoicingGatewayUnavailableException ex)
        {
            return Result.Failure<OrderInvoiceDto>(ex.Message);
        }

        // The SAT took it: what is written here must follow, even if someone changed the invoice meanwhile.
        for (var attempt = 1; ; attempt++)
        {
            var tracked = await _context.OrderInvoices.FirstAsync(i => i.Id == request.InvoiceId, cancellationToken);
            var now = _clock.UtcNow;
            if (tracked.Status == OrderInvoiceStatus.Stamped)
                tracked.RequestCancellation(request.Motive, userId, now);
            if (outcome == CfdiCancellationOutcome.Cancelled && tracked.Status == OrderInvoiceStatus.Cancelling)
                tracked.Cancel(now);
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tracked.TenantId,
                entityName: nameof(OrderInvoice),
                entityId: tracked.Id.ToString(),
                action: "OrderInvoiceCancellationRequested",
                performedBy: _currentUser.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { tracked.Number, tracked.Uuid, Motive = $"{(int)request.Motive:00}", Outcome = outcome.ToString() })));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success(OrderInvoiceDto.From(tracked));
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                _context.ChangeTracker.Clear();
            }
        }
    }
}

public enum OrderInvoiceFileKind
{
    Xml,
    Pdf
}

/// <summary>
/// The stamped XML or the PDF of an invoice (whoever manages the orders). Without a kept PDF, the PAC renders it from the
/// XML. Another business's invoice answers like a missing one.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record GetOrderInvoiceFileQuery(Guid InvoiceId, OrderInvoiceFileKind Kind) : IRequest<Result<PrivateFileDownload>>;

public class GetOrderInvoiceFileQueryHandler : IRequestHandler<GetOrderInvoiceFileQuery, Result<PrivateFileDownload>>
{
    public const string NotFoundMessage = "La factura no tiene ese archivo.";
    public const string PdfUnavailableMessage = "El PDF no está disponible por ahora: descarga el XML, que es la factura.";

    private readonly IApplicationDbContext _context;
    private readonly IPrivateFileStorage _storage;
    private readonly IInvoicingGateway _gateway;
    private readonly ILogger<GetOrderInvoiceFileQueryHandler> _logger;

    public GetOrderInvoiceFileQueryHandler(
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

    public async Task<Result<PrivateFileDownload>> Handle(GetOrderInvoiceFileQuery request, CancellationToken cancellationToken)
    {
        var invoice = await _context.OrderInvoices.AsNoTracking()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => new { i.OrderId, i.Serie, i.Folio, i.Uuid, i.XmlFileKey, i.PdfFileKey })
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice?.XmlFileKey is null || !await OrderInvoicing.SeesOrderAsync(_context, invoice.OrderId, cancellationToken))
            return Result.Failure<PrivateFileDownload>(NotFoundMessage);

        return await InvoiceFiles.DownloadAsync(
            _storage, _gateway, _logger, request.InvoiceId, $"{invoice.Serie}-{invoice.Folio}_{invoice.Uuid}",
            invoice.XmlFileKey, invoice.PdfFileKey, request.Kind, cancellationToken);
    }
}

/// <summary>Downloading the stamped XML or the PDF of an invoice, of an order or global (Fases 28A and 28B).</summary>
internal static class InvoiceFiles
{
    public static async Task<Result<PrivateFileDownload>> DownloadAsync(
        IPrivateFileStorage storage,
        IInvoicingGateway gateway,
        ILogger logger,
        Guid invoiceId,
        string fileName,
        string xmlFileKey,
        string? pdfFileKey,
        OrderInvoiceFileKind kind,
        CancellationToken cancellationToken)
    {
        if (kind == OrderInvoiceFileKind.Xml)
        {
            var xml = await storage.OpenReadAsync(xmlFileKey, cancellationToken);
            return xml is null
                ? Result.Failure<PrivateFileDownload>(GetOrderInvoiceFileQueryHandler.NotFoundMessage)
                : Result.Success(new PrivateFileDownload(xml, "application/xml", fileName + ".xml"));
        }

        if (pdfFileKey is not null && await storage.OpenReadAsync(pdfFileKey, cancellationToken) is { } kept)
            return Result.Success(new PrivateFileDownload(kept, "application/pdf", fileName + ".pdf"));
        if (!gateway.IsConfigured)
            return Result.Failure<PrivateFileDownload>(GetOrderInvoiceFileQueryHandler.PdfUnavailableMessage);

        // The PAC did not give it when it stamped: it renders it now from the stamped XML.
        var stampedXml = await OrderInvoiceStamper.ReadTextAsync(storage, xmlFileKey, cancellationToken);
        if (stampedXml is null)
            return Result.Failure<PrivateFileDownload>(GetOrderInvoiceFileQueryHandler.NotFoundMessage);
        try
        {
            var pdf = await gateway.RenderPdfAsync(stampedXml, cancellationToken);
            return Result.Success(new PrivateFileDownload(new MemoryStream(pdf), "application/pdf", fileName + ".pdf"));
        }
        catch (Exception ex) when (ex is InvoicingGatewayUnavailableException or InvoicingGatewayRefusedException)
        {
            logger.LogWarning("The PAC did not render the PDF of invoice {InvoiceId}: {Error}", invoiceId, ex.GetType().Name);
            return Result.Failure<PrivateFileDownload>(GetOrderInvoiceFileQueryHandler.PdfUnavailableMessage);
        }
    }
}

/// <summary>What deciding whether an order can be invoiced, and whom to, shares.</summary>
internal static class OrderInvoicing
{
    public const string NotPaidMessage =
        "La venta no está pagada por completo: cóbrala antes de facturarla, o factúrala en parcialidades (PPD) cuando el cliente "
        + "se la lleve fiada o ya se le haya entregado.";
    public const string AlreadyInvoicingMessage = "Otra persona está facturando este pedido en este momento: actualiza la página.";

    /// <summary>The order, if the business sees it (as its detail does).</summary>
    public static async Task<Order> LoadOrderAsync(IApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders.AsNoTracking()
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", orderId);
        if (!await SeesOrderTypeAsync(context, order.TenantId, order.OrderType, cancellationToken))
            throw new EntityNotFoundException("Order", orderId);
        return order;
    }

    /// <summary>
    /// Whether the business sees the order of an invoice, as its detail does: an order of a module it no longer has is not
    /// there for its invoices either (to retry, cancel or download).
    /// </summary>
    public static async Task<bool> SeesOrderAsync(IApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.TenantId, o.OrderType })
            .FirstOrDefaultAsync(cancellationToken);
        return order is not null && await SeesOrderTypeAsync(context, order.TenantId, order.OrderType, cancellationToken);
    }

    private static async Task<bool> SeesOrderTypeAsync(
        IApplicationDbContext context, Guid tenantId, OrderType orderType, CancellationToken cancellationToken)
        => TenantCapabilityRules.CanCreateOrderType(
            await TenantCapabilityRules.ResolveCapabilitiesAsync(context, tenantId, cancellationToken), orderType);

    public static string? LiveInvoiceProblem(OrderInvoice? live) => live?.Status switch
    {
        null => null,
        OrderInvoiceStatus.Stamping => $"La factura {live.Number} de este pedido se está timbrando.",
        OrderInvoiceStatus.Cancelling => $"La factura {live.Number} de este pedido espera su cancelación en el SAT.",
        _ => $"Este pedido ya tiene la factura {live.Number}: cancélala para facturarlo otra vez."
    };

    /// <summary>
    /// A sale in a global invoice that stands with the SAT (Fase 28B) is not invoiced on its own: the SAT asks to cancel the
    /// global invoice (motive 04), invoice the sale to the customer and issue the period's global invoice again without it.
    /// </summary>
    public static async Task<string?> GlobalInvoiceProblemAsync(IApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        var global = await context.GlobalInvoiceOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId && !o.Released)
            .Join(context.GlobalInvoices.AsNoTracking(), o => o.GlobalInvoiceId, g => g.Id, (o, g) => new { g.Serie, g.Folio })
            .FirstOrDefaultAsync(cancellationToken);
        return global is null ? null : InGlobalInvoiceMessage($"{global.Serie}-{global.Folio}");
    }

    public static string InGlobalInvoiceMessage(string number) =>
        $"Esta venta está en la factura global {number}. Si el cliente pide su factura, cancela esa global con el motivo 04 en " +
        "Reportes › Factura global, factura esta venta y emite otra vez la global del periodo: ya no la incluirá.";

    /// <summary>A sale that is still pending or an open tab can change its total: it is invoiced in installments once closed.</summary>
    public const string StillChangingMessage =
        "La venta todavía puede cambiar (sigue pendiente o es una cuenta abierta): acéptala o ciérrala antes de facturarla en parcialidades.";

    /// <summary>
    /// Why the order itself cannot be invoiced here (Fase 28A). The invoice covers the whole sale: what was given back or
    /// forgiven gets its own credit note (Fase 28D). One not paid in full is invoiced in installments (Fase 28C) only when the
    /// customer owes it: sold on credit, or delivered or completed (its total no longer changes); a sale still to be charged
    /// is charged first.
    /// </summary>
    public static async Task<string?> OrderProblemAsync(IApplicationDbContext context, Order order, CancellationToken cancellationToken)
    {
        if (order.Status == OrderStatus.Cancelled)
            return "Una orden cancelada no se factura.";
        if (order.Total <= 0)
            return "Una venta sin importe no se factura.";
        var owed = order.Total - order.PaidAmount > 0 || order.Payments.Count == 0;
        if (owed && !order.IsOwedByCustomer)
            return NotPaidMessage;
        // In installments the invoice stays as it is while the sale is paid: a sale that can still take items, delivery or
        // rounds would no longer match it (Fase 28C).
        if (owed && Order.TotalCanChange(order.Status, order.IsTab))
            return StillChangingMessage;
        var refundInCourse = await context.OnlinePaymentRefunds.AsNoTracking()
            .AnyAsync(r => r.OrderId == order.Id && r.Status == OnlinePaymentRefundStatus.Requested, cancellationToken);
        return refundInCourse
            ? "La venta tiene un reembolso en curso en Mercado Pago: espera a que termine."
            : null;
    }

    /// <summary>The customer with the order's phone, if there is one.</summary>
    public static async Task<Domain.Customers.Entities.Customer?> CustomerAsync(
        IApplicationDbContext context, IPhoneNormalizationService phones, string? orderPhone, bool tracked, CancellationToken cancellationToken)
        => CustomerPhones.TryNormalize(phones, orderPhone, out var phone)
            ? await CreditAccount.RecordAsync(context, phone, cancellationToken, tracked)
            : null;

    public static CfdiReceiver Receiver(CfdiReceiverInput input)
        => CfdiReceiver.Create(input.Rfc, input.Name, input.PostalCode, input.FiscalRegime, input.CfdiUse);

    /// <summary>
    /// The invoice's fiscal data kept in the customer with the order's phone, if there is one, with its audit entry. An
    /// invoice without an email (not sent this time) leaves the customer's as it is.
    /// </summary>
    public static async Task SaveToCustomerAsync(
        IApplicationDbContext context,
        IPhoneNormalizationService phones,
        ICurrentUserService currentUser,
        CfdiReceiverInput receiver,
        Order order,
        OrderInvoice invoice,
        CancellationToken cancellationToken)
    {
        if (await CustomerAsync(context, phones, order.CustomerPhone, tracked: true, cancellationToken) is not { } customer)
            return;

        var (previous, previousEmail) = (customer.FiscalData, customer.InvoiceEmail);
        // A value of its own: an owned value is never shared between two records.
        customer.SetFiscalData(Receiver(receiver), invoice.ReceiverEmail ?? customer.InvoiceEmail);
        if (CustomerFiscalDataAudit.Entry(customer, previous, previousEmail, currentUser.UserName, $"Factura {invoice.Number}") is { } changed)
            context.AuditLogs.Add(changed);
    }

    /// <summary>No email, or one plain address (Fase 28E).</summary>
    public static bool IsValidEmail(string? email) => string.IsNullOrWhiteSpace(email) || InvoiceEmailAddress.IsValid(email.Trim());

    /// <summary>What keeping the fiscal data in the customer also asks for, besides ManageOrders.</summary>
    public static IEnumerable<TenantPermission> SavingToCustomer(bool saveToCustomer)
        => saveToCustomer ? [TenantPermission.ManageCustomers] : [];
}
