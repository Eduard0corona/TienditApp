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
        CancelledAt = invoice.CancelledAt
    };
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
    /// <summary>The customer with the order's phone, whose fiscal data the invoice can keep.</summary>
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    /// <summary>The order's invoices, the newest first (cancelled and refused ones included).</summary>
    public List<OrderInvoiceDto> Invoices { get; set; } = [];
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
            ?? OrderInvoicing.LiveInvoiceProblem(invoices.FirstOrDefault(i => i.IsLive))
            ?? await OrderInvoicing.OrderProblemAsync(_context, order, cancellationToken);

        var last = invoices.FirstOrDefault();
        return new OrderInvoicingDto
        {
            Available = settings.Available,
            Sandbox = settings.Sandbox,
            CanInvoice = problem is null,
            Problem = problem,
            ProposedReceiver = customer?.FiscalData is { } fiscalData
                ? CfdiReceiverDto.From(fiscalData)
                : last is null ? null : CfdiReceiverDto.From(last.Receiver),
            CustomerId = customer?.Id,
            CustomerName = customer?.Name,
            Invoices = invoices.Select(OrderInvoiceDto.From).ToList()
        };
    }
}

/// <summary>
/// Stamps the invoice of a paid order to a customer with an RFC (whoever manages the orders): its folio and CFDI are saved
/// first (<see cref="RequestOrderInvoiceCommand"/>), and the PAC is asked after the commit, never inside the transaction.
/// When the PAC does not answer, the invoice stays «Timbrando» and is asked again with the same key.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record IssueOrderInvoiceCommand(Guid OrderId, CfdiReceiverInput Receiver, bool SaveToCustomer)
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
            new RequestOrderInvoiceCommand(request.OrderId, request.Receiver, request.SaveToCustomer), cancellationToken);
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
public record RequestOrderInvoiceCommand(Guid OrderId, CfdiReceiverInput Receiver, bool SaveToCustomer)
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
    }
}

public class RequestOrderInvoiceCommandHandler : IRequestHandler<RequestOrderInvoiceCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IFolioGenerator _folios;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;

    public RequestOrderInvoiceCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IFolioGenerator folios,
        IPhoneNormalizationService phones,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _folios = folios;
        _phones = phones;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(RequestOrderInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<Guid>(InvoicingSettings.NotAvailableMessage);

        var built = await GetOrderCfdiDraftQueryHandler.BuildAsync(_context, request.OrderId, request.Receiver, cancellationToken);
        if (built.IsFailure)
            return Result.Failure<Guid>(built.Error!);
        var (draft, order, _) = built.Value;
        if (!draft.IsReadyToStamp)
            return Result.Failure<Guid>(draft.Blockers[0]);

        var live = await _context.OrderInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == order.Id
                && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                    || i.Status == OrderInvoiceStatus.Cancelling), cancellationToken);
        var orderProblem = OrderInvoicing.LiveInvoiceProblem(live)
            ?? await OrderInvoicing.OrderProblemAsync(_context, order, cancellationToken);
        if (orderProblem is not null)
            return Result.Failure<Guid>(orderProblem);
        // The draft of a paid sale: what the order paid with, in one exhibition.
        if (draft.PaymentMethod != OrderInvoice.PaidInOneExhibition)
            return Result.Failure<Guid>(OrderInvoicing.NotPaidMessage);

        var now = _clock.UtcNow;
        var tenantId = _tenant.TenantId;
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var certificateProblem = InvoicingSettings.CertificateProblem(profile, draft.Issuer.Rfc, now);
        if (certificateProblem is not null)
            return Result.Failure<Guid>(certificateProblem);

        CfdiReceiver receiver;
        try
        {
            receiver = OrderInvoicing.Receiver(request.Receiver);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(ex.Message);
        }

        var timeZone = await _context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var serie = profile!.Serie;
        var folio = await _folios.NextInvoiceFolioAsync(tenantId, serie, cancellationToken);
        var document = CfdiDocument.From(draft, receiver, serie, folio, TenantDateTimeResolver.LocalNow(now, timeZone));
        var invoice = OrderInvoice.Request(
            tenantId, order.Id, serie, folio, receiver, draft.PaymentForm, document.ToJson(),
            draft.SubTotal, draft.TotalTransferred, draft.Total, userId, now);
        _context.OrderInvoices.Add(invoice);

        if (request.SaveToCustomer
            && await OrderInvoicing.CustomerAsync(_context, _phones, order.CustomerPhone, tracked: true, cancellationToken) is { } customer)
        {
            var previous = customer.FiscalData;
            // A value of its own: an owned value is never shared between two records.
            customer.SetFiscalData(OrderInvoicing.Receiver(request.Receiver));
            if (CustomerFiscalDataAudit.Entry(customer, previous, _currentUser.UserName, $"Factura {invoice.Number}") is { } changed)
                _context.AuditLogs.Add(changed);
        }

        _context.AuditLogs.Add(new AuditLog(
            tenantId: tenantId,
            entityName: nameof(OrderInvoice),
            entityId: invoice.Id.ToString(),
            action: "OrderInvoiceRequested",
            performedBy: _currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new { invoice.OrderId, order.OrderNumber, Number = invoice.Number, receiver.Rfc, invoice.Total })));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone invoiced the same order at the same time (the index of one live invoice per order kept theirs): this
            // one is undone whole, folio included. Anything else is thrown on, for the execution strategy to retry.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await _context.OrderInvoices.AsNoTracking().AnyAsync(
                    i => i.OrderId == request.OrderId
                        && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                            || i.Status == OrderInvoiceStatus.Cancelling),
                    cancellationToken))
                return Result.Failure<Guid>(OrderInvoicing.AlreadyInvoicingMessage);
            throw;
        }

        return Result.Success(invoice.Id);
    }
}

/// <summary>
/// Asks the PAC again for an invoice that is still «Timbrando» (its answer did not arrive), with the same CFDI and key: it
/// never stamps a second one.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RetryOrderInvoiceCommand(Guid InvoiceId) : IRequest<Result<OrderInvoiceDto>>;

public class RetryOrderInvoiceCommandHandler : IRequestHandler<RetryOrderInvoiceCommand, Result<OrderInvoiceDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

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
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is not { HasCertificate: true })
            return Result.Failure<OrderInvoiceDto>(NoCertificateToCancelMessage);

        // The issuer the invoice was stamped for: the PAC cancels it with the CSD it keeps for that RFC.
        var issuerRfc = CfdiDocument.FromJson(invoice.Document).Issuer.Rfc;
        CfdiCancellationOutcome outcome;
        try
        {
            outcome = await _gateway.CancelAsync(issuerRfc, invoice.Uuid!, request.Motive, cancellationToken);
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

        var fileName = $"{invoice.Serie}-{invoice.Folio}_{invoice.Uuid}";
        if (request.Kind == OrderInvoiceFileKind.Xml)
        {
            var xml = await _storage.OpenReadAsync(invoice.XmlFileKey, cancellationToken);
            return xml is null
                ? Result.Failure<PrivateFileDownload>(NotFoundMessage)
                : Result.Success(new PrivateFileDownload(xml, "application/xml", fileName + ".xml"));
        }

        if (invoice.PdfFileKey is not null && await _storage.OpenReadAsync(invoice.PdfFileKey, cancellationToken) is { } kept)
            return Result.Success(new PrivateFileDownload(kept, "application/pdf", fileName + ".pdf"));
        if (!_gateway.IsConfigured)
            return Result.Failure<PrivateFileDownload>(PdfUnavailableMessage);

        // The PAC did not give it when it stamped: it renders it now from the stamped XML.
        var stampedXml = await OrderInvoiceStamper.ReadTextAsync(_storage, invoice.XmlFileKey, cancellationToken);
        if (stampedXml is null)
            return Result.Failure<PrivateFileDownload>(NotFoundMessage);
        try
        {
            var pdf = await _gateway.RenderPdfAsync(stampedXml, cancellationToken);
            return Result.Success(new PrivateFileDownload(new MemoryStream(pdf), "application/pdf", fileName + ".pdf"));
        }
        catch (Exception ex) when (ex is InvoicingGatewayUnavailableException or InvoicingGatewayRefusedException)
        {
            _logger.LogWarning("The PAC did not render the PDF of invoice {InvoiceId}: {Error}", request.InvoiceId, ex.GetType().Name);
            return Result.Failure<PrivateFileDownload>(PdfUnavailableMessage);
        }
    }
}

/// <summary>What deciding whether an order can be invoiced, and whom to, shares.</summary>
internal static class OrderInvoicing
{
    public const string NotPaidMessage =
        "Solo se factura una venta pagada por completo: la factura en parcialidades (PPD) todavía no está disponible.";
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
    /// Why the order itself cannot be invoiced here (Fase 28A): only a sale paid in full, with nothing given back or
    /// forgiven, which would need a credit note.
    /// </summary>
    public static async Task<string?> OrderProblemAsync(IApplicationDbContext context, Order order, CancellationToken cancellationToken)
    {
        if (order.Status == OrderStatus.Cancelled)
            return "Una orden cancelada no se factura.";
        if (order.Total <= 0)
            return "Una venta sin importe no se factura.";
        if (order.WrittenOffAmount > 0)
            return "Se condonó parte de esta venta: lo condonado requiere una nota de crédito, que TienditApp todavía no genera.";
        if (order.RefundedAmount > 0)
            return "La venta tiene reembolsos: lo devuelto requiere una nota de crédito, que TienditApp todavía no genera.";
        if (order.Total - order.PaidAmount > 0 || order.Payments.Count == 0)
            return NotPaidMessage;
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

    /// <summary>What keeping the fiscal data in the customer also asks for, besides ManageOrders.</summary>
    public static IEnumerable<TenantPermission> SavingToCustomer(bool saveToCustomer)
        => saveToCustomer ? [TenantPermission.ManageCustomers] : [];
}
