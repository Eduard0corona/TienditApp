using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Customers;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Quotes;

/// <summary>
/// The accepted quote becomes the job (Fase 29E, ADR 0053 point 17): a service order with its lines at the price and with
/// the IVA the customer accepted, once. <paramref name="PromisedAt"/> is when the business promises it, as in any service order.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record ConvertQuoteToOrderCommand(Guid Id, DateTimeOffset? PromisedAt) : IRequest<Result<QuoteDto>>, ITransactionalRequest;

public class ConvertQuoteToOrderCommandValidator : AbstractValidator<ConvertQuoteToOrderCommand>
{
    public ConvertQuoteToOrderCommandValidator(IDateTimeProvider clock)
    {
        RuleFor(x => x.Id).NotEmpty();
        // A promise within a year: one years away is a typo. One before today depends on the business's day (the handler).
        RuleFor(x => x.PromisedAt)
            .Must(at => at is null || at <= clock.UtcNow.AddYears(1))
            .WithMessage(QuoteConversions.PromisedAtMessage);
    }
}

/// <summary>The rules and messages of turning an accepted quote into its service order.</summary>
public static class QuoteConversions
{
    public const string PromisedAtMessage = "La fecha prometida va de hoy a un año.";
    /// <summary>The priority a service order starts with at the counter (1 low, 3 normal, 5 urgent).</summary>
    public const int NormalPriority = 3;
    public const string ChargeToApplyVisitMessage =
        "Lo que se pagó de la visita pasa de su venta a la orden: conviértela con alguien que pueda cobrar y hacer reembolsos.";
    public const string ChangedMeanwhileMessage =
        "La cotización o la venta de su visita cambiaron al mismo tiempo: revisa la cotización y vuelve a intentarlo.";
    public const string TotalMismatchMessage = "La orden no sumaría lo que el cliente aceptó: revisa la cotización.";

    /// <summary>
    /// A promise goes from the start of today where the business lives its days: one before it, even hours ago in UTC,
    /// is yesterday for the business, a typo.
    /// </summary>
    public static bool PromisedBeforeToday(DateTimeOffset? promisedAt, DateTimeOffset now, string? timeZone)
    {
        if (promisedAt is null)
            return false;
        var zone = TenantDateTimeResolver.Resolve(timeZone);
        return promisedAt < TenantDateTimeResolver.StartOfLocalDayUtc(TimeZoneInfo.ConvertTime(now, zone).Date, zone);
    }

    /// <summary>«No se pudo aplicar lo que se pagó de la visita: …» with the reason the sale of the visit gave.</summary>
    public static string VisitNotAppliedMessage(string reason) => $"No se pudo aplicar lo que se pagó de la visita: {reason}";

    /// <summary>Why the sale of the visit gives back what goes to the order: the quote and the order, by their folios.</summary>
    public static string RefundReason(Quote quote, Order order)
        => $"Se aplica a la orden {order.OrderNumber}: el cliente contrató la cotización {quote.Number} (versión {quote.Revision}).";

    public static string PaymentNote(Order visitSale) => $"Lo que se pagó de la visita (venta {visitSale.OrderNumber}).";

    /// <summary>
    /// «De la cotización COT-… (versión …).» and, when its trackable services would give more pieces than an order generates,
    /// that the order goes without them.
    /// </summary>
    public static string IntakeNote(Quote quote, int? piecesLeftOut = null)
        => $"De la cotización {quote.Number} (versión {quote.Revision})."
            + (piecesLeftOut is { } pieces
                ? $" Sin piezas: serían {pieces} y una orden genera como máximo {OrderLimits.MaxServicePiecesPerOrder}."
                : string.Empty);

    /// <summary>A quote whose order would give more pieces than an order generates: refused while it is still a draft.</summary>
    public static string TooManyPiecesMessage(int pieces)
        => $"Esta cotización generaría {pieces} piezas de servicio y una orden genera como máximo {OrderLimits.MaxServicePiecesPerOrder}: divídela en dos cotizaciones.";

    /// <summary>The pieces an order gives its trackable services: one per unit, as at the counter.</summary>
    public static int ServicePieces(IEnumerable<(Domain.Catalog.Entities.Product Product, decimal Quantity)> lines)
        => (int)lines
            .Where(line => line.Product.IsTrackable && OrderProductClassifier.IsServiceUnit(line.Product.Unit))
            .Sum(line => decimal.Ceiling(line.Quantity));
}

/// <summary>
/// Turns an accepted quote into its service order, in one transaction (the order itself is <see cref="QuoteOrderConverter"/>):
/// <list type="number">
/// <item>The order is the business's: accepted for the team, with the quote's customer, lines and tax policy, so it totals
/// what the customer accepted; it takes the supplies of the catalog's products that have a recipe, never refused for a
/// shortage (the customer already accepted).</item>
/// <item>What was paid for the visit and the quote takes off (ADR 0053 point 14) leaves the sale of the visit for the order:
/// that sale gives it back to the balance in favor of the account of its phone, and the order is paid with it at once (as
/// «Saldo a favor», the invoice's «aplicación de anticipos»). Nothing enters or leaves a drawer and the balance ends as it
/// was, so the business's revenue is the job's total. It is a refund and a payment: it needs who may charge and refund.
/// What is applied is read from the sale of the visit as this request loads it to refund it, so a refund of that sale at the
/// same time collides on its version instead of both taking the same money.</item>
/// <item>The quote keeps its order and what it applied: a second conversion, also at the same time, collides on the quote's
/// version and is undone whole.</item>
/// </list>
/// </summary>
public class ConvertQuoteToOrderCommandHandler : IRequestHandler<ConvertQuoteToOrderCommand, Result<QuoteDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly CurrentAccessContext _access;
    private readonly QuoteOrderConverter _converter;
    private readonly IDateTimeProvider _clock;

    public ConvertQuoteToOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        CurrentAccessContext access,
        ICustomerLookupService customerLookup,
        IPhoneNormalizationService phones,
        IOrderInventoryService inventory,
        IOrderCashService cash,
        IFolioGenerator folios,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _access = access;
        _converter = new QuoteOrderConverter(context, customerLookup, phones, inventory, cash, folios, clock);
        _clock = clock;
    }

    private Guid TenantId => _tenantService.TenantId;

    public async Task<Result<QuoteDto>> Handle(ConvertQuoteToOrderCommand request, CancellationToken cancellationToken)
    {
        var quote = await QuotesHandler.FindAsync(_context, TenantId, request.Id, cancellationToken);
        if (quote.Status != QuoteStatus.Accepted)
            return Result.Failure<QuoteDto>(QuoteRules.NotAcceptedMessage);
        if (quote.OrderId is not null)
            return Result.Failure<QuoteDto>(QuoteRules.AlreadyConvertedMessage);

        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, TenantId, cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(capabilities, OrderType.Service))
            return Result.Failure<QuoteDto>(QuoteRules.NoServiceOrdersMessage);

        if (request.PromisedAt is not null)
        {
            var timeZone = await _context.TenantSettings.AsNoTracking()
                .Where(s => s.TenantId == TenantId)
                .Select(s => s.TimeZone)
                .FirstOrDefaultAsync(cancellationToken);
            if (QuoteConversions.PromisedBeforeToday(request.PromisedAt, _clock.UtcNow, timeZone))
                return Result.Failure<QuoteDto>(QuoteConversions.PromisedAtMessage);
        }

        var visit = await _converter.VisitCreditAsync(quote, cancellationToken);
        if (visit.Applied > 0)
        {
            var permissions = await _access.GetPermissionsAsync(cancellationToken);
            if (!permissions.Contains(nameof(TenantPermission.ChargeOrders)) || !permissions.Contains(nameof(TenantPermission.ProcessRefunds)))
                throw new ForbiddenAccessException(QuoteConversions.ChargeToApplyVisitMessage);
        }

        var converted = await _converter.ConvertAsync(quote, visit, request.PromisedAt, _currentUser, cancellationToken);
        if (converted.IsFailure)
            return Result.Failure<QuoteDto>(converted.Error!);
        var order = converted.Value!;
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The quote or the sale of its visit changed at the same time: the other change stands, and this one is undone
            // whole. Converted by someone else (into another order: this transaction may have already written its own before
            // the sale collided), it says so.
            var convertedElsewhere = await _context.Quotes
                .AsNoTracking()
                .AnyAsync(q => q.Id == quote.Id && q.TenantId == TenantId && q.OrderId != null && q.OrderId != order.Id,
                    cancellationToken);
            return Result.Failure<QuoteDto>(convertedElsewhere ? QuoteRules.AlreadyConvertedMessage : QuoteConversions.ChangedMeanwhileMessage);
        }

        return Result.Success(await QuoteViews.DescribeAsync(_context, quote,
            await QuoteCalendar.TodayAsync(_context, TenantId, _clock.UtcNow, cancellationToken), cancellationToken));
    }
}

/// <summary>
/// Who turns a quote into its order when no one of the team does (Fase 31N, ADR 0094): the customer who paid its deposit from
/// the link. The order, its payments and the activity say «Cliente (enlace)».
/// </summary>
internal sealed class QuoteLinkCustomer : ICurrentUserService
{
    public static readonly QuoteLinkCustomer Instance = new();

    public string? UserId => null;
    public string? UserName => QuoteAudit.CustomerFromLink;
    public string? Role => null;
    public string? TenantRole => null;
    public IReadOnlyCollection<string> Permissions => [];
    public Guid? TenantId => null;
}

/// <summary>
/// The service order of an accepted quote (Fase 29E), as <see cref="ConvertQuoteToOrderCommandHandler"/> describes it, built,
/// numbered and added to the context with the quote marked converted, but not saved: the caller saves it in its transaction.
/// Since the Fase 31N (ADR 0094) a deposit paid from the link converts the quote too, as «Cliente (enlace)», in the default
/// branch.
/// </summary>
internal sealed class QuoteOrderConverter
{
    private readonly IApplicationDbContext _context;
    private readonly ICustomerLookupService _customerLookup;
    private readonly IPhoneNormalizationService _phones;
    private readonly IOrderInventoryService _inventory;
    private readonly IOrderCashService _cash;
    private readonly IFolioGenerator _folios;
    private readonly IDateTimeProvider _clock;

    public QuoteOrderConverter(
        IApplicationDbContext context,
        ICustomerLookupService customerLookup,
        IPhoneNormalizationService phones,
        IOrderInventoryService inventory,
        IOrderCashService cash,
        IFolioGenerator folios,
        IDateTimeProvider clock)
    {
        _context = context;
        _customerLookup = customerLookup;
        _phones = phones;
        _inventory = inventory;
        _cash = cash;
        _folios = folios;
        _clock = clock;
    }

    /// <summary>The sale of the visit, tracked to refund it, and what the quote takes off for the visit now.</summary>
    public sealed record VisitCredit(Order? Sale, decimal Applied);

    /// <summary>
    /// What the quote takes off for the visit now, never more than the job's total: read from the sale of the visit as it is
    /// tracked to refund it, so a change of that sale meanwhile collides on its version when this is saved.
    /// </summary>
    public async Task<VisitCredit> VisitCreditAsync(Quote quote, CancellationToken cancellationToken)
    {
        Order? visitSale = null;
        if (quote.VisitCreditSaleOrderId is { } visitSaleId)
        {
            visitSale = await OrderRefundQueries.LoadForRefundAsync(_context, visitSaleId, cancellationToken);
            if (visitSale is null || visitSale.TenantId != quote.TenantId)
                throw new EntityNotFoundException(nameof(Order), visitSaleId);
        }
        var stillPaid = visitSale is null
            ? null
            : QuoteVisitCredits.PaidForTheVisit(visitSale, await QuoteVisitCredits.VisitServiceAsync(_context, quote, cancellationToken));
        return new VisitCredit(visitSale, Math.Min(quote.VisitCreditStanding(stillPaid), quote.Total));
    }

    /// <summary>
    /// The service order of <paramref name="quote"/> (accepted, not converted, of a business with service orders), with what
    /// <paramref name="visit"/> applies for the visit, as <paramref name="actor"/>: in the branch of their open drawer, else
    /// the default one. Added to the context with the quote converted and its activity; or why it cannot be.
    /// </summary>
    public async Task<Result<Order>> ConvertAsync(
        Quote quote, VisitCredit visit, DateTimeOffset? promisedAt, ICurrentUserService actor, CancellationToken cancellationToken)
    {
        if (quote.Status != QuoteStatus.Accepted)
            return Result.Failure<Order>(QuoteRules.NotAcceptedMessage);
        if (quote.OrderId is not null)
            return Result.Failure<Order>(QuoteRules.AlreadyConvertedMessage);
        var tenantId = quote.TenantId;
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, tenantId, cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(capabilities, OrderType.Service))
            return Result.Failure<Order>(QuoteRules.NoServiceOrdersMessage);

        // The customer by the quote's phone. A quote only asks that it is written, and an accepted one is no longer corrected: one
        // that does not read as a phone takes the phone of the sale of its visit (kept in E.164), or none, as a counter sale.
        var phone = _phones.TryNormalizeToE164(quote.CustomerPhone, CustomerPhoneOwnershipRules.DefaultCountry, out var quotePhone)
            ? quotePhone
            : visit.Sale?.CustomerPhone;
        var customer = string.IsNullOrWhiteSpace(phone)
            ? null
            : await _customerLookup.ResolveForOrderAsync(
                tenantId, phone, quote.CustomerName, CustomerPhoneOwnershipRules.DefaultCountry, cancellationToken);

        var branch = await BranchRules.ResolveForSaleAsync(
            _context, tenantId, null, Guid.TryParse(actor.UserId, out var userId) ? userId : null, cancellationToken);
        if (branch.IsFailure)
            return Result.Failure<Order>(branch.Error!);
        var businessType = await _context.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.BusinessType)
            .FirstAsync(cancellationToken);
        var now = _clock.UtcNow;

        var order = new Order(
            tenantId: tenantId,
            customerUserId: customer?.CustomerUserId,
            temporaryCustomerId: customer?.TemporaryCustomerId,
            createdByBusiness: true,
            orderNumber: Order.NumberNotAssigned,
            businessType: businessType,
            orderType: OrderType.Service,
            customerName: quote.CustomerName,
            customerPhone: customer?.PhoneNormalized,
            customerNotes: null,
            tableNumber: null,
            pricesIncludeTax: quote.PricesIncludeTax,
            channel: OrderChannel.Counter,
            branchId: branch.Value);

        // Its lines, at the accepted prices, and one piece per unit of each trackable service, as at the counter.
        var productIds = quote.Lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = await _context.Products
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var lines = QuoteConversion.Lines(quote, products.ToDictionary(p => p.Key, p => OrderLineFiscalData.From(p.Value)));
        foreach (var line in lines)
            order.AddItem(line);
        var trackable = lines
            .Where(line => products.TryGetValue(line.ProductId, out var product)
                && OrderProductClassifier.IsServiceUnit(product.Unit) && product.IsTrackable)
            .Select(line => (products[line.ProductId], new CreateOrderItemDto { ProductId = line.ProductId, Quantity = line.Quantity }))
            .ToList();
        // A draft past what an order generates is refused, but a product may have become trackable since, and an accepted
        // quote is no longer corrected: past it, the order goes without pieces and its intake note says so, never stuck.
        var pieces = QuoteConversions.ServicePieces(trackable.Select(t => (t.Item1, t.Item2.Quantity)));
        var withPieces = pieces <= OrderLimits.MaxServicePiecesPerOrder;
        order.SetServiceDetail(now, promisedAt, QuoteConversions.NormalPriority,
            QuoteConversions.IntakeNote(quote, withPieces ? null : pieces), null, null, null, null);
        if (withPieces && OrderPlacement.AddServicePieces(order, null, trackable, lines.Select(l => l.ProductId).ToHashSet()) is { } piecesError)
            return Result.Failure<Order>(piecesError);
        OrderPlacement.ApplyBusinessDefaultStatus(order, actor.UserName);
        if (order.Total != quote.Total)
            return Result.Failure<Order>(QuoteConversions.TotalMismatchMessage);

        order.AssignNumber(await _folios.NextOrderNumberAsync(tenantId, cancellationToken));

        if (visit.Applied > 0)
        {
            var visitApplied = await ApplyVisitAsync(quote, visit.Sale!, order, visit.Applied, actor, cancellationToken);
            if (visitApplied is not null)
                return Result.Failure<Order>(QuoteConversions.VisitNotAppliedMessage(visitApplied));
        }

        // The customer accepted the job: a shortage warns, never refuses it.
        await _inventory.ApplyConsumptionForOrderAsync(order, cancellationToken, StockShortageHandling.Allow);

        try
        {
            quote.ConvertToOrder(order.Id, visit.Applied, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Order>(ex.Message);
        }

        order.MarkAsCreated();
        _context.Orders.Add(order);
        _context.AuditLogs.Add(QuoteAudit.Create(quote, QuoteAudit.Converted, actor.UserName,
            new { order.OrderNumber, AppliedVisitCredit = visit.Applied }));
        return Result.Success(order);
    }

    /// <summary>
    /// What was paid for the visit goes from its sale to the order: refunded to the balance in favor of the sale's phone and
    /// paid from it into the order, both now. Null once applied; otherwise why the sale of the visit could not give it back.
    /// </summary>
    private async Task<string?> ApplyVisitAsync(
        Quote quote, Order visitSale, Order order, decimal amount, ICurrentUserService actor, CancellationToken cancellationToken)
    {
        var refunds = new OrderRefundProcessor(_context, actor, _cash, _inventory, _clock);
        var refund = await refunds.RefundAsync(visitSale, new OrderRefundInput(
            amount, OrderPaymentMethod.CustomerBalance, QuoteConversions.RefundReason(quote, order), RestockItems: false, [], null,
            order.OrderNumber), cancellationToken);
        if (refund.IsFailure)
            return refund.Error;

        // The same account the refund went to: the phone of the sale of the visit.
        var account = MessagingPhone.ToKey(visitSale.CustomerPhone!);
        var customerId = (await CreditAccount.RecordAsync(_context, account, cancellationToken, tracked: false))?.Id;
        var payment = await new OrderPaymentRegistrar(_context, _cash, _clock).ApplyCustomerBalanceAsync(
            order, amount, QuoteConversions.PaymentNote(visitSale), account, customerId, actor.UserName ?? "system",
            cancellationToken);
        return payment.IsFailure ? payment.Error : null;
    }
}
