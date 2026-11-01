using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Taxes;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Quotes;

/// <summary>The business's day, which decides when a quote expires.</summary>
internal static class QuoteCalendar
{
    public static DateOnly Today(string? timeZone, DateTimeOffset utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, TenantDateTimeResolver.Resolve(timeZone)).DateTime);

    public static async Task<DateOnly> TodayAsync(
        IApplicationDbContext context, Guid tenantId, DateTimeOffset utcNow, CancellationToken cancellationToken)
        => Today((await TenantSalesSettings.ReadAsync(context, tenantId, cancellationToken)).TimeZone, utcNow);
}

/// <summary>
/// What the customer paid for the visit that a quote takes off (ADR 0053 point 14): the visit was agreed «Se descuenta si
/// contratas» (or, scheduled by the team without a policy, the business charges its visits that way now), and its
/// appointment has a sale that is not cancelled. It is what that sale charged for the visit, the charge of its zone and the
/// lines of its service with their IVA, and never more than the sale has paid.
/// </summary>
internal static class QuoteVisitCredits
{
    public static async Task<QuoteVisitCredit?> ForAsync(
        IApplicationDbContext context, Guid tenantId, Guid? appointmentId, CancellationToken cancellationToken)
    {
        if (appointmentId is not { } id)
            return null;
        var appointment = await context.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, cancellationToken);
        if (appointment?.Visit is not { } visit || appointment.SaleOrderId is not { } saleId)
            return null;

        var policy = visit.ChargePolicy ?? await context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (HomeVisitChargePolicy?)s.HomeVisitChargePolicy)
            .FirstOrDefaultAsync(cancellationToken);
        if (policy != HomeVisitChargePolicy.CreditedIfHired)
            return null;

        var sale = await context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == saleId && o.TenantId == tenantId, cancellationToken);
        if (sale is null || sale.Status == OrderStatus.Cancelled)
            return null;

        var amounts = sale.Items
            .Where(item => appointment.ServiceProductId is { } service && item.ProductId == service && item.ChargedPrice > 0)
            .Select(item => new TaxableAmount(item.ChargedPrice, item.VatRate))
            .ToList();
        if (sale.VisitCharge is { Fee: > 0 } charge)
            amounts.Add(new TaxableAmount(charge.Fee, charge.FeeVatRate));
        var charged = OrderTaxCalculator.Calculate(sale.PricesIncludeTax, amounts).Sum(line => line.Total);
        var credit = Math.Min(charged, sale.NetPaidAmount);
        return credit > 0 ? new QuoteVisitCredit(sale.Id, decimal.Round(credit, 2, MidpointRounding.ToEven)) : null;
    }

    /// <summary>The sales of the visits these quotes take off that were cancelled since: they no longer take them off.</summary>
    public static async Task<IReadOnlySet<Guid>> CancelledAsync(
        IApplicationDbContext context, IEnumerable<Guid?> saleOrderIds, CancellationToken cancellationToken)
    {
        var ids = saleOrderIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();
        return (await context.Orders
                .AsNoTracking()
                .Where(o => ids.Contains(o.Id) && o.Status == OrderStatus.Cancelled)
                .Select(o => o.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}

/// <summary>What the activity log keeps of a quote: its folio, version and total, never what the customer answered.</summary>
internal static class QuoteAudit
{
    public const string Created = "QuoteCreated";
    public const string Sent = "QuoteSent";
    public const string Revised = "QuoteRevised";
    public const string Discarded = "QuoteDiscarded";
    public const string Accepted = "QuoteAccepted";
    public const string Rejected = "QuoteRejected";
    public const string SettingsUpdated = "QuoteSettingsUpdated";

    public static AuditLog Create(Quote quote, string action, string? performedBy, object? extra = null)
        => new(
            tenantId: quote.TenantId,
            entityName: nameof(Quote),
            entityId: quote.Id.ToString(),
            action: action,
            performedBy: performedBy ?? "system",
            metadata: JsonSerializer.Serialize(new { quote.Number, quote.Revision, quote.Total, Extra = extra }));
}

/// <summary>How a quote reads for the business.</summary>
internal static class QuoteViews
{
    public static async Task<QuoteDto> DescribeAsync(
        IApplicationDbContext context, Quote quote, DateOnly today, CancellationToken cancellationToken)
    {
        var versions = await context.Quotes
            .AsNoTracking()
            .Where(q => q.TenantId == quote.TenantId && q.Number == quote.Number)
            .OrderBy(q => q.Revision)
            .Select(q => new { q.Id, q.Revision, q.Status, q.ValidUntil, q.Total, q.SentAt, q.ClosedAt })
            .ToListAsync(cancellationToken);
        var appointment = quote.AppointmentId is { } appointmentId
            ? await context.Appointments
                .AsNoTracking()
                .Where(a => a.Id == appointmentId)
                .Select(a => new QuoteAppointmentDto
                {
                    Id = a.Id,
                    StartAt = a.StartAt,
                    ServiceName = a.ServiceName,
                    PostalCode = a.Visit != null ? a.Visit.PostalCode : null
                })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var applies = quote.VisitCreditSaleOrderId is { } sale
            && !(await QuoteVisitCredits.CancelledAsync(context, [sale], cancellationToken)).Contains(sale);
        var status = quote.StatusOn(today);
        var draft = versions.FirstOrDefault(v => v.Status == QuoteStatus.Draft);

        return new QuoteDto
        {
            Id = quote.Id,
            Number = quote.Number,
            Revision = quote.Revision,
            Status = status,
            CustomerId = quote.CustomerId,
            CustomerName = quote.CustomerName,
            CustomerPhone = quote.CustomerPhone,
            CustomerEmail = quote.CustomerEmail,
            Appointment = appointment,
            PricesIncludeTax = quote.PricesIncludeTax,
            ValidityDays = quote.ValidityDays,
            ValidUntil = quote.ValidUntil,
            Note = quote.Note,
            Lines = quote.Lines.OrderBy(line => line.Position).Select(line => new QuoteLineDto
            {
                Position = line.Position,
                ProductId = line.ProductId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                TaxRate = line.TaxRate,
                IsTaxExempt = line.IsTaxExempt,
                Total = line.Total
            }).ToList(),
            SubTotal = quote.SubTotal,
            Tax = quote.Tax,
            Taxes = Taxes(quote),
            Total = quote.Total,
            VisitCredit = quote.VisitCreditAmount is { } amount ? new QuoteVisitCreditDto { Amount = amount, Applies = applies } : null,
            ToPay = quote.ToPay(applies),
            SentAt = quote.SentAt,
            TrackingToken = quote.LinkOpensOn(today) ? quote.TrackingToken : null,
            ClosedAt = quote.ClosedAt,
            ReplyChannel = quote.ReplyChannel,
            CustomerResponse = quote.CustomerResponse,
            DraftRevisionId = (status is QuoteStatus.Sent or QuoteStatus.Expired or QuoteStatus.Rejected) && draft is not null && draft.Id != quote.Id
                ? draft.Id
                : null,
            Versions = versions.Select(v => new QuoteVersionDto
            {
                Id = v.Id,
                Revision = v.Revision,
                Status = v.Status == QuoteStatus.Sent && v.ValidUntil is { } until && today > until ? QuoteStatus.Expired : v.Status,
                Total = v.Total,
                SentAt = v.SentAt,
                ClosedAt = v.ClosedAt
            }).ToList(),
            CreatedAt = quote.CreatedAt
        };
    }

    public static List<QuoteTaxDto> Taxes(Quote quote)
        => quote.GetTaxBreakdown().Select(tax => new QuoteTaxDto
        {
            RatePercent = tax.RatePercent,
            IsExempt = tax.IsExempt,
            TaxableBase = tax.TaxableBase,
            TaxAmount = tax.TaxAmount
        }).ToList();
}
