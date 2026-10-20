using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers;

/// <summary>A movement of the balance in a customer's favor, as their account shows it (Fase 26D, ADR 0036).</summary>
public class CustomerBalanceEntryDto
{
    public Guid Id { get; set; }
    public CustomerBalanceEntryKind Kind { get; set; }
    /// <summary>Positive when it added to the balance (left, refunded), negative when it took from it (paid, given back).</summary>
    public decimal Amount { get; set; }
    /// <summary>How the money entered or left a drawer, for what they left or got back; null otherwise.</summary>
    public OrderPaymentMethod? Method { get; set; }
    /// <summary>The order paid with it or refunded to it; null for what they left or got back.</summary>
    public string? OrderNumber { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// The balance in a customer's favor (Fase 26D, ADR 0036): the sum of the movements of their account, the phone their
/// orders are grouped by. Read inside the customer's credit lock by whatever takes from it, so it never goes below zero.
/// </summary>
public static class CustomerBalances
{
    public const int RecentEntries = 10;
    public const string NoOpenDrawerMessage = "Abre tu caja: lo que un cliente deja a su favor, o se le devuelve, pasa por una caja.";
    public const string NothingInFavorMessage = "No tiene saldo a favor.";

    public static string ChangedMessage(decimal balance) =>
        $"El saldo a favor cambió: ahora tiene {OrderStatusLabels.FormatMoney(balance)}. Revisa el estado de cuenta antes de seguir.";

    /// <summary>The balance of an account, by the phone key.</summary>
    public static async Task<decimal> OfAsync(IApplicationDbContext context, string phoneKey, CancellationToken cancellationToken)
        => await context.CustomerBalanceEntries.AsNoTracking()
            .Where(e => e.Phone == phoneKey)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

    /// <summary>The balance of each of these accounts that has one.</summary>
    public static Task<Dictionary<string, decimal>> OfPhonesAsync(
        IApplicationDbContext context, IReadOnlyCollection<string> phoneKeys, CancellationToken cancellationToken)
        => context.CustomerBalanceEntries.AsNoTracking()
            .Where(e => phoneKeys.Contains(e.Phone))
            .GroupBy(e => e.Phone)
            .Select(g => new { Phone = g.Key, Balance = g.Sum(e => e.Amount) })
            .Where(b => b.Balance != 0)
            .ToDictionaryAsync(b => b.Phone, b => b.Balance, cancellationToken);

    /// <summary>What the business holds in favor of all its customers.</summary>
    public static async Task<decimal> TotalAsync(IApplicationDbContext context, CancellationToken cancellationToken)
        => await context.CustomerBalanceEntries.AsNoTracking().SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

    /// <summary>The latest movements of an account, the newest first.</summary>
    public static Task<List<CustomerBalanceEntryDto>> RecentAsync(
        IApplicationDbContext context, string phoneKey, CancellationToken cancellationToken)
        => (
                from entry in context.CustomerBalanceEntries.AsNoTracking()
                where entry.Phone == phoneKey
                join order in context.Orders.AsNoTracking() on entry.OrderId equals order.Id into orders
                from order in orders.DefaultIfEmpty()
                orderby entry.OccurredAt descending, entry.CreatedAt descending
                select new CustomerBalanceEntryDto
                {
                    Id = entry.Id,
                    Kind = entry.Kind,
                    Amount = entry.Amount,
                    Method = entry.Method,
                    OrderNumber = order == null ? null : order.OrderNumber,
                    Reference = entry.Reference,
                    Notes = entry.Notes,
                    OccurredAt = entry.OccurredAt
                })
            .Take(RecentEntries)
            .ToListAsync(cancellationToken);

    /// <summary>What a movement of a drawer says when the cashier wrote nothing: whose balance it is.</summary>
    internal static string DrawerNote(string what, CreditAccount account) =>
        account.Customer?.Name is { Length: > 0 } name ? $"{what} de {name}" : what;
}

/// <summary>
/// Dejar a favor (Fase 26D, ADR 0036): what a customer leaves before they buy, an advance, adds to their balance. The money
/// enters the drawer of whoever charges as a movement of its own (<see cref="CashTransactionType.CustomerDeposit"/>): it is
/// not a sale, nor a payment of an order, nor goes in a CFDI until they use it. Inside the customer's credit lock.
/// </summary>
[RequiresPermission(TenantPermission.ChargeOrders)]
public record AddCustomerDepositCommand : IRequest<Result<CustomerAccountDto>>, ITransactionalRequest
{
    public Guid? CustomerId { get; init; }
    public string? Phone { get; init; }
    public decimal Amount { get; init; }
    /// <summary>How they handed it over: cash, card, transfer, wallet or other.</summary>
    public OrderPaymentMethod Method { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
    /// <summary>
    /// Their balance as the screen showed it. Sent twice, or after a movement the screen did not see, it finds the balance
    /// changed and is refused instead of leaving it twice.
    /// </summary>
    public decimal? ExpectedBalance { get; init; }
}

public class AddCustomerDepositCommandValidator : AbstractValidator<AddCustomerDepositCommand>
{
    public AddCustomerDepositCommandValidator()
    {
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x).Must(x => x.CustomerId.HasValue || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage(CreditAccount.NeedsCustomerMessage);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(CustomerBalanceEntry.MaxAmount)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage(CustomerBalanceEntry.AmountMessage);
        RuleFor(x => x.Method).Must(m => Enum.IsDefined(m) && OrderPaymentMethodRules.EntersADrawer(m))
            .WithMessage(CustomerBalanceEntry.InPersonMethodMessage);
        RuleFor(x => x.Reference).MaximumLength(CustomerBalanceEntry.MaxReferenceLength);
        RuleFor(x => x.Notes).MaximumLength(CustomerBalanceEntry.MaxNotesLength);
        RuleFor(x => x.ExpectedBalance!.Value).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.ExpectedBalance.HasValue).WithName(nameof(AddCustomerDepositCommand.ExpectedBalance));
    }
}

public class AddCustomerDepositCommandHandler : IRequestHandler<AddCustomerDepositCommand, Result<CustomerAccountDto>>
{
    public const string DrawerNote = "Saldo a favor";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;
    private readonly IOrderCashService _orderCash;
    private readonly ICustomerCreditLock _creditLock;

    public AddCustomerDepositCommandHandler(
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

    public async Task<Result<CustomerAccountDto>> Handle(AddCustomerDepositCommand request, CancellationToken cancellationToken)
    {
        var resolved = await CreditAccount.ResolveAsync(_context, _phones, request.CustomerId, request.Phone, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<CustomerAccountDto>(resolved.Error!);
        var account = resolved.Value;

        await _creditLock.AcquireAsync(_tenant.TenantId, account.Phone, cancellationToken);
        var balance = await CustomerBalances.OfAsync(_context, account.Phone, cancellationToken);
        if (request.ExpectedBalance is { } expected && expected != balance)
            return Result.Failure<CustomerAccountDto>(CustomerBalances.ChangedMessage(balance));

        var entry = await CustomerBalanceDeposits.LeaveAsync(
            _context, _orderCash, _tenant.TenantId, account, request.Amount, request.Method, request.Reference, request.Notes,
            _clock.UtcNow, _currentUser.UserName ?? "system", cancellationToken);
        if (entry is null)
            return Result.Failure<CustomerAccountDto>(CustomerBalances.NoOpenDrawerMessage);

        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: account.Customer is null ? nameof(CustomerBalanceEntry) : nameof(Customer),
            entityId: (account.Customer?.Id ?? entry.Id).ToString(),
            action: "CustomerBalanceDeposited",
            performedBy: _currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new { request.Amount, Method = request.Method.ToString(), Balance = balance + request.Amount })));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await account.ReadAsync(_context, _clock.UtcNow, cancellationToken));
    }
}

/// <summary>What a customer leaves in their favor, in the drawer of whoever charges: the deposit and the rest of a payment.</summary>
internal static class CustomerBalanceDeposits
{
    /// <returns>The movement of their balance; null when no open drawer took the money (nothing was recorded).</returns>
    public static async Task<CustomerBalanceEntry?> LeaveAsync(
        IApplicationDbContext context,
        IOrderCashService orderCash,
        Guid tenantId,
        CreditAccount account,
        decimal amount,
        OrderPaymentMethod method,
        string? reference,
        string? notes,
        DateTimeOffset now,
        string recordedBy,
        CancellationToken cancellationToken)
    {
        var drawer = await orderCash.RegisterCustomerBalanceAsync(
            tenantId,
            CashTransactionType.CustomerDeposit,
            amount,
            method,
            now,
            reference?.Trim(),
            string.IsNullOrWhiteSpace(notes) ? CustomerBalances.DrawerNote(AddCustomerDepositCommandHandler.DrawerNote, account) : notes.Trim(),
            cancellationToken);
        if (drawer is not { } drawerId)
            return null;

        var entry = CustomerBalanceEntry.Deposit(
            tenantId, account.Phone, account.Customer?.Id, amount, method, drawerId, reference, notes, now, recordedBy);
        context.CustomerBalanceEntries.Add(entry);
        return entry;
    }
}

/// <summary>
/// Devolver el saldo a favor (Fase 26D, ADR 0036): up to what the customer has, out of the drawer of whoever gives it back
/// (<see cref="CashTransactionType.CustomerDepositReturn"/>). Money leaves the business, so it also needs who may refund.
/// Inside the customer's credit lock, so it never takes their balance below zero.
/// </summary>
[RequiresPermission(TenantPermission.ChargeOrders)]
public record ReturnCustomerBalanceCommand : IRequest<Result<CustomerAccountDto>>, ITransactionalRequest, IRequiresContentPermissions
{
    public Guid? CustomerId { get; init; }
    public string? Phone { get; init; }
    public decimal Amount { get; init; }
    /// <summary>How it is given back: cash, card, transfer, wallet or other.</summary>
    public OrderPaymentMethod Method { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
    /// <summary>Their balance as the screen showed it: sent twice, it finds the balance changed and gives nothing back again.</summary>
    public decimal ExpectedBalance { get; init; }

    public IEnumerable<TenantPermission> RequiredPermissions() => [TenantPermission.ProcessRefunds];
}

public class ReturnCustomerBalanceCommandValidator : AbstractValidator<ReturnCustomerBalanceCommand>
{
    public ReturnCustomerBalanceCommandValidator()
    {
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x).Must(x => x.CustomerId.HasValue || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage(CreditAccount.NeedsCustomerMessage);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(CustomerBalanceEntry.MaxAmount)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage(CustomerBalanceEntry.AmountMessage);
        RuleFor(x => x.Method).Must(m => Enum.IsDefined(m) && OrderPaymentMethodRules.EntersADrawer(m))
            .WithMessage(CustomerBalanceEntry.InPersonMethodMessage);
        RuleFor(x => x.Reference).MaximumLength(CustomerBalanceEntry.MaxReferenceLength);
        RuleFor(x => x.Notes).MaximumLength(CustomerBalanceEntry.MaxNotesLength);
        RuleFor(x => x.ExpectedBalance).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
    }
}

public class ReturnCustomerBalanceCommandHandler : IRequestHandler<ReturnCustomerBalanceCommand, Result<CustomerAccountDto>>
{
    public const string DrawerNote = "Saldo a favor devuelto";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;
    private readonly IOrderCashService _orderCash;
    private readonly ICustomerCreditLock _creditLock;

    public ReturnCustomerBalanceCommandHandler(
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

    public async Task<Result<CustomerAccountDto>> Handle(ReturnCustomerBalanceCommand request, CancellationToken cancellationToken)
    {
        var resolved = await CreditAccount.ResolveAsync(_context, _phones, request.CustomerId, request.Phone, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<CustomerAccountDto>(resolved.Error!);
        var account = resolved.Value;

        await _creditLock.AcquireAsync(_tenant.TenantId, account.Phone, cancellationToken);
        var balance = await CustomerBalances.OfAsync(_context, account.Phone, cancellationToken);
        if (balance <= 0)
            return Result.Failure<CustomerAccountDto>(CustomerBalances.NothingInFavorMessage);
        if (request.ExpectedBalance != balance)
            return Result.Failure<CustomerAccountDto>(CustomerBalances.ChangedMessage(balance));
        if (request.Amount > balance)
            return Result.Failure<CustomerAccountDto>(string.Format(CustomerBalanceEntry.NotEnoughMessage, OrderStatusLabels.FormatMoney(balance)));

        var now = _clock.UtcNow;
        var by = _currentUser.UserName ?? "system";
        var drawer = await _orderCash.RegisterCustomerBalanceAsync(
            _tenant.TenantId,
            CashTransactionType.CustomerDepositReturn,
            request.Amount,
            request.Method,
            now,
            request.Reference?.Trim(),
            string.IsNullOrWhiteSpace(request.Notes) ? CustomerBalances.DrawerNote(DrawerNote, account) : request.Notes.Trim(),
            cancellationToken);
        if (drawer is not { } drawerId)
            return Result.Failure<CustomerAccountDto>(CustomerBalances.NoOpenDrawerMessage);

        var entry = CustomerBalanceEntry.Returned(
            _tenant.TenantId, account.Phone, account.Customer?.Id, request.Amount, request.Method, drawerId,
            request.Reference, request.Notes, now, by);
        _context.CustomerBalanceEntries.Add(entry);
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: account.Customer is null ? nameof(CustomerBalanceEntry) : nameof(Customer),
            entityId: (account.Customer?.Id ?? entry.Id).ToString(),
            action: "CustomerBalanceReturned",
            performedBy: by,
            metadata: JsonSerializer.Serialize(new { request.Amount, Method = request.Method.ToString(), Balance = balance - request.Amount })));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await account.ReadAsync(_context, _clock.UtcNow, cancellationToken));
    }
}
