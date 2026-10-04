using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Reporting.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reports.Queries;

/// <summary>
/// Sales by branch and by day in a range of local dates (the current month when omitted): what was charged by
/// method, what was given back and how many orders came in. Closed days come from the daily snapshots.
/// </summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetSalesByBranchQuery : IRequest<SalesByBranchReportDto>
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? BranchId { get; init; }
}

public class GetSalesByBranchQueryValidator : AbstractValidator<GetSalesByBranchQuery>
{
    public GetSalesByBranchQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithMessage("La fecha inicial debe ser anterior a la final.")
            .Must(x => x.From is null || x.To is null || x.To.Value.DayNumber - x.From.Value.DayNumber < DailySalesReadModel.MaxRangeDays)
            .WithMessage($"El periodo puede abarcar como máximo {DailySalesReadModel.MaxRangeDays} días.");
    }
}

public class GetSalesByBranchQueryHandler : IRequestHandler<GetSalesByBranchQuery, SalesByBranchReportDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetSalesByBranchQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<SalesByBranchReportDto> Handle(GetSalesByBranchQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var utcNow = _dateTimeProvider.UtcNow;
        // TenantSettings is not ITenantScoped (no global filter): the tenant must be explicit.
        var timeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = TenantDateTimeResolver.Resolve(timeZoneId);
        var today = DailySalesReadModel.LocalToday(zone, utcNow);
        var from = request.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = request.To ?? (request.From is null ? today : from.AddMonths(1).AddDays(-1));
        if (to.DayNumber - from.DayNumber >= DailySalesReadModel.MaxRangeDays)
            to = from.AddDays(DailySalesReadModel.MaxRangeDays - 1);

        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, tenantId, cancellationToken);
        var readableTypes = new[] { OrderType.Retail, OrderType.Service }
            .Where(type => TenantCapabilityRules.CanCreateOrderType(capabilities, type))
            .ToHashSet();

        var read = await DailySalesReadModel.ReadAsync(_context, tenantId, zone, from, to, utcNow, cancellationToken);
        var buckets = read.Buckets
            .Where(x => readableTypes.Contains(x.OrderType))
            .Where(x => request.BranchId is null || x.BranchId == request.BranchId)
            .ToList();

        var branchNames = await _context.Branches
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var byBranch = buckets
            .GroupBy(x => x.BranchId)
            .Select(g => new BranchSalesDto
            {
                BranchId = g.Key,
                BranchName = g.Key is { } id && branchNames.TryGetValue(id, out var name) ? name : "Sin sucursal",
                Figures = SalesFiguresDto.From(Sum(g))
            })
            .OrderByDescending(x => x.Figures.NetSales)
            .ToList();

        var byDay = DailySalesReadModel.EachDay(from, to)
            .Select(date =>
            {
                var figures = Sum(buckets.Where(x => x.Date == date));
                return new DailySalesPointDto
                {
                    Date = date,
                    NetSales = figures.NetSales,
                    PaymentsAmount = figures.PaymentsAmount,
                    RefundsAmount = figures.RefundsAmount,
                    PaidOrders = figures.PaidOrders,
                    OrdersCreated = figures.OrdersCreated
                };
            })
            .ToList();

        return new SalesByBranchReportDto
        {
            From = from,
            To = to,
            Totals = SalesFiguresDto.From(Sum(buckets)),
            Branches = byBranch,
            Days = byDay,
            SnapshotDays = read.SnapshotDays,
            LiveDays = read.LiveDays
        };
    }

    private static DailySalesFigures Sum(IEnumerable<DailySalesBucket> buckets)
        => buckets.Aggregate(DailySalesFigures.Zero, (total, bucket) => total.Add(bucket.Figures));
}

public class SalesByBranchReportDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public SalesFiguresDto Totals { get; set; } = new();
    public List<BranchSalesDto> Branches { get; set; } = [];
    public List<DailySalesPointDto> Days { get; set; } = [];
    /// <summary>Days read from the daily snapshots.</summary>
    public int SnapshotDays { get; set; }
    /// <summary>Days computed from the sales (today, and closed days not stored yet).</summary>
    public int LiveDays { get; set; }
}

public class BranchSalesDto
{
    public Guid? BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public SalesFiguresDto Figures { get; set; } = new();
}

public class DailySalesPointDto
{
    public DateOnly Date { get; set; }
    public decimal NetSales { get; set; }
    public decimal PaymentsAmount { get; set; }
    public decimal RefundsAmount { get; set; }
    public int PaidOrders { get; set; }
    public int OrdersCreated { get; set; }
}

public class SalesFiguresDto
{
    public int OrdersCreated { get; set; }
    /// <summary>Orders with payments, counted on each day they were paid.</summary>
    public int PaidOrders { get; set; }
    public int PaymentsCount { get; set; }
    public decimal PaymentsAmount { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal TransferAmount { get; set; }
    public decimal WalletAmount { get; set; }
    public decimal OtherAmount { get; set; }
    public int RefundsCount { get; set; }
    public decimal RefundsAmount { get; set; }
    /// <summary>Charged minus given back.</summary>
    public decimal NetSales { get; set; }
    public decimal AverageTicket { get; set; }

    public static SalesFiguresDto From(DailySalesFigures figures) => new()
    {
        OrdersCreated = figures.OrdersCreated,
        PaidOrders = figures.PaidOrders,
        PaymentsCount = figures.PaymentsCount,
        PaymentsAmount = figures.PaymentsAmount,
        CashAmount = figures.CashAmount,
        CardAmount = figures.CardAmount,
        TransferAmount = figures.TransferAmount,
        WalletAmount = figures.WalletAmount,
        OtherAmount = figures.OtherAmount,
        RefundsCount = figures.RefundsCount,
        RefundsAmount = figures.RefundsAmount,
        NetSales = figures.NetSales,
        AverageTicket = figures.PaidOrders > 0 ? Math.Round(figures.NetSales / figures.PaidOrders, 2) : 0m
    };
}
