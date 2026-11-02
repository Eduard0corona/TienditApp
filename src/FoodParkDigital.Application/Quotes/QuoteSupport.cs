using System.Text.Json;
using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Entities;
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
/// units of its service the customer kept, with their IVA, and never more than the sale still has paid.
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

        var paid = await PaidForTheVisitAsync(context, tenantId, saleId, appointment.ServiceProductId, cancellationToken);
        return paid > 0 ? new QuoteVisitCredit(saleId, paid.Value) : null;
    }

    /// <summary>
    /// What the sale of the visit a quote takes off still has paid for the visit, after what was refunded or returned since.
    /// Null once that sale was cancelled: it no longer takes anything off.
    /// </summary>
    public static async Task<decimal?> StillPaidAsync(IApplicationDbContext context, Quote quote, CancellationToken cancellationToken)
    {
        if (quote.VisitCreditSaleOrderId is not { } saleId)
            return null;
        return await PaidForTheVisitAsync(
            context, quote.TenantId, saleId, await VisitServiceAsync(context, quote, cancellationToken), cancellationToken);
    }

    /// <summary>The service of the appointment of the visit a quote comes from, whose units its sale charged for the visit.</summary>
    public static async Task<Guid?> VisitServiceAsync(IApplicationDbContext context, Quote quote, CancellationToken cancellationToken)
        => quote.AppointmentId is { } appointmentId
            ? await context.Appointments
                .AsNoTracking()
                .Where(a => a.Id == appointmentId && a.TenantId == quote.TenantId)
                .Select(a => a.ServiceProductId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

    /// <summary>
    /// What a sale has paid for its visit: what it charges for it (the charge of its zone and the units of the service the
    /// customer kept, without those cancelled or returned, with their IVA), never more than the sale still has paid (what was
    /// paid less what was refunded). Null when the sale is cancelled.
    /// </summary>
    private static async Task<decimal?> PaidForTheVisitAsync(
        IApplicationDbContext context, Guid tenantId, Guid saleId, Guid? serviceProductId, CancellationToken cancellationToken)
    {
        var sale = await context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == saleId && o.TenantId == tenantId, cancellationToken);
        return sale is null ? null : PaidForTheVisit(sale, serviceProductId);
    }

    /// <summary>
    /// What <paramref name="sale"/>, as loaded with its lines, has paid for its visit (see <see cref="PaidForTheVisitAsync"/>).
    /// Null when it is cancelled.
    /// </summary>
    public static decimal? PaidForTheVisit(Order sale, Guid? serviceProductId)
    {
        if (sale.Status == OrderStatus.Cancelled)
            return null;

        var amounts = sale.Items
            .Where(item => serviceProductId is { } service && item.ProductId == service)
            .Select(item => new TaxableAmount(Kept(item), item.VatRate))
            .Where(amount => amount.Amount > 0)
            .ToList();
        if (sale.VisitCharge is { Fee: > 0 } charge)
            amounts.Add(new TaxableAmount(charge.Fee, charge.FeeVatRate));
        var charged = OrderTaxCalculator.Calculate(sale.PricesIncludeTax, amounts).Sum(line => line.Total);
        return decimal.Round(Math.Max(0m, Math.Min(charged, sale.NetPaidAmount)), 2, MidpointRounding.ToEven);
    }

    /// <summary>What a line charges for the units the customer kept: its charge in proportion, without those returned.</summary>
    private static decimal Kept(OrderItem item)
        => item.ActiveQuantity <= 0 || item.ReturnableQuantity <= 0
            ? 0m
            : decimal.Round(item.ChargedPrice * item.ReturnableQuantity / item.ActiveQuantity, 2, MidpointRounding.ToEven);

    /// <summary>
    /// How a quote's visit credit reads: what it takes off now; once it takes nothing off (its sale was cancelled, refunded
    /// or the visit returned), what it took off, as not applying.
    /// </summary>
    public static QuoteVisitCreditDto? Describe(Quote quote, decimal? stillPaidForTheVisit)
    {
        if (quote.VisitCreditAmount is not { } fixedAmount)
            return null;
        var standing = quote.VisitCreditStanding(stillPaidForTheVisit);
        return standing > 0
            ? new QuoteVisitCreditDto { Amount = standing, Applies = true }
            : new QuoteVisitCreditDto { Amount = fixedAmount, Applies = false };
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
    public const string WhatsAppTemplateChanged = "QuoteWhatsAppTemplateChanged";
    public const string Converted = "QuoteConverted";
    public const string WorkScheduled = "QuoteWorkScheduled";
    /// <summary>Who answered when the customer did from the quote's link (Fase 29G): the activity shows it as is.</summary>
    public const string CustomerFromLink = "Cliente (enlace)";

    public static AuditLog Create(Quote quote, string action, string? performedBy, object? extra = null)
        => new(
            tenantId: quote.TenantId,
            entityName: nameof(Quote),
            entityId: quote.Id.ToString(),
            action: action,
            performedBy: performedBy ?? "system",
            metadata: JsonSerializer.Serialize(new { quote.Number, quote.Revision, quote.Total, Extra = extra }));
}

/// <summary>The corrections of a quote the customer accepted.</summary>
internal static class QuoteCorrections
{
    /// <summary>
    /// Accepted, a correction started on it can never be sent: it is discarded, and stays in the history. Whoever recorded
    /// the answer (the business, or the customer from the link since the Fase 29G) is who discarded it.
    /// </summary>
    public static async Task DiscardPendingAsync(
        IApplicationDbContext context, Quote accepted, DateTimeOffset now, string? performedBy, CancellationToken cancellationToken)
    {
        var pendingCorrections = await context.Quotes
            .Include(q => q.Lines)
            .Where(q => q.TenantId == accepted.TenantId && q.Number == accepted.Number && q.Status == QuoteStatus.Draft)
            .ToListAsync(cancellationToken);
        foreach (var correction in pendingCorrections)
        {
            correction.Discard(now);
            context.AuditLogs.Add(QuoteAudit.Create(correction, QuoteAudit.Discarded, performedBy, new { Reason = "AcceptedEarlierVersion" }));
        }
    }
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
        var stillPaid = await QuoteVisitCredits.StillPaidAsync(context, quote, cancellationToken);
        var sends = await QuoteDeliveries.LatestAsync(context, quote, cancellationToken);
        var status = quote.StatusOn(today);
        var draft = versions.FirstOrDefault(v => v.Status == QuoteStatus.Draft);
        // The service order it became (Fase 29E), or whether it can become one now.
        var order = quote.OrderId is { } orderId
            ? await context.Orders
                .AsNoTracking()
                .Where(o => o.Id == orderId && o.TenantId == quote.TenantId)
                .Select(o => new QuoteOrderDto { Id = o.Id, OrderNumber = o.OrderNumber, Status = o.Status })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(context, quote.TenantId, cancellationToken);
        var canConvert = quote.Status == QuoteStatus.Accepted && quote.OrderId is null
            && TenantCapabilityRules.CanCreateOrderType(capabilities, OrderType.Service);
        // The appointments that do its job (Fase 29F), and whether more can be agendadas now.
        if (order is not null)
            order.WorkAppointments = (await WorkAppointments.ForOrdersAsync(context, quote.TenantId, [order.Id], cancellationToken))
                .GetValueOrDefault(order.Id) ?? [];
        var canScheduleWork = order is not null && order.Status != OrderStatus.Cancelled
            && TenantCapabilityRules.CanAccessAppointments(capabilities);

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
            VisitCredit = QuoteVisitCredits.Describe(quote, stillPaid),
            ToPay = quote.ToPay(stillPaid),
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
            CreatedAt = quote.CreatedAt,
            Emails = sends.Emails,
            WhatsApps = sends.WhatsApps,
            WhatsAppConnected = await CampaignRules.IsWhatsAppConnectedAsync(context, quote.TenantId, cancellationToken),
            Order = order,
            ConvertedAt = quote.ConvertedAt,
            CanConvertToOrder = canConvert,
            CanScheduleWork = canScheduleWork
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
