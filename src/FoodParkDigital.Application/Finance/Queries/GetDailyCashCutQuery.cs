using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Queries;

[RequiresPermission(TenantPermission.ManageCash)]
public record GetDailyCashCutQuery : IRequest<DailyCashCutDto>
{
    public DateOnly? Date { get; init; }
    public Guid? InventoryLocationId { get; init; }
    /// <summary>Payments of sales made at the branch and movements of its drawers.</summary>
    public Guid? BranchId { get; init; }
    /// <summary>The user (email) who took the payments, or the name of the one member of the business called so.</summary>
    public string? UserName { get; init; }
}

public class GetDailyCashCutQueryHandler : IRequestHandler<GetDailyCashCutQuery, DailyCashCutDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetDailyCashCutQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<DailyCashCutDto> Handle(GetDailyCashCutQuery request, CancellationToken cancellationToken)
    {
        var tenantTimeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantService.TenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

        var tenantZone = TenantDateTimeResolver.Resolve(tenantTimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(_dateTimeProvider.UtcNow, tenantZone);
        var localDate = request.Date ?? DateOnly.FromDateTime(localNow.DateTime);

        var startUtc = TenantDateTimeResolver.LocalDateBoundaryToUtc(
            localDate.ToDateTime(TimeOnly.MinValue),
            startOfDay: true,
            tenantZone);
        var endUtc = TenantDateTimeResolver.LocalDateBoundaryToUtc(
            localDate.ToDateTime(TimeOnly.MinValue),
            startOfDay: false,
            tenantZone);

        var userNameFilter = request.UserName?.Trim();
        // The cut shows names: the name of a member finds what they took, as their user does.
        var userCalled = string.IsNullOrWhiteSpace(userNameFilter)
            ? null
            : await CashPeople.UserCalledAsync(_context, userNameFilter, cancellationToken);

        var orderPayments = _context.OrderPayments
            .AsNoTracking()
            .Where(x => x.PaidAt >= startUtc && x.PaidAt <= endUtc)
            .AsQueryable();

        if (request.InventoryLocationId.HasValue)
            orderPayments = orderPayments.Where(x => x.InventoryLocationId == request.InventoryLocationId.Value);

        if (request.BranchId is { } branchId)
            orderPayments = orderPayments.Where(x => x.Order.BranchId == branchId);

        if (!string.IsNullOrWhiteSpace(userNameFilter))
            orderPayments = orderPayments.Where(x => x.CreatedBy == userNameFilter || (userCalled != null && x.CreatedBy == userCalled));

        var paymentRows = await orderPayments
            .Select(x => new
            {
                x.Amount,
                x.Tip,
                x.Method,
                x.CreatedBy,
                x.InventoryLocationId,
                x.Order.BranchId
            })
            .ToListAsync(cancellationToken);

        var branchNames = await _context.Branches
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var locationNames = await _context.InventoryLocations
            .AsNoTracking()
            .Where(x => !request.InventoryLocationId.HasValue || x.Id == request.InventoryLocationId.Value)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var people = await CashPeople.ResolveAsync(_context, paymentRows.Select(x => x.CreatedBy), [], cancellationToken);
        // What a delivery platform charged in its app went into no cashier's drawer: whoever captured the order did not
        // receive that money, so it is shown on its own row and not in theirs.
        var byUser = paymentRows
            .Where(x => OrderPaymentMethodRules.EntersADrawer(x.Method))
            .GroupBy(x => string.IsNullOrWhiteSpace(x.CreatedBy) ? "Sistema" : x.CreatedBy!)
            .Select(x => new DailyCashCutByUserDto
            {
                UserName = x.Key,
                DisplayName = people.NameOf(x.Key),
                PaymentsCount = x.Count(),
                TotalAmount = x.Sum(y => y.Amount)
            })
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        var byLocation = paymentRows
            .GroupBy(x => x.InventoryLocationId)
            .Select(x => new DailyCashCutByLocationDto
            {
                InventoryLocationId = x.Key,
                LocationName = x.Key.HasValue && locationNames.TryGetValue(x.Key.Value, out var name)
                    ? name
                    : "Sin sucursal",
                PaymentsCount = x.Count(),
                TotalAmount = x.Sum(y => y.Amount)
            })
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        var byBranch = paymentRows
            .GroupBy(x => x.BranchId)
            .Select(x => new DailyCashCutByBranchDto
            {
                BranchId = x.Key,
                BranchName = x.Key.HasValue && branchNames.TryGetValue(x.Key.Value, out var name)
                    ? name
                    : "Sin sucursal",
                PaymentsCount = x.Count(),
                TotalAmount = x.Sum(y => y.Amount)
            })
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        var cashTotal = paymentRows
            .Where(x => x.Method == OrderPaymentMethod.Cash)
            .Sum(x => x.Amount);
        var cardTotal = paymentRows
            .Where(x => x.Method == OrderPaymentMethod.Card)
            .Sum(x => x.Amount);
        var transferTotal = paymentRows
            .Where(x => x.Method == OrderPaymentMethod.Transfer)
            .Sum(x => x.Amount);
        var walletTotal = paymentRows
            .Where(x => x.Method == OrderPaymentMethod.DigitalWallet)
            .Sum(x => x.Amount);
        // Every method that has no bucket of its own, so the buckets always add up to what was charged.
        var otherTotal = paymentRows
            .Where(x => x.Method is not (OrderPaymentMethod.Cash or OrderPaymentMethod.Card or OrderPaymentMethod.Transfer
                or OrderPaymentMethod.DigitalWallet or OrderPaymentMethod.Platform or OrderPaymentMethod.Online
                or OrderPaymentMethod.CustomerBalance))
            .Sum(x => x.Amount);
        var platformRows = paymentRows
            .Where(x => x.Method == OrderPaymentMethod.Platform)
            .ToList();
        var platformTotal = platformRows.Sum(x => x.Amount);
        var onlineRows = paymentRows
            .Where(x => x.Method == OrderPaymentMethod.Online)
            .ToList();
        var onlineTotal = onlineRows.Sum(x => x.Amount);
        // What customers paid with their balance in their favor (Fase 26D): sales of the day, but the money entered a drawer
        // when they left it, as a movement of its own.
        var balanceRows = paymentRows
            .Where(x => x.Method == OrderPaymentMethod.CustomerBalance)
            .ToList();
        var customerBalanceTotal = balanceRows.Sum(x => x.Amount);

        // What went back as «Plataforma» (an order a platform charged and then cancelled), «En línea» (through Mercado Pago)
        // or to the balance in the customer's favor (Fase 26D): it never left a drawer and has no cash movement, so it is
        // shown apart, with the filters of the payments.
        async Task<decimal> RefundsOutsideTheDrawersAsync(OrderPaymentMethod method)
        {
            var refunds = _context.OrderRefunds
                .AsNoTracking()
                .Where(x => x.Method == method && x.RefundedAt >= startUtc && x.RefundedAt <= endUtc);

            if (request.InventoryLocationId is { } refundLocationId)
            {
                // «Plataforma» and «En línea» go back only as they were paid; the balance takes a refund of an order paid
                // any way (Fase 26D), so it counts where the order was charged.
                var sameMethod = method != OrderPaymentMethod.CustomerBalance;
                refunds = refunds.Where(x => _context.OrderPayments.Any(payment =>
                    payment.OrderId == x.OrderId
                    && (!sameMethod || payment.Method == method)
                    && payment.InventoryLocationId == refundLocationId));
            }

            if (request.BranchId is { } refundBranchId)
                refunds = refunds.Where(x => _context.Orders.Any(order => order.Id == x.OrderId && order.BranchId == refundBranchId));

            if (!string.IsNullOrWhiteSpace(userNameFilter))
                refunds = refunds.Where(x => x.RefundedBy == userNameFilter || (userCalled != null && x.RefundedBy == userCalled));

            return await refunds.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        }

        var platformRefundsTotal = await RefundsOutsideTheDrawersAsync(OrderPaymentMethod.Platform);
        var onlineRefundsTotal = await RefundsOutsideTheDrawersAsync(OrderPaymentMethod.Online);
        var customerBalanceRefundsTotal = await RefundsOutsideTheDrawersAsync(OrderPaymentMethod.CustomerBalance);

        var manualTransactions = _context.CashTransactions
            .AsNoTracking()
            .Where(x =>
                x.OccurredAt >= startUtc
                && x.OccurredAt <= endUtc
                && x.TransactionType != CashTransactionType.SalePayment
                // Tips come with the sale payments and are reported apart (TipsTotal).
                && x.TransactionType != CashTransactionType.Tip)
            .AsQueryable();

        if (request.InventoryLocationId.HasValue)
            manualTransactions = manualTransactions.Where(x => x.InventoryLocationId == request.InventoryLocationId.Value);

        if (request.BranchId is { } drawerBranchId)
            manualTransactions = manualTransactions.Where(x => x.CashSession.BranchId == drawerBranchId);

        if (!string.IsNullOrWhiteSpace(userNameFilter))
            manualTransactions = manualTransactions.Where(x => x.CreatedBy == userNameFilter || (userCalled != null && x.CreatedBy == userCalled));

        var movementRows = await manualTransactions
            .Select(x => new { x.Amount, x.PaymentMethod, x.TransactionType })
            .ToListAsync(cancellationToken);
        // What customers left in their favor and what was given back to them (Fase 26D): money of the drawers, but neither
        // a sale nor a movement by hand, so it is shown on its own lines.
        static bool IsBalanceMovement(CashTransactionType type) =>
            type is CashTransactionType.CustomerDeposit or CashTransactionType.CustomerDepositReturn;
        var balanceMovements = movementRows.Where(x => IsBalanceMovement(x.TransactionType)).ToList();
        // Every other row, one by one: two withdrawals of $500 the same day are two movements, not one.
        var manualRows = movementRows.Where(x => !IsBalanceMovement(x.TransactionType)).ToList();
        var customerDepositsTotal = balanceMovements.Where(x => x.Amount > 0).Sum(x => x.Amount);
        var customerDepositReturnsTotal = balanceMovements.Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount));
        var cashBalanceMovements = balanceMovements.Where(x => CashSession.IsCashMovement(x.PaymentMethod)).ToList();
        var cashCustomerDepositsTotal = cashBalanceMovements.Where(x => x.Amount > 0).Sum(x => x.Amount);
        var cashCustomerDepositReturnsTotal = cashBalanceMovements.Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount));

        var manualIncomeTotal = manualRows
            .Where(x => x.Amount > 0)
            .Sum(x => x.Amount);
        var manualExpenseTotal = manualRows
            .Where(x => x.Amount < 0)
            .Sum(x => Math.Abs(x.Amount));
        // Only cash movements change the drawers, by the same rule as CashSession: a card or transfer refund
        // gives money back, but none of it leaves a drawer.
        var cashManualRows = manualRows
            .Where(x => CashSession.IsCashMovement(x.PaymentMethod))
            .ToList();
        var cashManualIncomeTotal = cashManualRows
            .Where(x => x.Amount > 0)
            .Sum(x => x.Amount);
        var cashManualExpenseTotal = cashManualRows
            .Where(x => x.Amount < 0)
            .Sum(x => Math.Abs(x.Amount));

        var salesPaymentsTotal = paymentRows.Sum(x => x.Amount);
        // Tips are not sales, but they are money received: in the drawer when paid in cash.
        var tipsTotal = paymentRows.Sum(x => x.Tip);
        var cashTipsTotal = paymentRows.Where(x => x.Method == OrderPaymentMethod.Cash).Sum(x => x.Tip);
        // The business has not received what a platform charged (it deposits it later, minus its commission) nor what was
        // paid online (Mercado Pago releases it, minus its fee), and their refunds move no drawer either: all stay out, so
        // the money of the day stays symmetric. What was paid with a balance in favor entered when the customer left it
        // (Fase 26D): that day's deposits count, and what was given back of them.
        var netCashFlow = salesPaymentsTotal - platformTotal - onlineTotal - customerBalanceTotal + tipsTotal
            + manualIncomeTotal - manualExpenseTotal + customerDepositsTotal - customerDepositReturnsTotal;
        var netCashOnlyFlow = cashTotal + cashTipsTotal + cashManualIncomeTotal - cashManualExpenseTotal
            + cashCustomerDepositsTotal - cashCustomerDepositReturnsTotal;

        return new DailyCashCutDto
        {
            Date = localDate,
            SalesPaymentsTotal = salesPaymentsTotal,
            SalesPaymentsCount = paymentRows.Count,
            CashTotal = cashTotal,
            CardTotal = cardTotal,
            TransferTotal = transferTotal,
            WalletTotal = walletTotal,
            OtherTotal = otherTotal,
            PlatformTotal = platformTotal,
            PlatformPaymentsCount = platformRows.Count,
            PlatformRefundsTotal = platformRefundsTotal,
            OnlineTotal = onlineTotal,
            OnlinePaymentsCount = onlineRows.Count,
            OnlineRefundsTotal = onlineRefundsTotal,
            CustomerBalanceTotal = customerBalanceTotal,
            CustomerBalancePaymentsCount = balanceRows.Count,
            CustomerBalanceRefundsTotal = customerBalanceRefundsTotal,
            CustomerDepositsTotal = customerDepositsTotal,
            CustomerDepositReturnsTotal = customerDepositReturnsTotal,
            CashCustomerDepositsTotal = cashCustomerDepositsTotal,
            CashCustomerDepositReturnsTotal = cashCustomerDepositReturnsTotal,
            TipsTotal = tipsTotal,
            CashTipsTotal = cashTipsTotal,
            ManualIncomeTotal = manualIncomeTotal,
            ManualExpenseTotal = manualExpenseTotal,
            CashManualIncomeTotal = cashManualIncomeTotal,
            CashManualExpenseTotal = cashManualExpenseTotal,
            NetCashFlow = netCashFlow,
            NetCashOnlyFlow = netCashOnlyFlow,
            ByUser = byUser,
            ByLocation = byLocation,
            ByBranch = byBranch
        };
    }
}
