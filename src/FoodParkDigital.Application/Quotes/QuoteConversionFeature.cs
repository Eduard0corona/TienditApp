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

    public static string IntakeNote(Quote quote) => $"De la cotización {quote.Number} (versión {quote.Revision}).";
}

/// <summary>
/// Turns an accepted quote into its service order, in one transaction:
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
    private readonly ICustomerLookupService _customerLookup;
    private readonly IPhoneNormalizationService _phones;
    private readonly IOrderInventoryService _inventory;
    private readonly IOrderCashService _cash;
    private readonly IFolioGenerator _folios;
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
        _customerLookup = customerLookup;
        _phones = phones;
        _inventory = inventory;
        _cash = cash;
        _folios = folios;
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

        // What the quote takes off for the visit now, never more than the job's total: read from the sale of the visit as it is
        // tracked to refund it, so a change of that sale meanwhile collides on its version when this is saved.
        Order? visitSale = null;
        if (quote.VisitCreditSaleOrderId is { } visitSaleId)
        {
            visitSale = await OrderRefundQueries.LoadForRefundAsync(_context, visitSaleId, cancellationToken);
            if (visitSale is null || visitSale.TenantId != TenantId)
                throw new EntityNotFoundException(nameof(Order), visitSaleId);
        }
        var stillPaid = visitSale is null
            ? null
            : QuoteVisitCredits.PaidForTheVisit(visitSale, await QuoteVisitCredits.VisitServiceAsync(_context, quote, cancellationToken));
        var applied = Math.Min(quote.VisitCreditStanding(stillPaid), quote.Total);
        if (applied > 0)
        {
            var permissions = await _access.GetPermissionsAsync(cancellationToken);
            if (!permissions.Contains(nameof(TenantPermission.ChargeOrders)) || !permissions.Contains(nameof(TenantPermission.ProcessRefunds)))
                throw new ForbiddenAccessException(QuoteConversions.ChargeToApplyVisitMessage);
        }

        // The customer by the quote's phone. A quote only asks that it is written, and an accepted one is no longer corrected: one
        // that does not read as a phone takes the phone of the sale of its visit (kept in E.164), or none, as a counter sale.
        var phone = _phones.TryNormalizeToE164(quote.CustomerPhone, CustomerPhoneOwnershipRules.DefaultCountry, out var quotePhone)
            ? quotePhone
            : visitSale?.CustomerPhone;
        var customer = string.IsNullOrWhiteSpace(phone)
            ? null
            : await _customerLookup.ResolveForOrderAsync(
                TenantId, phone, quote.CustomerName, CustomerPhoneOwnershipRules.DefaultCountry, cancellationToken);

        var branch = await BranchRules.ResolveForSaleAsync(
            _context, TenantId, null, Guid.TryParse(_currentUser.UserId, out var userId) ? userId : null, cancellationToken);
        if (branch.IsFailure)
            return Result.Failure<QuoteDto>(branch.Error!);
        var businessType = await _context.Tenants
            .Where(t => t.Id == TenantId)
            .Select(t => t.BusinessType)
            .FirstAsync(cancellationToken);
        var now = _clock.UtcNow;

        var order = new Order(
            tenantId: TenantId,
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
            .Where(p => p.TenantId == TenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var lines = QuoteConversion.Lines(quote, products.ToDictionary(p => p.Key, p => OrderLineFiscalData.From(p.Value)));
        foreach (var line in lines)
            order.AddItem(line);
        var trackable = lines
            .Where(line => products.TryGetValue(line.ProductId, out var product)
                && OrderProductClassifier.IsServiceUnit(product.Unit) && product.IsTrackable)
            .Select(line => (products[line.ProductId], new CreateOrderItemDto { ProductId = line.ProductId, Quantity = line.Quantity }))
            .ToList();
        order.SetServiceDetail(now, request.PromisedAt, QuoteConversions.NormalPriority, QuoteConversions.IntakeNote(quote),
            null, null, null, null);
        if (OrderPlacement.AddServicePieces(order, null, trackable, lines.Select(l => l.ProductId).ToHashSet()) is { } piecesError)
            return Result.Failure<QuoteDto>(piecesError);
        OrderPlacement.ApplyBusinessDefaultStatus(order, _currentUser.UserName);
        if (order.Total != quote.Total)
            return Result.Failure<QuoteDto>(QuoteConversions.TotalMismatchMessage);

        order.AssignNumber(await _folios.NextOrderNumberAsync(TenantId, cancellationToken));

        if (applied > 0)
        {
            var visitApplied = await ApplyVisitAsync(quote, visitSale!, order, applied, cancellationToken);
            if (visitApplied is not null)
                return Result.Failure<QuoteDto>(QuoteConversions.VisitNotAppliedMessage(visitApplied));
        }

        // The customer accepted the job: a shortage warns, never refuses it.
        await _inventory.ApplyConsumptionForOrderAsync(order, cancellationToken, StockShortageHandling.Allow);

        try
        {
            quote.ConvertToOrder(order.Id, applied, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteDto>(ex.Message);
        }

        order.MarkAsCreated();
        _context.Orders.Add(order);
        _context.AuditLogs.Add(QuoteAudit.Create(quote, QuoteAudit.Converted, _currentUser.UserName,
            new { order.OrderNumber, AppliedVisitCredit = applied }));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The quote or the sale of its visit changed at the same time: the other change stands, and this one is undone
            // whole. Converted by someone else (into another order: this transaction may have already written its own before
            // the sale collided), it says so.
            var converted = await _context.Quotes
                .AsNoTracking()
                .AnyAsync(q => q.Id == quote.Id && q.TenantId == TenantId && q.OrderId != null && q.OrderId != order.Id,
                    cancellationToken);
            return Result.Failure<QuoteDto>(converted ? QuoteRules.AlreadyConvertedMessage : QuoteConversions.ChangedMeanwhileMessage);
        }

        return Result.Success(await QuoteViews.DescribeAsync(_context, quote,
            await QuoteCalendar.TodayAsync(_context, TenantId, now, cancellationToken), cancellationToken));
    }

    /// <summary>
    /// What was paid for the visit goes from its sale to the order: refunded to the balance in favor of the sale's phone and
    /// paid from it into the order, both now. Null once applied; otherwise why the sale of the visit could not give it back.
    /// </summary>
    private async Task<string?> ApplyVisitAsync(Quote quote, Order visitSale, Order order, decimal amount, CancellationToken cancellationToken)
    {
        var refunds = new OrderRefundProcessor(_context, _currentUser, _cash, _inventory, _clock);
        var refund = await refunds.RefundAsync(visitSale, new OrderRefundInput(
            amount, OrderPaymentMethod.CustomerBalance, QuoteConversions.RefundReason(quote, order), RestockItems: false, [], null,
            order.OrderNumber), cancellationToken);
        if (refund.IsFailure)
            return refund.Error;

        // The same account the refund went to: the phone of the sale of the visit.
        var account = MessagingPhone.ToKey(visitSale.CustomerPhone!);
        var customerId = (await CreditAccount.RecordAsync(_context, account, cancellationToken, tracked: false))?.Id;
        var payment = await new OrderPaymentRegistrar(_context, _cash, _clock).ApplyCustomerBalanceAsync(
            order, amount, QuoteConversions.PaymentNote(visitSale), account, customerId, _currentUser.UserName ?? "system",
            cancellationToken);
        return payment.IsFailure ? payment.Error : null;
    }
}
