using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>The page where a business's customers invoice their tickets (Fase 28F, ADR 0050).</summary>
public class SelfInvoicePageDto
{
    public string BusinessName { get; set; } = null!;
    /// <summary>Whether the business invoices tickets online now: self-invoicing on and its invoicing ready.</summary>
    public bool Available { get; set; }
    /// <summary>The PAC's test environment: the invoices have no fiscal effect.</summary>
    public bool Sandbox { get; set; }
}

public enum SelfInvoiceTicketStatus
{
    /// <summary>The customer can invoice it now.</summary>
    CanInvoice = 0,
    /// <summary>It already has its invoice; the customer can have it sent again with its RFC.</summary>
    Invoiced = 1,
    /// <summary>Its invoice is being stamped: it arrives by email.</summary>
    Stamping = 2,
    /// <summary>It is not invoiced online: the customer asks the business (<see cref="SelfInvoiceTicketDto.Message"/>).</summary>
    NotOnline = 3
}

/// <summary>A ticket as the customer sees it on the page: what it is, and whether it is invoiced online.</summary>
public class SelfInvoiceTicketDto
{
    /// <summary>The folio printed on the ticket, «ORD-000123».</summary>
    public string OrderNumber { get; set; } = null!;
    /// <summary>When it was paid in full (or placed, while it is not).</summary>
    public DateTimeOffset Date { get; set; }
    public decimal Total { get; set; }
    public SelfInvoiceTicketStatus Status { get; set; }
    /// <summary>Its invoice («F-12»), once it has one.</summary>
    public string? InvoiceNumber { get; set; }
    /// <summary>Why it is not invoiced online, for the customer.</summary>
    public string? Message { get; set; }
    /// <summary>The last day it can be invoiced online, in the business's calendar.</summary>
    public DateOnly? InvoiceBy { get; set; }
}

public enum SelfInvoiceOutcome
{
    /// <summary>Stamped: the XML and the PDF go to the email.</summary>
    Stamped = 0,
    /// <summary>The PAC did not answer yet: TienditApp keeps asking, and the email follows once it is stamped.</summary>
    Stamping = 1,
    /// <summary>The PAC refused it (<see cref="SelfInvoiceResultDto.Message"/>): the customer corrects the data and tries again.</summary>
    Refused = 2
}

public class SelfInvoiceResultDto
{
    public SelfInvoiceOutcome Outcome { get; set; }
    /// <summary>The invoice, «F-12».</summary>
    public string InvoiceNumber { get; set; } = null!;
    /// <summary>Where its XML and PDF go.</summary>
    public string Email { get; set; } = null!;
    /// <summary>Why the PAC refused it.</summary>
    public string? Message { get; set; }
}

public class SelfInvoiceResendDto
{
    public string InvoiceNumber { get; set; } = null!;
    public string Email { get; set; } = null!;
}

/// <summary>
/// The business's page for its customers to invoice their tickets, by its slug; null when it does not exist or is not
/// active. It says only whether it invoices online, nothing of its sales.
/// </summary>
[AllowAnonymousRequest("Autofactura: el nombre del negocio y si factura en línea, sin datos de sus ventas.")]
public record GetSelfInvoicePageQuery(string Slug) : IRequest<SelfInvoicePageDto?>;

public class GetSelfInvoicePageQueryValidator : AbstractValidator<GetSelfInvoicePageQuery>
{
    public GetSelfInvoicePageQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(SelfInvoicing.MaxSlugLength);
    }
}

public class GetSelfInvoicePageQueryHandler : IRequestHandler<GetSelfInvoicePageQuery, SelfInvoicePageDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public GetSelfInvoicePageQueryHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<SelfInvoicePageDto?> Handle(GetSelfInvoicePageQuery request, CancellationToken cancellationToken)
    {
        var business = await SelfInvoicing.FindBusinessAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return null;
        return new SelfInvoicePageDto
        {
            BusinessName = business.Name,
            Available = await SelfInvoicing.ProblemAsync(_context, business, _gateway, _clock.UtcNow, cancellationToken) is null,
            Sandbox = _gateway.Sandbox
        };
    }
}

/// <summary>
/// A ticket of the business by the folio and the code printed on it, and whether the customer can invoice it online. A
/// folio that is not there and a code that is not its own answer the same (null), so the page tells nothing without the
/// ticket; a business that does not invoice online now answers why.
/// </summary>
[AllowAnonymousRequest("Autofactura: un ticket con el folio y el código impresos en él, imposibles de adivinar juntos.")]
public record LookUpSelfInvoiceTicketQuery(string Slug, string Folio, string Code) : IRequest<Result<SelfInvoiceTicketDto?>>;

public class LookUpSelfInvoiceTicketQueryValidator : AbstractValidator<LookUpSelfInvoiceTicketQuery>
{
    public LookUpSelfInvoiceTicketQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(SelfInvoicing.MaxSlugLength);
        RuleFor(x => x.Folio).NotEmpty().WithMessage("Escribe el folio del ticket.").MaximumLength(SelfInvoicing.MaxTypedLength);
        RuleFor(x => x.Code).NotEmpty().WithMessage("Escribe el código del ticket.").MaximumLength(SelfInvoicing.MaxTypedLength);
    }
}

public class LookUpSelfInvoiceTicketQueryHandler : IRequestHandler<LookUpSelfInvoiceTicketQuery, Result<SelfInvoiceTicketDto?>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public LookUpSelfInvoiceTicketQueryHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<SelfInvoiceTicketDto?>> Handle(LookUpSelfInvoiceTicketQuery request, CancellationToken cancellationToken)
    {
        var business = await SelfInvoicing.FindBusinessAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return Result.Success<SelfInvoiceTicketDto?>(null);
        var now = _clock.UtcNow;
        if (await SelfInvoicing.ProblemAsync(_context, business, _gateway, now, cancellationToken) is { } problem)
            return Result.Failure<SelfInvoiceTicketDto?>(problem);
        var order = await SelfInvoicing.FindOrderAsync(_context, business, request.Folio, request.Code, cancellationToken);
        if (order is null)
            return Result.Success<SelfInvoiceTicketDto?>(null);

        var ticket = new SelfInvoiceTicketDto
        {
            OrderNumber = order.OrderNumber,
            Date = SelfInvoicing.PaidAt(order) ?? order.CreatedAt,
            Total = order.Total
        };
        var live = await SelfInvoicing.LiveInvoiceAsync(_context, order.Id, cancellationToken);
        if (live is not null)
        {
            ticket.InvoiceNumber = live.Number;
            (ticket.Status, ticket.Message) = live.Status switch
            {
                OrderInvoiceStatus.Stamped => (SelfInvoiceTicketStatus.Invoiced, (string?)null),
                OrderInvoiceStatus.Stamping => (SelfInvoiceTicketStatus.Stamping, null),
                _ => (SelfInvoiceTicketStatus.NotOnline, SelfInvoicing.CancellingMessage(live.Number))
            };
            return Result.Success<SelfInvoiceTicketDto?>(ticket);
        }

        var built = await GetOrderCfdiDraftQueryHandler.BuildAsync(_context, order.Id, null, cancellationToken);
        var notOnline = built.IsFailure
            ? SelfInvoicing.AskTheBusinessMessage
            : await SelfInvoicing.OrderProblemAsync(_context, order, cancellationToken)
              ?? SelfInvoicing.SaleProblem(built.Value.Draft, order, business.TimeZone, now);
        if (notOnline is not null)
        {
            ticket.Status = SelfInvoiceTicketStatus.NotOnline;
            ticket.Message = notOnline;
            return Result.Success<SelfInvoiceTicketDto?>(ticket);
        }

        ticket.Status = SelfInvoiceTicketStatus.CanInvoice;
        ticket.InvoiceBy = SelfInvoicing.InvoiceBy(SelfInvoicing.PaidAt(order)!.Value, business.TimeZone);
        return Result.Success<SelfInvoiceTicketDto?>(ticket);
    }
}

/// <summary>
/// The customer invoices the ticket (Fase 28F): with the folio and the code printed on it, its fiscal data and the email
/// its XML and PDF go to. The same invoice Pedidos makes, requested by the customer: its folio and CFDI are saved first
/// (<see cref="RequestSelfInvoiceCommand"/>), and the PAC is asked after the commit; the email goes from the queue once it is
/// stamped (Fase 28E). Null when the ticket is not there (or the code is not its own).
/// </summary>
[AllowAnonymousRequest("Autofactura: el cliente factura su ticket con el folio y el código impresos en él.")]
public record SelfInvoiceCommand(string Slug, string Folio, string Code, CfdiReceiverInput Receiver, string Email)
    : IRequest<Result<SelfInvoiceResultDto?>>;

public class SelfInvoiceCommandValidator : AbstractValidator<SelfInvoiceCommand>
{
    public SelfInvoiceCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(SelfInvoicing.MaxSlugLength);
        RuleFor(x => x.Folio).NotEmpty().WithMessage("Escribe el folio del ticket.").MaximumLength(SelfInvoicing.MaxTypedLength);
        RuleFor(x => x.Code).NotEmpty().WithMessage("Escribe el código del ticket.").MaximumLength(SelfInvoicing.MaxTypedLength);
        RuleFor(x => x.Receiver).NotNull().WithMessage("Escribe tus datos fiscales.").SetValidator(new CfdiReceiverInputValidator());
        RuleFor(x => x.Email).NotEmpty().WithMessage(SelfInvoicing.EmailRequiredMessage)
            .Must(email => InvoiceEmailAddress.IsValid(email.Trim())).WithMessage(InvoiceEmailAddress.InvalidMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Email), ApplyConditionTo.CurrentValidator);
    }
}

public class SelfInvoiceCommandHandler : IRequestHandler<SelfInvoiceCommand, Result<SelfInvoiceResultDto?>>
{
    private readonly ISender _sender;
    private readonly IInvoicingGateway _gateway;
    private readonly OrderInvoiceStamper _stamper;

    public SelfInvoiceCommandHandler(
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

    public async Task<Result<SelfInvoiceResultDto?>> Handle(SelfInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<SelfInvoiceResultDto?>(SelfInvoicing.NotNowMessage);

        var requested = await _sender.Send(
            new RequestSelfInvoiceCommand(request.Slug, request.Folio, request.Code, request.Receiver, request.Email), cancellationToken);
        if (requested.IsFailure)
            return Result.Failure<SelfInvoiceResultDto?>(requested.Error!);
        if (requested.Value is not { } invoiceId)
            return Result.Success<SelfInvoiceResultDto?>(null);

        var invoice = await _stamper.StampAsync(invoiceId, cancellationToken);
        return Result.Success<SelfInvoiceResultDto?>(new SelfInvoiceResultDto
        {
            Outcome = invoice.Status switch
            {
                OrderInvoiceStatus.Stamped => SelfInvoiceOutcome.Stamped,
                OrderInvoiceStatus.Refused => SelfInvoiceOutcome.Refused,
                _ => SelfInvoiceOutcome.Stamping
            },
            InvoiceNumber = invoice.Number,
            Email = invoice.ReceiverEmail!,
            Message = invoice.Status == OrderInvoiceStatus.Refused ? invoice.FailureMessage : null
        });
    }
}

/// <summary>
/// The invoice the customer asks for, fixed before the PAC is asked (sent by <see cref="SelfInvoiceCommand"/>), in one
/// transaction: the same checks as Pedidos under the business's invoicing lock, and the ones of self-invoicing (paid in full,
/// within its month, not of a delivery app). It checks the ticket's code again. Answers the new invoice's id, or null when
/// the ticket is not there.
/// </summary>
[AllowAnonymousRequest("Autofactura: el cliente factura su ticket con el folio y el código impresos en él.")]
public record RequestSelfInvoiceCommand(string Slug, string Folio, string Code, CfdiReceiverInput Receiver, string Email)
    : IRequest<Result<Guid?>>, ITransactionalRequest;

public class RequestSelfInvoiceCommandValidator : AbstractValidator<RequestSelfInvoiceCommand>
{
    public RequestSelfInvoiceCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(SelfInvoicing.MaxSlugLength);
        RuleFor(x => x.Folio).NotEmpty().MaximumLength(SelfInvoicing.MaxTypedLength);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(SelfInvoicing.MaxTypedLength);
        RuleFor(x => x.Receiver).NotNull().SetValidator(new CfdiReceiverInputValidator());
        RuleFor(x => x.Email).NotEmpty().Must(email => InvoiceEmailAddress.IsValid(email.Trim()))
            .WithMessage(InvoiceEmailAddress.InvalidMessage);
    }
}

public class RequestSelfInvoiceCommandHandler : IRequestHandler<RequestSelfInvoiceCommand, Result<Guid?>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;
    private readonly OrderInvoiceRequester _requester;

    public RequestSelfInvoiceCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IInvoicingGateway gateway,
        IInvoicingLock invoicingLock,
        IFolioGenerator folios,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _gateway = gateway;
        _clock = clock;
        _requester = new OrderInvoiceRequester(context, invoicingLock, folios, clock);
    }

    public async Task<Result<Guid?>> Handle(RequestSelfInvoiceCommand request, CancellationToken cancellationToken)
    {
        var business = await SelfInvoicing.FindBusinessAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return Result.Success<Guid?>(null);
        var now = _clock.UtcNow;
        if (await SelfInvoicing.ProblemAsync(_context, business, _gateway, now, cancellationToken) is { } problem)
            return Result.Failure<Guid?>(problem);
        var order = await SelfInvoicing.FindOrderAsync(_context, business, request.Folio, request.Code, cancellationToken);
        if (order is null)
            return Result.Success<Guid?>(null);

        // What the customer can do something about, in the customer's words; the checks of Pedidos follow under the lock.
        var live = await SelfInvoicing.LiveInvoiceAsync(_context, order.Id, cancellationToken);
        var orderProblem = live is null
            ? await SelfInvoicing.OrderProblemAsync(_context, order, cancellationToken)
            : live.Status == OrderInvoiceStatus.Cancelling ? SelfInvoicing.CancellingMessage(live.Number) : SelfInvoicing.AlreadyInvoicedMessage(live.Number);
        if (orderProblem is not null)
            return Result.Failure<Guid?>(orderProblem);
        try
        {
            OrderInvoicing.Receiver(request.Receiver);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid?>(ex.Message);
        }

        string? saleProblem = null;
        var requested = await _requester.RequestAsync(
            business.Id,
            new OrderInvoiceRequest(order.Id, request.Receiver, request.Email, OrderInvoice.SelfInvoiceRequester, SelfInvoicing.PerformedBy)
            {
                Check = (draft, current) => saleProblem = SelfInvoicing.SaleProblem(draft, current, business.TimeZone, now)
            },
            cancellationToken);
        if (requested.IsSuccess)
            return Result.Success<Guid?>(requested.Value.Id);
        // The rest of what Pedidos checks speaks to the business: the customer is told to ask it.
        return Result.Failure<Guid?>(
            saleProblem
            ?? (requested.Error == OrderInvoicing.AlreadyInvoicingMessage ? SelfInvoicing.BeingInvoicedMessage : SelfInvoicing.AskTheBusinessMessage));
    }
}

/// <summary>
/// The customer has the invoice of the ticket sent again (Fase 28F): with the folio and the code, the RFC the invoice was
/// made for (without it nothing more is said) and an email. It goes from the queue like the others, within the same limits
/// as «Enviar por correo» (Fase 28E), counted under the business's invoicing lock. Null when the ticket is not there.
/// </summary>
[AllowAnonymousRequest("Autofactura: reenviar la factura de un ticket con su folio, su código y el RFC de la factura.")]
public record ResendSelfInvoiceCommand(string Slug, string Folio, string Code, string Rfc, string Email)
    : IRequest<Result<SelfInvoiceResendDto?>>, ITransactionalRequest;

public class ResendSelfInvoiceCommandValidator : AbstractValidator<ResendSelfInvoiceCommand>
{
    public ResendSelfInvoiceCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(SelfInvoicing.MaxSlugLength);
        RuleFor(x => x.Folio).NotEmpty().WithMessage("Escribe el folio del ticket.").MaximumLength(SelfInvoicing.MaxTypedLength);
        RuleFor(x => x.Code).NotEmpty().WithMessage("Escribe el código del ticket.").MaximumLength(SelfInvoicing.MaxTypedLength);
        RuleFor(x => x.Rfc).NotEmpty().WithMessage("Escribe el RFC de la factura.").MaximumLength(SelfInvoicing.MaxTypedLength);
        RuleFor(x => x.Email).NotEmpty().WithMessage(SelfInvoicing.EmailRequiredMessage)
            .Must(email => InvoiceEmailAddress.IsValid(email.Trim())).WithMessage(InvoiceEmailAddress.InvalidMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Email), ApplyConditionTo.CurrentValidator);
    }
}

public class ResendSelfInvoiceCommandHandler : IRequestHandler<ResendSelfInvoiceCommand, Result<SelfInvoiceResendDto?>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IInvoicingGateway _gateway;
    private readonly IInvoicingLock _lock;
    private readonly IDateTimeProvider _clock;

    public ResendSelfInvoiceCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IInvoicingGateway gateway,
        IInvoicingLock invoicingLock,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _gateway = gateway;
        _lock = invoicingLock;
        _clock = clock;
    }

    public async Task<Result<SelfInvoiceResendDto?>> Handle(ResendSelfInvoiceCommand request, CancellationToken cancellationToken)
    {
        var business = await SelfInvoicing.FindBusinessAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return Result.Success<SelfInvoiceResendDto?>(null);
        var now = _clock.UtcNow;
        if (await SelfInvoicing.ProblemAsync(_context, business, _gateway, now, cancellationToken) is { } problem)
            return Result.Failure<SelfInvoiceResendDto?>(problem);
        var order = await SelfInvoicing.FindOrderAsync(_context, business, request.Folio, request.Code, cancellationToken);
        if (order is null)
            return Result.Success<SelfInvoiceResendDto?>(null);

        var live = await SelfInvoicing.LiveInvoiceAsync(_context, order.Id, cancellationToken);
        if (live is not { Status: OrderInvoiceStatus.Stamped })
            return Result.Failure<SelfInvoiceResendDto?>(SelfInvoicing.NoInvoiceToResendMessage);
        if (!string.Equals(live.Receiver.Rfc, request.Rfc.Trim().ToUpperInvariant(), StringComparison.Ordinal))
            return Result.Failure<SelfInvoiceResendDto?>(SelfInvoicing.OtherRfcMessage);

        await _lock.AcquireAsync(business.Id, cancellationToken);
        var since = now - CfdiEmails.Day;
        if (await _context.CfdiEmails.CountAsync(e => e.CfdiId == live.Id && e.RequestedAt > since, cancellationToken)
            >= CfdiEmails.MaxPerCfdiPerDay)
            return Result.Failure<SelfInvoiceResendDto?>(SelfInvoicing.TooManyForInvoiceMessage);
        if (await _context.CfdiEmails.CountAsync(e => e.RequestedAt > since, cancellationToken) >= CfdiEmails.MaxPerBusinessPerDay)
            return Result.Failure<SelfInvoiceResendDto?>(SelfInvoicing.TooManyForBusinessMessage);

        CfdiEmail email;
        try
        {
            email = CfdiEmail.Request(live, request.Email, OrderInvoice.SelfInvoiceRequester, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<SelfInvoiceResendDto?>(ex.Message);
        }
        _context.CfdiEmails.Add(email);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success<SelfInvoiceResendDto?>(new SelfInvoiceResendDto { InvoiceNumber = live.Number, Email = email.To });
    }
}

/// <summary>What the ticket of an order prints for self-invoicing: its folio and code, while the business has it on.</summary>
public class OrderSelfInvoiceDto
{
    /// <summary>The address of the business's page, «/m/{slug}/facturar».</summary>
    public string Path { get; set; } = null!;
    public string OrderNumber { get; set; } = null!;
    /// <summary>As the ticket shows it, «ABCDE-12345».</summary>
    public string Code { get; set; } = null!;
}

/// <summary>
/// The folio and code the ticket of an order prints (whoever manages the orders); null while self-invoicing is off, or for a
/// sale that is not invoiced online (cancelled, without amount, or of a delivery app).
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record GetOrderSelfInvoiceQuery(Guid OrderId) : IRequest<OrderSelfInvoiceDto?>;

public class GetOrderSelfInvoiceQueryHandler : IRequestHandler<GetOrderSelfInvoiceQuery, OrderSelfInvoiceDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetOrderSelfInvoiceQueryHandler(IApplicationDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<OrderSelfInvoiceDto?> Handle(GetOrderSelfInvoiceQuery request, CancellationToken cancellationToken)
    {
        var order = await OrderInvoicing.LoadOrderAsync(_context, request.OrderId, cancellationToken);
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var code = profile?.SelfInvoiceCodeFor(order.Id);
        if (code is null || order.Status == OrderStatus.Cancelled || order.Total <= 0 || order.IsDeliveryPlatformOrder)
            return null;
        var slug = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => t.Slug)
            .FirstAsync(cancellationToken);
        return new OrderSelfInvoiceDto
        {
            Path = SelfInvoicing.PagePath(slug),
            OrderNumber = order.OrderNumber,
            Code = SelfInvoiceCode.Format(code)
        };
    }
}

/// <summary>What the page answers when a folio is not there or a code is not its own: the same for both.</summary>
public static class SelfInvoiceMessages
{
    public const string NotFound = "No encontramos ese ticket: revisa el folio y el código impresos en él.";
}

/// <summary>A business with self-invoicing, found by its slug, already in context.</summary>
internal sealed record SelfInvoiceBusiness(Guid Id, string Name, string? TimeZone, InvoicingProfile? Profile);

internal static class SelfInvoicing
{
    public const int MaxSlugLength = 100;
    /// <summary>What a customer types as a folio, a code or an RFC: far more than any of them.</summary>
    public const int MaxTypedLength = 40;
    /// <summary>Who requested the invoices customers ask for, in the audit.</summary>
    public const string PerformedBy = "Autofactura";

    public const string NotOfferedMessage = "Este negocio no factura en línea por ahora: pídele tu factura directamente.";
    public const string NotNowMessage =
        "El negocio no puede facturar en este momento: intenta más tarde o pídele tu factura directamente.";
    public const string AskTheBusinessMessage = "Este ticket no se puede facturar en línea: pídele tu factura al negocio.";
    public const string BeingInvoicedMessage =
        "Este ticket se está facturando en este momento: revisa tu correo en unos minutos.";
    public const string EmailRequiredMessage = "Escribe el correo al que te llegará la factura.";
    public const string NoInvoiceToResendMessage = "Este ticket todavía no tiene una factura timbrada que reenviar.";
    public const string OtherRfcMessage = "Ese RFC no es el de la factura de este ticket.";
    public const string TooManyForInvoiceMessage =
        "Esta factura ya se envió varias veces hoy: revisa tu correo (también la carpeta de spam) o inténtalo mañana.";
    public const string TooManyForBusinessMessage =
        "El negocio ya envió muchas facturas por correo hoy: inténtalo mañana o pídesela directamente.";

    public static string AlreadyInvoicedMessage(string number) =>
        $"Este ticket ya tiene la factura {number}: si no te llegó, pide que te la reenvíen con el RFC de la factura.";

    public static string CancellingMessage(string number) =>
        $"La factura {number} de este ticket se está cancelando: pídele tu factura al negocio.";

    public static string PagePath(string slug) => $"/m/{slug}/facturar";

    /// <summary>
    /// The active business by its slug, put in context, with its time zone and invoicing profile; null when it does not exist
    /// or is not active.
    /// </summary>
    public static async Task<SelfInvoiceBusiness?> FindBusinessAsync(
        IApplicationDbContext context, ICurrentTenantService tenantService, string slug, CancellationToken cancellationToken)
    {
        var tenant = await PublicMenuTenant.FindAsync(context, slug, cancellationToken);
        if (tenant is null)
            return null;
        // A public request has no business: the ticket and the invoices are read through the filter of the page's business.
        tenantService.SetTenant(tenant.Id);
        var profile = await context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return new SelfInvoiceBusiness(tenant.Id, tenant.Name, tenant.Settings!.TimeZone, profile);
    }

    /// <summary>Why the business does not invoice online now, for its customers; null when it does.</summary>
    public static async Task<string?> ProblemAsync(
        IApplicationDbContext context, SelfInvoiceBusiness business, IInvoicingGateway gateway, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (business.Profile is not { SelfInvoicing: true })
            return NotOfferedMessage;
        var settings = await InvoicingSettings.ReadAsync(context, business.Id, gateway, now, cancellationToken);
        return settings.Ready ? null : NotNowMessage;
    }

    /// <summary>
    /// The order of the folio, when the code is its own (compared in constant time); null otherwise, the same for both. An
    /// order of a module the business no longer has is not there either.
    /// </summary>
    public static async Task<Order?> FindOrderAsync(
        IApplicationDbContext context, SelfInvoiceBusiness business, string folio, string code, CancellationToken cancellationToken)
    {
        var orderNumber = NormalizeFolio(folio);
        var order = orderNumber is null
            ? null
            : await context.Orders.AsNoTracking()
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);
        if (order is null || !business.Profile!.TakesSelfInvoiceCode(order.Id, code))
        {
            // As much work for a folio that is not there as for a code that is not its own.
            if (order is null)
                business.Profile!.TakesSelfInvoiceCode(Guid.Empty, code);
            return null;
        }
        return await OrderInvoicing.SeesOrderAsync(context, order.Id, cancellationToken) ? order : null;
    }

    /// <summary>
    /// The folio as the ticket prints it: «ORD-000123», «ord 000123», «000123» and «123» are the same ticket; null when it
    /// cannot be one.
    /// </summary>
    public static string? NormalizeFolio(string? typed)
    {
        var folio = typed?.ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (string.IsNullOrEmpty(folio))
            return null;
        if (folio.StartsWith(Folios.OrderScope, StringComparison.Ordinal))
            folio = folio[Folios.OrderScope.Length..];
        return folio.Length is > 0 and <= 12 && folio.All(char.IsAsciiDigit) && long.TryParse(folio, out var number) && number > 0
            ? Folios.OrderNumber(number)
            : null;
    }

    public static Task<OrderInvoice?> LiveInvoiceAsync(IApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
        => context.OrderInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == orderId
                && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                    || i.Status == OrderInvoiceStatus.Cancelling), cancellationToken);

    /// <summary>Why the order itself is not invoiced online, for the customer; null when nothing of it stands in the way.</summary>
    public static async Task<string?> OrderProblemAsync(IApplicationDbContext context, Order order, CancellationToken cancellationToken)
    {
        if (order.Status == OrderStatus.Cancelled)
            return "Esta compra se canceló: no se factura.";
        if (order.Total <= 0)
            return "Esta compra no tiene importe que facturar.";
        if (order.Total - order.PaidAmount > 0 || order.Payments.Count == 0)
            return "Esta compra todavía no está pagada por completo: pídele tu factura al negocio.";
        if (await OrderInvoicing.GlobalInvoiceProblemAsync(context, order.Id, cancellationToken) is not null)
            return "Esta compra ya está en la factura global del negocio: pídele tu factura directamente.";
        return await OrderInvoicing.OrderProblemAsync(context, order, cancellationToken) is null ? null : AskTheBusinessMessage;
    }

    /// <summary>
    /// Why the sale is not invoiced online, by its CFDI draft: paid in one exhibition, ready to stamp, not of a delivery app,
    /// and until the last day of the month it was paid in full (in the business's calendar).
    /// </summary>
    public static string? SaleProblem(CfdiDraftDto draft, Order order, string? timeZone, DateTimeOffset now)
    {
        if (order.IsDeliveryPlatformOrder)
            return "Los pedidos de apps de entrega no se facturan en línea: pídele tu factura al negocio.";
        if (draft.PaymentMethod != CfdiInvoice.PaidInOneExhibition)
            return "Esta compra todavía no está pagada por completo: pídele tu factura al negocio.";
        if (!draft.IsReadyToStamp || PaidAt(order) is not { } paidAt)
            return AskTheBusinessMessage;
        var invoiceBy = InvoiceBy(paidAt, timeZone);
        return DateOnly.FromDateTime(TenantDateTimeResolver.LocalNow(now, timeZone)) > invoiceBy
            ? $"Esta compra se podía facturar en línea hasta el {SpanishCalendar.LongDate(invoiceBy, withYear: true)}: pídele tu factura al negocio."
            : null;
    }

    /// <summary>When the sale was paid in full: its last payment.</summary>
    public static DateTimeOffset? PaidAt(Order order) => order.Payments.Count == 0 ? null : order.Payments.Max(p => p.PaidAt);

    /// <summary>The last day of the month the sale was paid in full, in the business's calendar.</summary>
    public static DateOnly InvoiceBy(DateTimeOffset paidAt, string? timeZone)
    {
        var local = TenantDateTimeResolver.LocalNow(paidAt, timeZone);
        return new DateOnly(local.Year, local.Month, 1).AddMonths(1).AddDays(-1);
    }
}
