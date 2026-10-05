using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Enums;
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

        var orderPayments = _context.OrderPayments
            .AsNoTracking()
            .Where(x => x.PaidAt >= startUtc && x.PaidAt <= endUtc)
            .AsQueryable();

        if (request.InventoryLocationId.HasValue)
            orderPayments = orderPayments.Where(x => x.InventoryLocationId == request.InventoryLocationId.Value);

        if (request.BranchId is { } branchId)
            orderPayments = orderPayments.Where(x => x.Order.BranchId == branchId);

        if (!string.IsNullOrWhiteSpace(userNameFilter))
            orderPayments = orderPayments.Where(x => x.CreatedBy == userNameFilter);

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

        var byUser = paymentRows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.CreatedBy) ? "Sistema" : x.CreatedBy!)
            .Select(x => new DailyCashCutByUserDto
            {
                UserName = x.Key,
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
        var otherTotal = paymentRows
            .Where(x => x.Method == OrderPaymentMethod.Other)
            .Sum(x => x.Amount);

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
            manualTransactions = manualTransactions.Where(x => x.CreatedBy == userNameFilter);

        var manualRows = await manualTransactions
            .Select(x => x.Amount)
            .ToListAsync(cancellationToken);

        var manualIncomeTotal = manualRows
            .Where(x => x > 0)
            .Sum();
        var manualExpenseTotal = manualRows
            .Where(x => x < 0)
            .Sum(x => Math.Abs(x));

        var salesPaymentsTotal = paymentRows.Sum(x => x.Amount);
        // Tips are not sales, but they are money received: in the drawer when paid in cash.
        var tipsTotal = paymentRows.Sum(x => x.Tip);
        var cashTipsTotal = paymentRows.Where(x => x.Method == OrderPaymentMethod.Cash).Sum(x => x.Tip);
        var netCashFlow = salesPaymentsTotal + tipsTotal + manualIncomeTotal - manualExpenseTotal;
        var netCashOnlyFlow = cashTotal + cashTipsTotal + manualIncomeTotal - manualExpenseTotal;

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
            TipsTotal = tipsTotal,
            CashTipsTotal = cashTipsTotal,
            ManualIncomeTotal = manualIncomeTotal,
            ManualExpenseTotal = manualExpenseTotal,
            NetCashFlow = netCashFlow,
            NetCashOnlyFlow = netCashOnlyFlow,
            ByUser = byUser,
            ByLocation = byLocation,
            ByBranch = byBranch
        };
    }
}
