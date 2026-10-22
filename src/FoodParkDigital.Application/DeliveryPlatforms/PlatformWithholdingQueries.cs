using System.Globalization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>
/// The withholdings of a platform in a year (Fase 22J, ADR 0042): month by month, what TienditApp calculated for the
/// orders the platform charged against what its CFDI de retenciones say, and the CFDI of the year.
/// </summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetPlatformWithholdingsQuery : IRequest<PlatformWithholdingsDto>
{
    /// <summary>The most CFDI the list brings: DiDi issues one a week, so a year has about 53, plus the removed ones.</summary>
    public const int MaxCertificates = 200;

    public OrderChannel Channel { get; init; }
    public int Year { get; init; }
}

public class GetPlatformWithholdingsQueryValidator : AbstractValidator<GetPlatformWithholdingsQuery>
{
    public const string YearMessage = "Elige un año desde 2019.";

    public GetPlatformWithholdingsQueryValidator()
    {
        RuleFor(x => x.Channel)
            .Must(OrderChannelRules.IsDeliveryPlatform)
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        RuleFor(x => x.Year)
            .InclusiveBetween(PlatformWithholdingCertificate.FirstYear, 9998)
            .WithMessage(YearMessage);
    }
}

public class GetPlatformWithholdingsQueryHandler : IRequestHandler<GetPlatformWithholdingsQuery, PlatformWithholdingsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPlatformWithholdingsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PlatformWithholdingsDto> Handle(GetPlatformWithholdingsQuery request, CancellationToken cancellationToken)
    {
        var zone = await PlatformSettlementReader.ZoneAsync(_context, _tenantService.TenantId, cancellationToken);
        var calculated = await CalculatedAsync(request.Channel, request.Year, zone, cancellationToken);

        // The CFDI in force. The ones whose periods overlap are compared together (a weekly one of DiDi that crosses the
        // month, with the weekly ones of both months): they count whole in the last month they cover between them, and that
        // month is compared with what was calculated in every month they cover. A month is never compared twice.
        var vouchers = await _context.PlatformWithholdingCertificates
            .AsNoTracking()
            .Where(c => c.Channel == request.Channel && c.Year == request.Year && c.RemovedAt == null)
            .Select(c => new Voucher(c.MonthFrom, c.MonthTo, c.OperationTotal, c.IsrWithheld, c.VatWithheld))
            .ToListAsync(cancellationToken);
        var blocks = Block.Of(vouchers);
        var byLastMonth = blocks.ToDictionary(b => b.To);
        Figures Of(int month) => calculated.TryGetValue(month, out var figures) ? figures : new Figures();
        Figures Over(IEnumerable<int> covered)
        {
            var sum = new Figures();
            foreach (var month in covered)
                sum.Add(Of(month));
            return sum;
        }

        var months = Enumerable.Range(1, 12).Select(month =>
        {
            var dto = Of(month).ToDto(month);
            if (byLastMonth.TryGetValue(month, out var block))
            {
                Compare(dto, block.Count, block.OperationTotal, block.Isr, block.Vat, Over(block.Months));
                dto.CoveredFromMonth = block.From < month ? block.From : null;
            }
            else if (blocks.FirstOrDefault(b => b.From <= month && month < b.To) is { } covering)
            {
                dto.ComparedInMonth = covering.To;
            }
            return dto;
        }).ToList();

        // The year: what was calculated in all of it, and the difference only over the months some CFDI covers (the
        // current one has none yet), each counted once.
        var total = Over(Enumerable.Range(1, 12)).ToDto(null);
        if (blocks.Count > 0)
        {
            Compare(
                total,
                blocks.Sum(b => b.Count),
                blocks.Sum(b => b.OperationTotal),
                blocks.Sum(b => b.Isr),
                blocks.Sum(b => b.Vat),
                Over(blocks.SelectMany(b => b.Months)));
        }

        var certificates = await _context.PlatformWithholdingCertificates
            .AsNoTracking()
            .Where(c => c.Channel == request.Channel && c.Year == request.Year)
            .OrderBy(c => c.MonthTo)
            .ThenBy(c => c.MonthFrom)
            .ThenBy(c => c.IssuedOn)
            .ThenBy(c => c.Id)
            .Take(GetPlatformWithholdingsQuery.MaxCertificates + 1)
            .ToListAsync(cancellationToken);

        var terms = await _context.DeliveryPlatformSettings
            .AsNoTracking()
            .Where(s => s.Channel == request.Channel)
            .Select(s => new { s.IsrWithholdingPercent, s.VatWithholdingPercent })
            .FirstOrDefaultAsync(cancellationToken);

        return new PlatformWithholdingsDto
        {
            Channel = request.Channel,
            Year = request.Year,
            IsrWithholdingPercent = terms?.IsrWithholdingPercent,
            VatWithholdingPercent = terms?.VatWithholdingPercent,
            Months = months,
            Total = total,
            Certificates = certificates.Take(GetPlatformWithholdingsQuery.MaxCertificates).Select(PlatformWithholdingText.ToDto).ToList(),
            CertificatesTruncated = certificates.Count > GetPlatformWithholdingsQuery.MaxCertificates
        };
    }

    /// <summary>
    /// What TienditApp calculated, by month of the business: the orders the platform charged, registered in the year, each
    /// with its withholdings in proportion to what is still paid (rounded per order, like the settlement).
    /// </summary>
    private async Task<Dictionary<int, Figures>> CalculatedAsync(
        OrderChannel channel, int year, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var startUtc = DailySalesReadModel.StartUtc(new DateOnly(year, 1, 1), zone);
        var endUtc = DailySalesReadModel.EndUtc(new DateOnly(year, 12, 31), zone);
        var rows = await (
                from detail in _context.PlatformOrderDetails.AsNoTracking()
                join order in _context.Orders.AsNoTracking() on detail.OrderId equals order.Id
                where detail.Channel == channel
                      && detail.CollectedBy == PlatformCollectedBy.Platform
                      && detail.RegisteredAt >= startUtc
                      && detail.RegisteredAt <= endUtc
                      // Given back whole, or not paid: it brings nothing.
                      && order.PaidAmount - order.RefundedAmount != 0
                select new
                {
                    detail.RegisteredAt,
                    detail.OrderTotal,
                    order.SubTotal,
                    NetPaid = order.PaidAmount - order.RefundedAmount,
                    detail.IsrWithheld,
                    detail.VatWithheld
                })
            .ToListAsync(cancellationToken);

        var months = new Dictionary<int, Figures>();
        foreach (var row in rows.Where(row => row.OrderTotal > 0))
        {
            var month = DailySalesReadModel.LocalDate(row.RegisteredAt, zone).Month;
            if (!months.TryGetValue(month, out var figures))
                months[month] = figures = new Figures();
            figures.Orders++;
            figures.SalesWithoutTax += PlatformSettlementCalculator.InProportion(row.SubTotal, row.NetPaid, row.OrderTotal);
            figures.Isr += PlatformSettlementCalculator.InProportion(row.IsrWithheld, row.NetPaid, row.OrderTotal);
            figures.Vat += PlatformSettlementCalculator.InProportion(row.VatWithheld, row.NetPaid, row.OrderTotal);
        }
        return months;
    }

    /// <param name="covered">What was calculated in the months the CFDI cover: the difference is against that.</param>
    private static void Compare(
        PlatformWithholdingPeriodDto period, int count, decimal operationTotal, decimal isr, decimal vat, Figures covered)
    {
        period.CertificateCount = count;
        period.CertificateOperationTotal = operationTotal;
        period.CertificateIsr = isr;
        period.CertificateVat = vat;
        period.IsrDifference = isr - covered.Isr;
        period.VatDifference = vat - covered.Vat;
    }

    private sealed record Voucher(int MonthFrom, int MonthTo, decimal OperationTotal, decimal Isr, decimal Vat);

    /// <summary>CFDI whose periods overlap, one after another: they are compared together, over every month they cover.</summary>
    private sealed class Block
    {
        private Block(Voucher first)
        {
            From = first.MonthFrom;
            To = first.MonthTo;
            Add(first);
        }

        public int From { get; }
        public int To { get; private set; }
        public int Count { get; private set; }
        public decimal OperationTotal { get; private set; }
        public decimal Isr { get; private set; }
        public decimal Vat { get; private set; }
        public IEnumerable<int> Months => Enumerable.Range(From, To - From + 1);

        /// <summary>The blocks of the CFDI, by month: one that starts in a month another one covers joins it.</summary>
        public static List<Block> Of(IEnumerable<Voucher> vouchers)
        {
            var blocks = new List<Block>();
            foreach (var voucher in vouchers.OrderBy(v => v.MonthFrom).ThenBy(v => v.MonthTo))
            {
                if (blocks.Count > 0 && voucher.MonthFrom <= blocks[^1].To)
                    blocks[^1].Add(voucher);
                else
                    blocks.Add(new Block(voucher));
            }
            return blocks;
        }

        private void Add(Voucher voucher)
        {
            To = Math.Max(To, voucher.MonthTo);
            Count++;
            OperationTotal += voucher.OperationTotal;
            Isr += voucher.Isr;
            Vat += voucher.Vat;
        }
    }

    private sealed class Figures
    {
        public int Orders { get; set; }
        public decimal SalesWithoutTax { get; set; }
        public decimal Isr { get; set; }
        public decimal Vat { get; set; }

        public void Add(Figures other)
        {
            Orders += other.Orders;
            SalesWithoutTax += other.SalesWithoutTax;
            Isr += other.Isr;
            Vat += other.Vat;
        }

        public PlatformWithholdingPeriodDto ToDto(int? month) => new()
        {
            Month = month,
            OrderCount = Orders,
            SalesWithoutTax = SalesWithoutTax,
            CalculatedIsr = Isr,
            CalculatedVat = Vat
        };
    }
}

/// <summary>How a CFDI de retenciones reads in the app and in its messages.</summary>
internal static class PlatformWithholdingText
{
    /// <summary>«septiembre de 2026», or «julio a septiembre de 2026» for one of several months.</summary>
    public static string Period(PlatformWithholdingCertificate certificate)
        => certificate.MonthFrom == certificate.MonthTo
            ? string.Create(CultureInfo.InvariantCulture, $"{SpanishCalendar.MonthName(certificate.MonthTo)} de {certificate.Year}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{SpanishCalendar.MonthName(certificate.MonthFrom)} a {SpanishCalendar.MonthName(certificate.MonthTo)} de {certificate.Year}");

    public static PlatformWithholdingCertificateDto ToDto(PlatformWithholdingCertificate certificate) => new()
    {
        Id = certificate.Id,
        Channel = certificate.Channel,
        Uuid = certificate.Uuid,
        IssuerRfc = certificate.IssuerRfc,
        IssuerName = certificate.IssuerName,
        Year = certificate.Year,
        MonthFrom = certificate.MonthFrom,
        MonthTo = certificate.MonthTo,
        IssuedOn = certificate.IssuedOn,
        OperationTotal = certificate.OperationTotal,
        IsrBase = certificate.IsrBase,
        IsrWithheld = certificate.IsrWithheld,
        IsrRatePercent = RatePercent(certificate.IsrWithheld, certificate.IsrBase),
        VatBase = certificate.VatBase,
        VatWithheld = certificate.VatWithheld,
        VatRatePercent = VatRatePercent(certificate.VatWithheld, certificate.VatBase),
        TotalWithheld = certificate.TotalWithheld,
        ReplacesUuid = certificate.ReplacesUuid,
        CreatedAt = certificate.CreatedAt,
        RemovedAt = certificate.RemovedAt,
        RemovalReason = certificate.RemovalReason
    };

    /// <summary>What was withheld over its base, in percent to two decimals; none without a base to divide by.</summary>
    private static decimal? RatePercent(decimal withheld, decimal? taxBase)
        => taxBase is > 0 ? decimal.Round(withheld / taxBase.Value * 100m, 2, MidpointRounding.ToEven) : null;

    /// <summary>
    /// The IVA withheld as a share of the IVA the platform charged. The SAT does not fix what the base of the IVA withheld
    /// is: when it is the IVA, the share is a big part of it (half, or all of it); when an issuer writes the amount without
    /// IVA instead, the share comes out a few percent and says nothing about the IVA, so none is given.
    /// </summary>
    private static decimal? VatRatePercent(decimal withheld, decimal? taxBase)
        => RatePercent(withheld, taxBase) is >= 25m and var rate ? rate : null;
}
