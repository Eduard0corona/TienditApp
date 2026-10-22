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
using FoodParkDigital.Domain.Messaging.Enums;
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
    /// <summary>What of it is past the term of the fiado (Fase 26B); 0 without a term.</summary>
    public decimal Overdue { get; set; }
    /// <summary>The customer's own term (Fase 26C); null when the business's applies.</summary>
    public int? OwnCreditTermDays { get; set; }
    /// <summary>What they have in their favor (Fase 26D) besides what they owe; 0 without any.</summary>
    public decimal CreditBalance { get; set; }
}

public class ReceivablesDto
{
    public PaginatedList<ReceivableDto> Page { get; set; } = null!;
    /// <summary>What all the customers that match owe the business.</summary>
    public decimal TotalOwed { get; set; }
    /// <summary>What of it is past the term of the fiado (Fase 26B); 0 without a term.</summary>
    public decimal TotalOverdue { get; set; }
    /// <summary>How many days the business gives to pay (Fase 26B); null without a term.</summary>
    public int? CreditTermDays { get; set; }
    /// <summary>
    /// Every how many days the business reminds what is overdue on WhatsApp (Fase 26C); null while the reminders are off.
    /// </summary>
    public int? CreditReminderEveryDays { get; set; }
    /// <summary>How many days before the last day to pay the business tells it on WhatsApp (Fase 26E); null while it is off.</summary>
    public int? CreditDueSoonDaysBefore { get; set; }
    /// <summary>What the business holds in favor of all its customers (Fase 26D), whether they owe or not.</summary>
    public decimal TotalCreditBalance { get; set; }
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
    /// <summary>
    /// How many days they have to pay (Fase 26B): their own term (Fase 26C), else the business's; null without either.
    /// </summary>
    public int? CreditTermDays { get; set; }
    /// <summary>Their own term (Fase 26C); null when the business's applies.</summary>
    public int? OwnCreditTermDays { get; set; }
    /// <summary>What of what they owe is past the term (Fase 26B); 0 without a term.</summary>
    public decimal Overdue { get; set; }
    /// <summary>
    /// Every how many days the business reminds what is overdue on WhatsApp (Fase 26C); null while the reminders are off.
    /// </summary>
    public int? CreditReminderEveryDays { get; set; }
    /// <summary>When the last reminder of what they owe went out to them (Fase 26C); null if none did.</summary>
    public DateTimeOffset? LastReminderAt { get; set; }
    /// <summary>When the last notice of what comes due soon went out to them (Fase 26E); null if none did.</summary>
    public DateTimeOffset? LastDueSoonAt { get; set; }
    /// <summary>The orders owed, the oldest first (at most <see cref="GetCustomerAccountQuery.MaxOrders"/>).</summary>
    public List<CustomerAccountOrderDto> Orders { get; set; } = [];
    /// <summary>How many orders they owe, also beyond the ones listed.</summary>
    public int OrdersCount { get; set; }
    /// <summary>Their latest payments, on any of their orders.</summary>
    public List<CustomerAccountPaymentDto> RecentPayments { get; set; } = [];
    /// <summary>What the business forgave them lately (Fase 26B), on any of their orders.</summary>
    public List<CustomerAccountWriteOffDto> RecentWriteOffs { get; set; } = [];
    /// <summary>What they have in their favor (Fase 26D): what they left and refunds to it, minus what they used or got back.</summary>
    public decimal CreditBalance { get; set; }
    /// <summary>The latest movements of their balance in their favor, the newest first.</summary>
    public List<CustomerBalanceEntryDto> RecentBalanceEntries { get; set; } = [];
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
    /// <summary>What the business forgave of it (Fase 26B).</summary>
    public decimal WrittenOff { get; set; }
    public decimal Balance { get; set; }
    /// <summary>The last day to pay it by the customer's term, in the business's calendar (Fase 26B); null without a term.</summary>
    public DateOnly? DueOn { get; set; }
    /// <summary>Its last day to pay has passed.</summary>
    public bool IsOverdue { get; set; }
}

/// <summary>What the business forgave a customer of an order (Fase 26B, ADR 0034).</summary>
public class CustomerAccountWriteOffDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public DateTimeOffset WrittenOffAt { get; set; }
    public decimal Amount { get; set; }
    public OrderWriteOffReason Reason { get; set; }
    public string? Notes { get; set; }
    /// <summary>When it was undone (owed again); null while it stands.</summary>
    public DateTimeOffset? ReversedAt { get; set; }
}

/// <summary>The term of the fiado of the business (Fase 26B).</summary>
public class CustomerCreditTermDto
{
    public int? CreditTermDays { get; set; }
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
    /// <summary>Only the customers with something past the term of the fiado (Fase 26B); nobody without a term.</summary>
    public bool OverdueOnly { get; init; }
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
    private readonly IDateTimeProvider _clock;

    public GetReceivablesQueryHandler(IApplicationDbContext context, IPhoneNormalizationService phones, IDateTimeProvider clock)
    {
        _context = context;
        _phones = phones;
        _clock = clock;
    }

    public async Task<ReceivablesDto> Handle(GetReceivablesQuery request, CancellationToken cancellationToken)
    {
        var term = await CreditTerm.ReadAsync(_context, cancellationToken);
        var lines = Receivables.Lines(_context.Orders.AsNoTracking());
        if (!string.IsNullOrWhiteSpace(request.Search))
            lines = await MatchingAsync(lines, request.Search.Trim(), cancellationToken);
        // Each customer's orders by their own term, else the business's (Fase 26C).
        var overdue = await CreditTerm.OverdueAsync(_context, lines, term, _clock.UtcNow, cancellationToken);
        if (request.OverdueOnly)
        {
            // Every order of a customer with something overdue stays: what they owe is all of it.
            var overduePhones = overdue.Select(l => l.Phone);
            lines = lines.Where(l => overduePhones.Contains(l.Phone));
        }

        var overdueByPhone = overdue
            .GroupBy(l => l.Phone)
            .Select(g => new { Phone = g.Key, Overdue = g.Sum(l => l.Balance) });
        var customers =
            from customer in lines
                .GroupBy(l => l.Phone)
                .Select(g => new
                {
                    Phone = g.Key,
                    Owed = g.Sum(l => l.Balance),
                    Orders = g.Count(),
                    Since = g.Min(l => l.CreatedAt)
                })
            join late in overdueByPhone on customer.Phone equals late.Phone into lates
            from late in lates.DefaultIfEmpty()
            select new
            {
                customer.Phone,
                customer.Owed,
                Overdue = (decimal?)late.Overdue ?? 0m,
                customer.Orders,
                customer.Since
            };
        var ordered = request.Sort == ReceivablesSort.Oldest
            ? customers.OrderBy(c => c.Since).ThenBy(c => c.Phone)
            : customers.OrderByDescending(c => c.Owed).ThenBy(c => c.Phone);

        var (pageNumber, pageSize) = Paging.Normalize(request.PageNumber, request.PageSize);
        var count = await customers.CountAsync(cancellationToken);
        var totalOwed = await lines.SumAsync(l => (decimal?)l.Balance, cancellationToken) ?? 0m;
        var totalOverdue = await overdue.SumAsync(l => (decimal?)l.Balance, cancellationToken) ?? 0m;
        var page = await ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var reminderEveryDays = await CreditReminders.EveryDaysAsync(_context, cancellationToken);

        // The page's names, limits and terms: the customer record with each phone, else the name on the latest order owed.
        var phones = page.Select(c => c.Phone).ToList();
        var records = await RecordsAsync(phones, cancellationToken);
        var ownTerms = await CreditTerm.OwnTerms(_context.Customers.AsNoTracking())
            .Where(t => phones.Contains(t.Phone))
            .ToDictionaryAsync(t => t.Phone, t => t.Days, cancellationToken);
        var latestNames = (await lines
                .Where(l => phones.Contains(l.Phone))
                .Select(l => new { l.Phone, l.CustomerName, l.CreatedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.Phone)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).First().CustomerName);
        var inFavor = await CustomerBalances.OfPhonesAsync(_context, phones, cancellationToken);

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
                OverLimit = limit is { } value && c.Owed > value,
                Overdue = c.Overdue,
                OwnCreditTermDays = ownTerms.TryGetValue(c.Phone, out var days) ? days : null,
                CreditBalance = inFavor.GetValueOrDefault(c.Phone)
            };
        }).ToList();

        return new ReceivablesDto
        {
            Page = new PaginatedList<ReceivableDto>(items, count, pageNumber, pageSize),
            TotalOwed = totalOwed,
            TotalOverdue = totalOverdue,
            CreditTermDays = term.Days,
            CreditReminderEveryDays = reminderEveryDays,
            CreditDueSoonDaysBefore = await CreditReminders.DueSoonDaysBeforeAsync(_context, cancellationToken),
            TotalCreditBalance = await CustomerBalances.TotalAsync(_context, cancellationToken)
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
    public const int RecentWriteOffs = 10;

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
    private readonly IDateTimeProvider _clock;

    public GetCustomerAccountQueryHandler(IApplicationDbContext context, IPhoneNormalizationService phones, IDateTimeProvider clock)
    {
        _context = context;
        _phones = phones;
        _clock = clock;
    }

    public async Task<Result<CustomerAccountDto>> Handle(GetCustomerAccountQuery request, CancellationToken cancellationToken)
    {
        var account = await CreditAccount.ResolveAsync(_context, _phones, request.CustomerId, request.Phone, cancellationToken);
        return account.IsFailure
            ? Result.Failure<CustomerAccountDto>(account.Error!)
            : Result.Success(await account.Value.ReadAsync(_context, _clock.UtcNow, cancellationToken));
    }
}

/// <summary>
/// A payment to the account of a customer (Fase 26A): one amount, split over the orders they owe from the oldest to the
/// newest. Each part is a payment of its order in the drawer of whoever charges, like one taken in Pedidos, and the orders
/// it settles close as a paid sale does. Never more than what they owe, unless the rest stays in their favor (Fase 26D):
/// then it enters the same drawer as what a customer leaves. With «Saldo a favor» (Fase 26D) it uses what they have in
/// their favor instead, outside every drawer.
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
    /// <summary>
    /// What is paid beyond what they owe stays in their favor (Fase 26D) instead of being refused. Only with a method charged
    /// in person; with nothing owed, all of it stays in their favor.
    /// </summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool RestInFavor { get; init; }
    /// <summary>
    /// Their balance in their favor as the screen showed it (Fase 26D), required to pay with it or to leave the rest. Like
    /// <see cref="ExpectedOwed"/>, a payment that finds it changed is refused: one sent twice uses or leaves nothing twice.
    /// </summary>
    public decimal? ExpectedBalance { get; init; }
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
        RuleFor(x => x.ExpectedBalance!.Value).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.ExpectedBalance.HasValue).WithName(nameof(PayCustomerAccountCommand.ExpectedBalance));
        // Nothing else stops a retry from using or leaving the balance twice: with nothing owed there is no ExpectedOwed.
        RuleFor(x => x.ExpectedBalance).NotNull()
            .When(x => x.RestInFavor || x.Method == OrderPaymentMethod.CustomerBalance)
            .WithMessage(PayCustomerAccountCommandHandler.ExpectedBalanceMessage);
        RuleFor(x => x.RestInFavor).Equal(false).When(x => !OrderPaymentMethodRules.EntersADrawer(x.Method))
            .WithMessage(PayCustomerAccountCommandHandler.RestInFavorInPersonMessage);
        RuleFor(x => x.EvidenceUrl).Null().When(x => x.Method == OrderPaymentMethod.CustomerBalance)
            .WithMessage(PayCustomerAccountCommandHandler.BalanceWithoutEvidenceMessage);
    }
}

public class PayCustomerAccountCommandHandler : IRequestHandler<PayCustomerAccountCommand, Result<CustomerAccountDto>>
{
    public const string NothingOwedMessage = "No debe nada: no hay a qué abonar.";
    public const string RestInFavorInPersonMessage =
        "Lo que sobra queda a favor solo si se paga en persona: efectivo, tarjeta, transferencia, billetera u otro.";
    public const string BalanceWithoutEvidenceMessage = "Lo que se paga con saldo a favor no lleva comprobante.";
    public const string ExpectedBalanceMessage =
        "Indica el saldo a favor que se veía (expectedBalance): así un abono repetido no lo usa ni lo deja dos veces.";
    public const string RestNote = "Lo que sobró de un abono";
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

    public static string AboveSplitMessage(decimal splittable) =>
        $"Abona hasta {OrderStatusLabels.FormatMoney(splittable)} a la vez: la cuenta tiene más de {MaxOrders} pedidos.";

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

        // All of it, as the account shows it, though one payment splits over the oldest orders only.
        var owed = await Receivables.OwedAsync(_context.Orders, account.Phone, cancellationToken);
        if (owed == 0 && !request.RestInFavor)
            return Result.Failure<CustomerAccountDto>(NothingOwedMessage);
        if (request.ExpectedOwed is { } expected && expected != owed)
            return Result.Failure<CustomerAccountDto>(OwedChangedMessage(owed));
        // What goes to the orders, and what stays in their favor when that was asked (Fase 26D).
        var toOrders = Math.Min(request.Amount, owed);
        var rest = request.Amount - toOrders;
        if (rest > 0 && !request.RestInFavor)
            return Result.Failure<CustomerAccountDto>(AboveOwedMessage(owed));
        if (toOrders > orders.Sum(o => o.Balance))
            return Result.Failure<CustomerAccountDto>(AboveSplitMessage(orders.Sum(o => o.Balance)));

        var withBalance = request.Method == OrderPaymentMethod.CustomerBalance;
        if (withBalance || request.ExpectedBalance.HasValue)
        {
            var balance = await CustomerBalances.OfAsync(_context, account.Phone, cancellationToken);
            if (request.ExpectedBalance is { } expectedBalance && expectedBalance != balance)
                return Result.Failure<CustomerAccountDto>(CustomerBalances.ChangedMessage(balance));
            if (withBalance && CustomerBalanceEntry.CoverProblem(balance, request.Amount, OrderStatusLabels.FormatMoney) is { } notCovered)
                return Result.Failure<CustomerAccountDto>(notCovered);
        }

        var settings = await TenantSalesSettings.ReadAsync(_context, _tenant.TenantId, cancellationToken);
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, _tenant.TenantId, cancellationToken);
        var registrar = new OrderPaymentRegistrar(_context, _orderCash, _clock);
        var by = _currentUser.UserName ?? "system";
        // The change of what was handed over goes with the last part, so the parts add up to what the drawer received.
        var change = request.CashReceived is { } received ? received - request.Amount : 0m;
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? PayCustomerAccountCommand.DefaultNote : request.Notes;
        var remaining = toOrders;
        var parts = new List<object>();
        foreach (var order in orders)
        {
            if (remaining == 0)
                break;

            var part = Math.Min(remaining, order.Balance);
            remaining -= part;
            var payment = withBalance
                // Paid with what they left before: the sale now, outside every drawer, and the movement of their balance.
                ? await registrar.ApplyCustomerBalanceAsync(order, part, notes, account.Phone, account.Customer?.Id, by, cancellationToken)
                : await registrar.RegisterAsync(
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

        // The rest stays in their favor (Fase 26D): it enters the same drawer as what a customer leaves.
        CustomerBalanceEntry? inFavor = null;
        if (rest > 0)
        {
            inFavor = await CustomerBalanceDeposits.LeaveAsync(
                _context, _orderCash, _tenant.TenantId, account, rest, request.Method, request.Reference,
                string.IsNullOrWhiteSpace(request.Notes) ? RestNote : request.Notes, _clock.UtcNow, by, cancellationToken);
            if (inFavor is null)
                return Result.Failure<CustomerAccountDto>(CustomerBalances.NoOpenDrawerMessage);
        }

        // Under the customer record, else the first order paid, else what stayed in their favor.
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: account.Customer is not null ? nameof(Customer) : orders.Count > 0 ? nameof(Order) : nameof(CustomerBalanceEntry),
            entityId: (account.Customer?.Id ?? (orders.Count > 0 ? orders[0].Id : inFavor!.Id)).ToString(),
            action: "CustomerAccountPaid",
            performedBy: by,
            metadata: JsonSerializer.Serialize(new { request.Amount, Method = request.Method.ToString(), Orders = parts, InFavor = rest })));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await account.ReadAsync(_context, _clock.UtcNow, cancellationToken));
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

    public async Task<CustomerAccountDto> ReadAsync(IApplicationDbContext context, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        // Their own term (Fase 26C), else the business's.
        var ownDays = await CreditTerm.OwnDaysAsync(context, Phone, cancellationToken);
        var term = (await CreditTerm.ReadAsync(context, cancellationToken)).For(ownDays);
        var overdueBefore = term.OverdueBefore(utcNow);
        var lines = Receivables.OfCustomer(context.Orders.AsNoTracking(), Phone);
        var owed = await lines.SumAsync(l => (decimal?)l.Balance, cancellationToken) ?? 0m;
        var overdue = await lines.Where(l => l.OwedSince < overdueBefore).SumAsync(l => (decimal?)l.Balance, cancellationToken) ?? 0m;
        var count = await lines.CountAsync(cancellationToken);
        var owedOrders = await lines
            .OrderBy(l => l.CreatedAt)
            .ThenBy(l => l.OrderNumber)
            .Take(GetCustomerAccountQuery.MaxOrders)
            .Select(l => new
            {
                Order = new CustomerAccountOrderDto
                {
                    OrderId = l.OrderId,
                    OrderNumber = l.OrderNumber,
                    CreatedAt = l.CreatedAt,
                    Status = l.Status,
                    SoldOnCredit = l.SoldOnCredit,
                    Total = l.Total,
                    Paid = l.Paid,
                    WrittenOff = l.WrittenOff,
                    Balance = l.Balance,
                    IsOverdue = l.OwedSince < overdueBefore
                },
                l.OwedSince
            })
            .ToListAsync(cancellationToken);
        var orders = owedOrders.Select(o =>
        {
            o.Order.DueOn = term.DueOn(o.OwedSince);
            return o.Order;
        }).ToList();

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
        var writeOffs = await context.OrderWriteOffs.AsNoTracking()
            .Join(
                context.Orders.Where(o => o.CustomerPhone != null && variants.Contains(o.CustomerPhone) && o.Status != OrderStatus.Cancelled),
                writeOff => writeOff.OrderId,
                order => order.Id,
                (writeOff, order) => new CustomerAccountWriteOffDto
                {
                    Id = writeOff.Id,
                    OrderNumber = order.OrderNumber,
                    WrittenOffAt = writeOff.WrittenOffAt,
                    Amount = writeOff.Amount,
                    Reason = writeOff.Reason,
                    Notes = writeOff.Notes,
                    ReversedAt = writeOff.ReversedAt
                })
            .OrderByDescending(w => w.WrittenOffAt)
            .Take(GetCustomerAccountQuery.RecentWriteOffs)
            .ToListAsync(cancellationToken);

        var lastReminderAt = await context.OutgoingMessages.AsNoTracking()
            .Where(m => m.Purpose == MessagePurpose.CreditReminder
                && m.Status == OutgoingMessageStatus.Sent
                && variants.Contains(m.Phone))
            .MaxAsync(m => m.SentAt, cancellationToken);
        var lastDueSoonAt = await context.OutgoingMessages.AsNoTracking()
            .Where(m => m.Purpose == MessagePurpose.CreditDueSoon
                && m.Status == OutgoingMessageStatus.Sent
                && variants.Contains(m.Phone))
            .MaxAsync(m => m.SentAt, cancellationToken);

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
            CreditTermDays = term.Days,
            OwnCreditTermDays = ownDays,
            Overdue = overdue,
            CreditReminderEveryDays = await CreditReminders.EveryDaysAsync(context, cancellationToken),
            LastReminderAt = lastReminderAt,
            LastDueSoonAt = lastDueSoonAt,
            Orders = orders,
            OrdersCount = count,
            RecentPayments = payments,
            RecentWriteOffs = writeOffs,
            CreditBalance = await CustomerBalances.OfAsync(context, Phone, cancellationToken),
            RecentBalanceEntries = await CustomerBalances.RecentAsync(context, Phone, cancellationToken)
        };
    }
}
