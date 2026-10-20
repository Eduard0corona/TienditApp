using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers;

/// <summary>Who owes the business (Fase 26A): a row per customer, by phone.</summary>
public class ReceivableDto
{
    /// <summary>The phone the customer's orders are grouped by (+52 and ten digits for Mexico).</summary>
    public string Phone { get; set; } = null!;
    /// <summary>The customer record's name, or the name on their latest order owed.</summary>
    public string Name { get; set; } = null!;
    /// <summary>The customer record with this phone; null when the business has none.</summary>
    public Guid? CustomerId { get; set; }
    public decimal Owed { get; set; }
    public int Orders { get; set; }
    /// <summary>When the oldest order owed was made.</summary>
    public DateTimeOffset Since { get; set; }
    /// <summary>How much the business lets them owe; null when they are not sold on credit.</summary>
    public decimal? CreditLimit { get; set; }
    /// <summary>They owe more than their limit (it was lowered, or orders were delivered unpaid).</summary>
    public bool OverLimit { get; set; }
}

public class ReceivablesDto
{
    public PaginatedList<ReceivableDto> Page { get; set; } = null!;
    /// <summary>What all the customers that match owe the business.</summary>
    public decimal TotalOwed { get; set; }
}

public enum ReceivablesSort
{
    /// <summary>Who owes the most first.</summary>
    Owed = 0,
    /// <summary>Who owes since longest ago first.</summary>
    Oldest = 1
}

/// <summary>The account of a customer (Fase 26A): what they may and do owe, the orders owed and their latest payments.</summary>
public class CustomerAccountDto
{
    public string Phone { get; set; } = null!;
    public string? Name { get; set; }
    public Guid? CustomerId { get; set; }
    public decimal? CreditLimit { get; set; }
    /// <summary>The business sells to them on credit now: an active customer with a phone and a limit.</summary>
    public bool SellsOnCredit { get; set; }
    public decimal Owed { get; set; }
    /// <summary>What they may still owe (never below 0); null without a limit.</summary>
    public decimal? Available { get; set; }
    public bool OverLimit { get; set; }
    /// <summary>The orders owed, the oldest first (at most <see cref="GetCustomerAccountQuery.MaxOrders"/>).</summary>
    public List<CustomerAccountOrderDto> Orders { get; set; } = [];
    /// <summary>How many orders they owe, also beyond the ones listed.</summary>
    public int OrdersCount { get; set; }
    /// <summary>Their latest payments, on any of their orders.</summary>
    public List<CustomerAccountPaymentDto> RecentPayments { get; set; } = [];
}

public class CustomerAccountOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public OrderStatus Status { get; set; }
    public bool SoldOnCredit { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Balance { get; set; }
}

public class CustomerAccountPaymentDto
{
    public string OrderNumber { get; set; } = null!;
    public DateTimeOffset PaidAt { get; set; }
    public decimal Amount { get; set; }
    public OrderPaymentMethod Method { get; set; }
}

/// <summary>
/// How much the business lets a customer owe (Fase 26A): from $1 to $1,000,000, or no limit (they are not sold on credit).
/// The owner's decision, recorded in the activity log. A limit below what they owe only stops selling them more on credit.
/// </summary>
[RequiresTenantManager]
public record UpdateCustomerCreditLimitCommand : IRequest<Result<CustomerDto>>
{
    public Guid CustomerId { get; init; }
    public decimal? CreditLimit { get; init; }
}

public class UpdateCustomerCreditLimitCommandValidator : AbstractValidator<UpdateCustomerCreditLimitCommand>
{
    public UpdateCustomerCreditLimitCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.CreditLimit!.Value)
            .InclusiveBetween(1m, Customer.MaxCreditLimit)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.CreditLimit.HasValue)
            .WithName(nameof(UpdateCustomerCreditLimitCommand.CreditLimit))
            .WithMessage(Customer.CreditLimitRangeMessage);
    }
}

public class UpdateCustomerCreditLimitCommandHandler : IRequestHandler<UpdateCustomerCreditLimitCommand, Result<CustomerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;

    public UpdateCustomerCreditLimitCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    public async Task<Result<CustomerDto>> Handle(UpdateCustomerCreditLimitCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new EntityNotFoundException("Customer", request.CustomerId);

        var previous = customer.CreditLimit;
        if (previous == request.CreditLimit)
            return Result.Success(CustomerDto.From(customer));

        try
        {
            customer.SetCreditLimit(request.CreditLimit);
        }
        catch (DomainException refused)
        {
            return Result.Failure<CustomerDto>(refused.Message);
        }

        // Who the business trusts with credit, and with how much, is the owner's decision: worth recording.
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: nameof(Customer),
            entityId: customer.Id.ToString(),
            action: "CustomerCreditLimitUpdated",
            performedBy: _currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new { Previous = previous, New = customer.CreditLimit })));
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CustomerDto.From(customer));
    }
}

/// <summary>
/// Clientes › Por cobrar (Fase 26A): who owes the business and how much, by phone (<see cref="Receivables"/>), with the
/// total. Whoever looks after customers reads it, and so does whoever charges, who collects the payments.
/// </summary>
[RequiresPermission(TenantPermission.ManageCustomers, TenantPermission.ChargeOrders)]
public record GetReceivablesQuery : IRequest<ReceivablesDto>
{
    /// <summary>Part of the customer's name or of their phone.</summary>
    public string? Search { get; init; }
    public ReceivablesSort Sort { get; init; } = ReceivablesSort.Owed;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetReceivablesQueryValidator : AbstractValidator<GetReceivablesQuery>
{
    public GetReceivablesQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Sort).IsInEnum();
    }
}

public class GetReceivablesQueryHandler : IRequestHandler<GetReceivablesQuery, ReceivablesDto>
{
    // Customer records whose name matches a search, to find what they owe under their phone.
    private const int SearchedCustomers = 50;
    // A phone search needs a few digits: one or two match almost everyone.
    private const int MinPhoneDigits = 3;

    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;

    public GetReceivablesQueryHandler(IApplicationDbContext context, IPhoneNormalizationService phones)
    {
        _context = context;
        _phones = phones;
    }

    public async Task<ReceivablesDto> Handle(GetReceivablesQuery request, CancellationToken cancellationToken)
    {
        var lines = Receivables.Lines(_context.Orders.AsNoTracking());
        if (!string.IsNullOrWhiteSpace(request.Search))
            lines = await MatchingAsync(lines, request.Search.Trim(), cancellationToken);

        var customers = lines
            .GroupBy(l => l.Phone)
            .Select(g => new
            {
                Phone = g.Key,
                Owed = g.Sum(l => l.Balance),
                Orders = g.Count(),
                Since = g.Min(l => l.CreatedAt)
            });
        var ordered = request.Sort == ReceivablesSort.Oldest
            ? customers.OrderBy(c => c.Since).ThenBy(c => c.Phone)
            : customers.OrderByDescending(c => c.Owed).ThenBy(c => c.Phone);

        var (pageNumber, pageSize) = Paging.Normalize(request.PageNumber, request.PageSize);
        var count = await customers.CountAsync(cancellationToken);
        var totalOwed = await lines.SumAsync(l => (decimal?)l.Balance, cancellationToken) ?? 0m;
        var page = await ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        // The page's names and limits: the customer record with each phone, else the name on the latest order owed.
        var phones = page.Select(c => c.Phone).ToList();
        var records = await RecordsAsync(phones, cancellationToken);
        var latestNames = (await lines
                .Where(l => phones.Contains(l.Phone))
                .Select(l => new { l.Phone, l.CustomerName, l.CreatedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.Phone)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).First().CustomerName);

        var items = page.Select(c =>
        {
            records.TryGetValue(c.Phone, out var record);
            // A customer record deactivated keeps its limit, but they are not sold on credit: no limit to show or pass.
            var limit = record is { SellsOnCredit: true } ? record.CreditLimit : null;
            return new ReceivableDto
            {
                Phone = c.Phone,
                Name = record?.Name ?? latestNames.GetValueOrDefault(c.Phone) ?? c.Phone,
                CustomerId = record?.Id,
                Owed = c.Owed,
                Orders = c.Orders,
                Since = c.Since,
                CreditLimit = limit,
                OverLimit = limit is { } value && c.Owed > value
            };
        }).ToList();

        return new ReceivablesDto
        {
            Page = new PaginatedList<ReceivableDto>(items, count, pageNumber, pageSize),
            TotalOwed = totalOwed
        };
    }

    /// <summary>
    /// The lines of the customers that match: by the name on an order owed, by a customer record's name, or by part of the
    /// phone. Every order of a customer who matches stays, so what they owe is all of it.
    /// </summary>
    private async Task<IQueryable<OwedOrder>> MatchingAsync(IQueryable<OwedOrder> lines, string search, CancellationToken cancellationToken)
    {
        // Lowercased like the customers list, so it reads the same whatever the database's collation.
        var text = search.ToLowerInvariant();
        var recordPhones = (await _context.Customers.AsNoTracking()
                .Where(c => !c.IsPublicGeneral && c.Phone != null && c.Name.ToLower().Contains(text))
                .OrderByDescending(c => c.IsActive)
                .ThenBy(c => c.Name)
                .Take(SearchedCustomers)
                .Select(c => c.Phone!)
                .ToListAsync(cancellationToken))
            .Select(phone => CustomerPhones.TryNormalize(_phones, phone, out var key) ? key : null)
            .OfType<string>()
            .Distinct()
            .ToList();

        var digits = new string(search.Where(char.IsAsciiDigit).ToArray());
        var matching = digits.Length >= MinPhoneDigits
            ? lines.Where(l => l.CustomerName!.ToLower().Contains(text) || recordPhones.Contains(l.Phone) || l.Phone.Contains(digits))
            : lines.Where(l => l.CustomerName!.ToLower().Contains(text) || recordPhones.Contains(l.Phone));
        var phones = matching.Select(l => l.Phone);
        return lines.Where(l => phones.Contains(l.Phone));
    }

    /// <summary>The customer record of each phone, the active one first when several share it.</summary>
    private async Task<Dictionary<string, Customer>> RecordsAsync(IReadOnlyList<string> phones, CancellationToken cancellationToken)
    {
        var forms = phones.SelectMany(CustomerPhones.StoredForms).Distinct().ToList();
        var records = await _context.Customers.AsNoTracking()
            .Where(c => !c.IsPublicGeneral && c.Phone != null && forms.Contains(c.Phone))
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.CreditLimit != null)
            .ToListAsync(cancellationToken);

        var byPhone = new Dictionary<string, Customer>();
        foreach (var record in records)
        {
            if (CustomerPhones.TryNormalize(_phones, record.Phone, out var key))
                byPhone.TryAdd(key, record);
        }

        return byPhone;
    }
}

/// <summary>
/// The account of a customer (Fase 26A), by customer record or phone: their limit, what they owe and what they may still
/// owe, the orders owed and their latest payments. What the cashier looks at before selling on credit or taking a payment.
/// </summary>
[RequiresPermission(TenantPermission.ManageCustomers, TenantPermission.ChargeOrders)]
public record GetCustomerAccountQuery : IRequest<Result<CustomerAccountDto>>
{
    public const int MaxOrders = 100;
    public const int RecentPayments = 10;

    public Guid? CustomerId { get; init; }
    public string? Phone { get; init; }
}

public class GetCustomerAccountQueryValidator : AbstractValidator<GetCustomerAccountQuery>
{
    public GetCustomerAccountQueryValidator()
    {
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x).Must(x => x.CustomerId.HasValue || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage(CreditAccount.NeedsCustomerMessage);
    }
}

public class GetCustomerAccountQueryHandler : IRequestHandler<GetCustomerAccountQuery, Result<CustomerAccountDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;

    public GetCustomerAccountQueryHandler(IApplicationDbContext context, IPhoneNormalizationService phones)
    {
        _context = context;
        _phones = phones;
    }

    public async Task<Result<CustomerAccountDto>> Handle(GetCustomerAccountQuery request, CancellationToken cancellationToken)
    {
        var account = await CreditAccount.ResolveAsync(_context, _phones, request.CustomerId, request.Phone, cancellationToken);
        return account.IsFailure
            ? Result.Failure<CustomerAccountDto>(account.Error!)
            : Result.Success(await account.Value.ReadAsync(_context, cancellationToken));
    }
}

/// <summary>
/// A payment to the account of a customer (Fase 26A): one amount, split over the orders they owe from the oldest to the
/// newest. Each part is a payment of its order in the drawer of whoever charges, like one taken in Pedidos, and the orders
/// it settles close as a paid sale does. Never more than what they owe: there is no credit in their favor yet.
/// </summary>
[RequiresPermission(TenantPermission.ChargeOrders)]
public record PayCustomerAccountCommand : IRequest<Result<CustomerAccountDto>>, ITransactionalRequest
{
    public const string DefaultNote = "Abono a la cuenta";

    public Guid? CustomerId { get; init; }
    public string? Phone { get; init; }
    public decimal Amount { get; init; }
    public OrderPaymentMethod Method { get; init; }
    public string? EvidenceUrl { get; init; }
    public string? Reference { get; init; }
    /// <summary>Kept with each payment; «Abono a la cuenta» when not sent.</summary>
    public string? Notes { get; init; }
    /// <summary>What the customer handed over in cash, to keep the change.</summary>
    public decimal? CashReceived { get; init; }
    /// <summary>
    /// What the account owed when the payment was taken, as the screen showed it. A payment sent twice, or after another
    /// one that the screen did not see, finds the account changed and is refused instead of registered again (a payment has
    /// no idempotency key).
    /// </summary>
    public decimal? ExpectedOwed { get; init; }
}

public class PayCustomerAccountCommandValidator : AbstractValidator<PayCustomerAccountCommand>
{
    public PayCustomerAccountCommandValidator()
    {
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x).Must(x => x.CustomerId.HasValue || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage(CreditAccount.NeedsCustomerMessage);
        // Stored with 2 decimals, like every payment (see AddOrderPaymentCommand).
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Method).NotEqual(OrderPaymentMethod.Platform)
            .WithMessage(OrderPaymentMethodRules.PlatformOnlyOnCreationMessage);
        RuleFor(x => x.Method).NotEqual(OrderPaymentMethod.Online)
            .WithMessage(OrderPaymentMethodRules.OnlineOnlyOnCreationMessage);
        RuleFor(x => x.EvidenceUrl).MaximumLength(1000);
        RuleFor(x => x.Reference).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.CashReceived!.Value).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.CashReceived.HasValue).WithName(nameof(PayCustomerAccountCommand.CashReceived));
        RuleFor(x => x.CashReceived).Null().When(x => x.Method != OrderPaymentMethod.Cash)
            .WithMessage(OrderPayment.CashReceivedOnlyForCashMessage);
        RuleFor(x => x.CashReceived!.Value).GreaterThanOrEqualTo(x => x.Amount)
            .When(x => x.CashReceived.HasValue && x.Method == OrderPaymentMethod.Cash)
            .WithName(nameof(PayCustomerAccountCommand.CashReceived))
            .WithMessage(OrderPayment.CashReceivedShortMessage);
        RuleFor(x => x.ExpectedOwed!.Value).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.ExpectedOwed.HasValue).WithName(nameof(PayCustomerAccountCommand.ExpectedOwed));
    }
}

public class PayCustomerAccountCommandHandler : IRequestHandler<PayCustomerAccountCommand, Result<CustomerAccountDto>>
{
    public const string NothingOwedMessage = "No debe nada: no hay a qué abonar.";
    // Orders split in one payment: far more than a customer owes at once.
    private const int MaxOrders = 500;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;
    private readonly IOrderCashService _orderCash;
    private readonly ICustomerCreditLock _creditLock;

    public PayCustomerAccountCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IPhoneNormalizationService phones,
        IDateTimeProvider clock,
        IOrderCashService orderCash,
        ICustomerCreditLock creditLock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _phones = phones;
        _clock = clock;
        _orderCash = orderCash;
        _creditLock = creditLock;
    }

    public static string AboveOwedMessage(decimal owed) =>
        $"Debe {OrderStatusLabels.FormatMoney(owed)}: el abono no puede ser mayor.";

    public static string OwedChangedMessage(decimal owed) =>
        $"La cuenta cambió: ahora debe {OrderStatusLabels.FormatMoney(owed)}. Revisa el estado de cuenta antes de cobrar.";

    public async Task<Result<CustomerAccountDto>> Handle(PayCustomerAccountCommand request, CancellationToken cancellationToken)
    {
        var resolved = await CreditAccount.ResolveAsync(_context, _phones, request.CustomerId, request.Phone, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<CustomerAccountDto>(resolved.Error!);
        var account = resolved.Value;

        // A sale on credit or another payment for the same customer waits: each one splits over what the other left.
        await _creditLock.AcquireAsync(_tenant.TenantId, account.Phone, cancellationToken);

        var variants = MessagingPhone.Variants(account.Phone).ToList();
        // With its pieces: a service still in the workshop does not close when it is paid (CloseIfPaidCounterSale).
        var orders = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.Delivery)
            .Include(o => o.ServicePieces)
            .Where(o => o.CustomerPhone != null && variants.Contains(o.CustomerPhone))
            .Where(Receivables.IsOwed)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.OrderNumber)
            .Take(MaxOrders)
            .ToListAsync(cancellationToken);

        var owed = orders.Sum(o => o.Balance);
        if (owed == 0)
            return Result.Failure<CustomerAccountDto>(NothingOwedMessage);
        if (request.ExpectedOwed is { } expected && expected != owed)
            return Result.Failure<CustomerAccountDto>(OwedChangedMessage(owed));
        if (request.Amount > owed)
            return Result.Failure<CustomerAccountDto>(AboveOwedMessage(owed));

        var settings = await TenantSalesSettings.ReadAsync(_context, _tenant.TenantId, cancellationToken);
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, _tenant.TenantId, cancellationToken);
        var registrar = new OrderPaymentRegistrar(_context, _orderCash, _clock);
        // The change of what was handed over goes with the last part, so the parts add up to what the drawer received.
        var change = request.CashReceived is { } received ? received - request.Amount : 0m;
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? PayCustomerAccountCommand.DefaultNote : request.Notes;
        var remaining = request.Amount;
        var parts = new List<object>();
        foreach (var order in orders)
        {
            if (remaining == 0)
                break;

            var part = Math.Min(remaining, order.Balance);
            remaining -= part;
            var payment = await registrar.RegisterAsync(
                order,
                new OrderPaymentInput(
                    part,
                    request.Method,
                    InventoryLocationId: null,
                    request.EvidenceUrl,
                    request.Reference,
                    notes,
                    Tip: 0,
                    CashReceived: remaining == 0 && request.CashReceived.HasValue ? part + change : null),
                cancellationToken);
            if (payment.IsFailure)
                return Result.Failure<CustomerAccountDto>(payment.Error!);

            // The payment that settles a sale closes it, as in Pedidos (when the business keeps that policy).
            CounterSaleClosing.TryClose(order, settings, capabilities, _currentUser.UserName);
            parts.Add(new { order.OrderNumber, Amount = part });
        }

        // Under the customer record, or the first order paid when the business has none for the phone.
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: account.Customer is null ? nameof(Order) : nameof(Customer),
            entityId: (account.Customer?.Id ?? orders[0].Id).ToString(),
            action: "CustomerAccountPaid",
            performedBy: _currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new { request.Amount, Method = request.Method.ToString(), Orders = parts })));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await account.ReadAsync(_context, cancellationToken));
    }
}

/// <summary>
/// Selling on credit in Vender (Fase 26A): whether what a sale leaves owed fits in what the customer may still owe. Checked
/// inside the customer's credit lock, which the request's transaction holds until it commits, so two sales on credit or a
/// sale and a payment at once count each other.
/// </summary>
public static class CustomerCredit
{
    public const string PhoneIsNotTheCustomersMessage =
        "El teléfono de la venta no es el del cliente: lo fiado se junta por su teléfono.";

    public static string NoCreditMessage(string? name) =>
        $"{(string.IsNullOrWhiteSpace(name) ? "Este cliente" : name)} no tiene fiado: el dueño o un administrador le pone un límite en Clientes.";

    public static string OverLimitMessage(string name, decimal owed, decimal sale, decimal limit) => owed >= limit
        // Nothing of the sale fits (the limit was lowered, or orders were handed over unpaid): charging part does not help.
        ? $"{name} ya debe {OrderStatusLabels.FormatMoney(owed)} y su límite es {OrderStatusLabels.FormatMoney(limit)}: " +
          "cobra la venta completa o pide que suban su límite."
        : $"Con esta venta, {name} debería {OrderStatusLabels.FormatMoney(owed + sale)} y su límite es " +
          $"{OrderStatusLabels.FormatMoney(limit)}: cobra al menos {OrderStatusLabels.FormatMoney(owed + sale - limit)} o pide que suban su límite.";

    /// <param name="customerId">The customer chosen in Vender, when one was.</param>
    /// <param name="orderPhone">The phone the order is stored with.</param>
    /// <param name="leftOwed">What the sale leaves to pay after the payments taken with it.</param>
    /// <returns>Why it cannot be sold on credit; null when it can.</returns>
    public static async Task<string?> CheckSaleAsync(
        IApplicationDbContext context,
        ICustomerCreditLock creditLock,
        IPhoneNormalizationService phones,
        Guid tenantId,
        Guid? customerId,
        string? orderPhone,
        decimal leftOwed,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderPhone))
            return Order.CreditNeedsCustomerMessage;
        if (leftOwed <= 0)
            return Order.CreditNeedsBalanceMessage;

        var phone = MessagingPhone.ToKey(orderPhone);
        var customer = customerId is { } id
            ? await context.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            : await CreditAccount.RecordAsync(context, phone, cancellationToken);
        if (customer is null || !customer.SellsOnCredit)
            return NoCreditMessage(customer?.Name);
        if (!CustomerPhones.TryNormalize(phones, customer.Phone, out var customerPhone) || customerPhone != phone)
            return PhoneIsNotTheCustomersMessage;

        await creditLock.AcquireAsync(tenantId, phone, cancellationToken);
        var owed = await Receivables.OwedAsync(context.Orders, phone, cancellationToken);
        var limit = customer.CreditLimit!.Value;
        return owed + leftOwed > limit ? OverLimitMessage(customer.Name, owed, leftOwed, limit) : null;
    }
}

/// <summary>Whose account: the phone the orders are grouped by, and the customer record with it when there is one.</summary>
public sealed record CreditAccount(string Phone, Customer? Customer)
{
    public const string NeedsCustomerMessage = "Indica el cliente o su teléfono.";

    public static async Task<Result<CreditAccount>> ResolveAsync(
        IApplicationDbContext context,
        IPhoneNormalizationService phones,
        Guid? customerId,
        string? rawPhone,
        CancellationToken cancellationToken)
    {
        Customer? customer = null;
        if (customerId is { } id)
        {
            customer = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (customer is null)
                return Result.Failure<CreditAccount>("El cliente no existe.");
            if (customer.IsPublicGeneral)
                return Result.Failure<CreditAccount>(Customer.PublicGeneralCreditMessage);
            rawPhone = customer.Phone;
            if (string.IsNullOrWhiteSpace(rawPhone))
                return Result.Failure<CreditAccount>(Customer.NoPhoneCreditMessage);
        }

        if (!CustomerPhones.TryNormalize(phones, rawPhone, out var phone))
            return Result.Failure<CreditAccount>(CustomerPhones.InvalidMessage);

        customer ??= await RecordAsync(context, phone, cancellationToken, tracked: false);
        return Result.Success(new CreditAccount(phone, customer));
    }

    /// <summary>The customer record with this phone, however it was typed: the active one first, then one with a limit.</summary>
    public static Task<Customer?> RecordAsync(
        IApplicationDbContext context,
        string phone,
        CancellationToken cancellationToken,
        bool tracked = true)
    {
        var forms = CustomerPhones.StoredForms(phone);
        var customers = tracked ? context.Customers : context.Customers.AsNoTracking();
        return customers
            .Where(c => !c.IsPublicGeneral && c.Phone != null && forms.Contains(c.Phone))
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.CreditLimit != null)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CustomerAccountDto> ReadAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        var lines = Receivables.OfCustomer(context.Orders.AsNoTracking(), Phone);
        var owed = await lines.SumAsync(l => (decimal?)l.Balance, cancellationToken) ?? 0m;
        var count = await lines.CountAsync(cancellationToken);
        var orders = await lines
            .OrderBy(l => l.CreatedAt)
            .ThenBy(l => l.OrderNumber)
            .Take(GetCustomerAccountQuery.MaxOrders)
            .Select(l => new CustomerAccountOrderDto
            {
                OrderId = l.OrderId,
                OrderNumber = l.OrderNumber,
                CreatedAt = l.CreatedAt,
                Status = l.Status,
                SoldOnCredit = l.SoldOnCredit,
                Total = l.Total,
                Paid = l.Paid,
                Balance = l.Balance
            })
            .ToListAsync(cancellationToken);

        var variants = MessagingPhone.Variants(Phone).ToList();
        var payments = await context.OrderPayments.AsNoTracking()
            .Where(p => p.Order.CustomerPhone != null && variants.Contains(p.Order.CustomerPhone)
                && p.Order.Status != OrderStatus.Cancelled)
            .OrderByDescending(p => p.PaidAt)
            .Take(GetCustomerAccountQuery.RecentPayments)
            .Select(p => new CustomerAccountPaymentDto
            {
                OrderNumber = p.Order.OrderNumber,
                PaidAt = p.PaidAt,
                Amount = p.Amount,
                Method = p.Method
            })
            .ToListAsync(cancellationToken);

        var name = Customer?.Name ?? await context.Orders.AsNoTracking()
            .Where(o => o.CustomerPhone != null && variants.Contains(o.CustomerPhone))
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => o.CustomerName)
            .FirstOrDefaultAsync(cancellationToken);
        var limit = Customer?.CreditLimit;
        return new CustomerAccountDto
        {
            Phone = Phone,
            Name = name,
            CustomerId = Customer?.Id,
            CreditLimit = limit,
            SellsOnCredit = Customer?.SellsOnCredit ?? false,
            Owed = owed,
            Available = limit is { } value ? Math.Max(0m, value - owed) : null,
            OverLimit = limit is { } max && owed > max,
            Orders = orders,
            OrdersCount = count,
            RecentPayments = payments
        };
    }
}
