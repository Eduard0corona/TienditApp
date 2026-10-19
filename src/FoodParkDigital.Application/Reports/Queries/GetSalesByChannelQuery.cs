using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reports.Queries;

/// <summary>
/// Sales by where the orders came from, in a range of local dates (the current month when omitted), on the cash basis of
/// Inicio and of sales by branch: what was charged on the day of each payment minus what was given back on the day of
/// each refund. For the delivery platforms it adds what they charged in their apps, their commission and its IVA, what
/// they withheld and what they should deposit. Those follow the money: each payment of an order adds its share of the
/// amounts the order copied when it was registered (amount × payment / order total), and each refund takes its share
/// back, so a closed day never changes and a range that covers an order and its void nets to zero.
/// </summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetSalesByChannelQuery : IRequest<SalesByChannelReportDto>, IReportDateRange
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? BranchId { get; init; }
}

public class GetSalesByChannelQueryValidator : AbstractValidator<GetSalesByChannelQuery>
{
    public GetSalesByChannelQueryValidator()
    {
        this.ValidateTheRange();
    }
}

public class GetSalesByChannelQueryHandler : IRequestHandler<GetSalesByChannelQuery, SalesByChannelReportDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetSalesByChannelQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<SalesByChannelReportDto> Handle(GetSalesByChannelQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var timeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = TenantDateTimeResolver.Resolve(timeZoneId);
        var (from, to) = DailySalesReadModel.ResolveRange(
            request.From, request.To, DailySalesReadModel.LocalToday(zone, _dateTimeProvider.UtcNow));
        var startUtc = DailySalesReadModel.StartUtc(from, zone);
        var endUtc = DailySalesReadModel.EndUtc(to, zone);

        // The kinds of order the business can read, as in sales by branch and Inicio.
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, tenantId, cancellationToken);
        var retail = TenantCapabilityRules.CanCreateOrderType(capabilities, OrderType.Retail);
        var service = TenantCapabilityRules.CanCreateOrderType(capabilities, OrderType.Service);

        var payments = _context.OrderPayments
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PaidAt >= startUtc && x.PaidAt <= endUtc)
            .Where(x => (retail && x.Order.OrderType == OrderType.Retail) || (service && x.Order.OrderType == OrderType.Service))
            .Select(x => new { x.OrderId, x.Amount, x.Method, x.Order.Channel, x.Order.BranchId });
        var refunds =
            from refund in _context.OrderRefunds.AsNoTracking()
            join order in _context.Orders.AsNoTracking() on refund.OrderId equals order.Id
            where refund.TenantId == tenantId
                && refund.RefundedAt >= startUtc
                && refund.RefundedAt <= endUtc
                && ((retail && order.OrderType == OrderType.Retail) || (service && order.OrderType == OrderType.Service))
            select new { refund.OrderId, refund.Amount, refund.Method, order.Channel, order.BranchId };
        if (request.BranchId is { } branchId)
        {
            payments = payments.Where(x => x.BranchId == branchId);
            refunds = refunds.Where(x => x.BranchId == branchId);
        }

        // Four aggregates in the database: nothing is read row by row.
        var charged = await payments
            .GroupBy(x => x.Channel)
            .Select(g => new
            {
                Channel = g.Key,
                // Each order once in the period, as Inicio counts them.
                PaidOrders = g.Select(x => x.OrderId).Distinct().Count(),
                Amount = g.Sum(x => x.Amount),
                Platform = g.Sum(x => x.Method == OrderPaymentMethod.Platform ? x.Amount : 0m),
                Online = g.Sum(x => x.Method == OrderPaymentMethod.Online ? x.Amount : 0m)
            })
            .ToListAsync(cancellationToken);
        var givenBack = await refunds
            .GroupBy(x => x.Channel)
            .Select(g => new
            {
                Channel = g.Key,
                Amount = g.Sum(x => x.Amount),
                Platform = g.Sum(x => x.Method == OrderPaymentMethod.Platform ? x.Amount : 0m),
                Online = g.Sum(x => x.Method == OrderPaymentMethod.Online ? x.Amount : 0m)
            })
            .ToListAsync(cancellationToken);
        var chargedShares = await (
                from payment in payments
                join detail in _context.PlatformOrderDetails.AsNoTracking() on payment.OrderId equals detail.OrderId
                group new
                {
                    payment.Amount,
                    detail.OrderTotal,
                    detail.Commission,
                    detail.CommissionTax,
                    detail.IsrWithheld,
                    detail.VatWithheld
                } by payment.Channel into g
                select new PlatformShares(
                    g.Key,
                    g.Sum(x => x.Commission * x.Amount / x.OrderTotal),
                    g.Sum(x => x.CommissionTax * x.Amount / x.OrderTotal),
                    g.Sum(x => x.IsrWithheld * x.Amount / x.OrderTotal),
                    g.Sum(x => x.VatWithheld * x.Amount / x.OrderTotal)))
            .ToListAsync(cancellationToken);
        var givenBackShares = await (
                from refund in refunds
                join detail in _context.PlatformOrderDetails.AsNoTracking() on refund.OrderId equals detail.OrderId
                group new
                {
                    refund.Amount,
                    detail.OrderTotal,
                    detail.Commission,
                    detail.CommissionTax,
                    detail.IsrWithheld,
                    detail.VatWithheld
                } by refund.Channel into g
                select new PlatformShares(
                    g.Key,
                    g.Sum(x => x.Commission * x.Amount / x.OrderTotal),
                    g.Sum(x => x.CommissionTax * x.Amount / x.OrderTotal),
                    g.Sum(x => x.IsrWithheld * x.Amount / x.OrderTotal),
                    g.Sum(x => x.VatWithheld * x.Amount / x.OrderTotal)))
            .ToListAsync(cancellationToken);

        // The platforms the business takes orders from show even without sales, so a quiet week reads as zero.
        var activePlatforms = await _context.DeliveryPlatformSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .Select(x => x.Channel)
            .ToListAsync(cancellationToken);

        var channels = charged.Select(x => x.Channel)
            .Concat(givenBack.Select(x => x.Channel))
            .Concat(activePlatforms)
            .Distinct()
            .OrderBy(channel => channel)
            .ToList();
        var rows = channels
            .Select(channel =>
            {
                var paid = charged.FirstOrDefault(x => x.Channel == channel);
                var back = givenBack.FirstOrDefault(x => x.Channel == channel);
                return Row(
                    channel,
                    paid?.PaidOrders ?? 0,
                    paid?.Amount ?? 0m,
                    paid?.Platform ?? 0m,
                    back?.Amount ?? 0m,
                    back?.Platform ?? 0m,
                    (paid?.Online ?? 0m) - (back?.Online ?? 0m),
                    PlatformShares.Net(
                        chargedShares.FirstOrDefault(x => x.Channel == channel),
                        givenBackShares.FirstOrDefault(x => x.Channel == channel)));
            })
            .ToList();

        return new SalesByChannelReportDto
        {
            From = from,
            To = to,
            Channels = rows,
            Totals = Total(rows)
        };
    }

    private static ChannelSalesDto Row(
        OrderChannel channel,
        int paidOrders,
        decimal paymentsAmount,
        decimal platformPayments,
        decimal refundsAmount,
        decimal platformRefunds,
        decimal onlineCollected,
        PlatformShares shares)
    {
        var isPlatform = OrderChannelRules.IsDeliveryPlatform(channel);
        var netSales = paymentsAmount - refundsAmount;
        var platformCollected = platformPayments - platformRefunds;
        // Summed in full in the database and rounded once per row; the expected settlement comes from the rounded
        // amounts, so the identity holds to the cent (I7).
        var commission = Round(shares.Commission);
        var commissionTax = Round(shares.CommissionTax);
        var isrWithheld = Round(shares.IsrWithheld);
        var vatWithheld = Round(shares.VatWithheld);

        return new ChannelSalesDto
        {
            Channel = channel,
            IsDeliveryPlatform = isPlatform,
            PaidOrders = paidOrders,
            PaymentsAmount = paymentsAmount,
            RefundsAmount = refundsAmount,
            NetSales = netSales,
            PlatformCollected = platformCollected,
            // What the business charged itself of the platform's orders (the courier paid it): the platform discounts
            // its commission from a deposit.
            BusinessCollected = isPlatform ? netSales - platformCollected : 0m,
            OnlineCollected = onlineCollected,
            Commission = commission,
            CommissionTax = commissionTax,
            IsrWithheld = isrWithheld,
            VatWithheld = vatWithheld,
            ExpectedSettlement = platformCollected - commission - commissionTax - isrWithheld - vatWithheld
        };
    }

    /// <summary>The sum of the rows, as they show.</summary>
    private static ChannelSalesDto Total(IReadOnlyCollection<ChannelSalesDto> rows) => new()
    {
        Channel = null,
        IsDeliveryPlatform = false,
        PaidOrders = rows.Sum(x => x.PaidOrders),
        PaymentsAmount = rows.Sum(x => x.PaymentsAmount),
        RefundsAmount = rows.Sum(x => x.RefundsAmount),
        NetSales = rows.Sum(x => x.NetSales),
        PlatformCollected = rows.Sum(x => x.PlatformCollected),
        BusinessCollected = rows.Sum(x => x.BusinessCollected),
        OnlineCollected = rows.Sum(x => x.OnlineCollected),
        Commission = rows.Sum(x => x.Commission),
        CommissionTax = rows.Sum(x => x.CommissionTax),
        IsrWithheld = rows.Sum(x => x.IsrWithheld),
        VatWithheld = rows.Sum(x => x.VatWithheld),
        ExpectedSettlement = rows.Sum(x => x.ExpectedSettlement)
    };

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.ToEven);

    /// <summary>A channel's share of the amounts its orders copied, before rounding.</summary>
    private sealed record PlatformShares(
        OrderChannel Channel,
        decimal Commission,
        decimal CommissionTax,
        decimal IsrWithheld,
        decimal VatWithheld)
    {
        /// <summary>What the payments added minus what the refunds took back.</summary>
        public static PlatformShares Net(PlatformShares? charged, PlatformShares? givenBack) => new(
            charged?.Channel ?? givenBack?.Channel ?? default,
            (charged?.Commission ?? 0m) - (givenBack?.Commission ?? 0m),
            (charged?.CommissionTax ?? 0m) - (givenBack?.CommissionTax ?? 0m),
            (charged?.IsrWithheld ?? 0m) - (givenBack?.IsrWithheld ?? 0m),
            (charged?.VatWithheld ?? 0m) - (givenBack?.VatWithheld ?? 0m));
    }
}

public class SalesByChannelReportDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>The channels with payments or refunds in the period and the active platforms, in the order of the enum.</summary>
    public List<ChannelSalesDto> Channels { get; set; } = [];
    /// <summary>The sum of the rows.</summary>
    public ChannelSalesDto Totals { get; set; } = new();
}

/// <summary>
/// What one channel sold in the period. The figures of the platforms (what they charged, their commission and its IVA,
/// the withholdings and the expected settlement) are 0 for the counter, WhatsApp and the online menu.
/// </summary>
public class ChannelSalesDto
{
    /// <summary>The channel; null in the totals.</summary>
    public OrderChannel? Channel { get; set; }
    public bool IsDeliveryPlatform { get; set; }
    /// <summary>Orders with a payment in the period, each one once (as Inicio counts them).</summary>
    public int PaidOrders { get; set; }
    public decimal PaymentsAmount { get; set; }
    public decimal RefundsAmount { get; set; }
    /// <summary>Charged minus given back.</summary>
    public decimal NetSales { get; set; }
    /// <summary>What the platform charged in its app («Plataforma» payments) minus what went back as «Plataforma».</summary>
    public decimal PlatformCollected { get; set; }
    /// <summary>What the business charged itself of the platform's orders: <see cref="NetSales"/> minus <see cref="PlatformCollected"/>.</summary>
    public decimal BusinessCollected { get; set; }
    /// <summary>
    /// What customers paid online in Mercado Pago («En línea», Fase 24I) minus what went back that way: it is in the
    /// business's Mercado Pago account, not in its drawers. Only the online menu has it.
    /// </summary>
    public decimal OnlineCollected { get; set; }
    public decimal Commission { get; set; }
    /// <summary>The IVA the platform charges on its commission.</summary>
    public decimal CommissionTax { get; set; }
    public decimal IsrWithheld { get; set; }
    public decimal VatWithheld { get; set; }
    /// <summary>
    /// What the platform should deposit: what it charged minus the commission, its IVA and the withholdings. Negative when
    /// the business charged the orders itself: it owes the platform the commission and its IVA.
    /// </summary>
    public decimal ExpectedSettlement { get; set; }
}
