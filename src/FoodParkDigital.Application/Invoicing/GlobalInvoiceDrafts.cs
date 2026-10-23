using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>A sale the global invoice covers, as its preview lists it.</summary>
public class GlobalInvoiceSaleDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    /// <summary>
    /// When it was paid in full (its last payment): the day that puts it in the period, or in an earlier one that already has
    /// its global invoice (see <see cref="GlobalInvoiceDrafts.LateSalesWarning"/>).
    /// </summary>
    public DateTimeOffset PaidAt { get; set; }
    public decimal Total { get; set; }
}

/// <summary>A sale paid in the period that the global invoice leaves out, and why.</summary>
public class GlobalInvoiceLeftOutDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public decimal Total { get; set; }
    public string Reason { get; set; } = null!;
}

/// <summary>
/// The global invoice of a period before it is stamped (Fase 28B, ADR 0046): its sales, the ones left out and why, its
/// amounts and what stops it. What it shows is what «Timbrar» stamps.
/// </summary>
public class GlobalInvoiceDraftDto
{
    public GlobalInvoicePeriodicity Periodicity { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    /// <summary>c_Meses of its InformacionGlobal.</summary>
    public string Months { get; set; } = null!;
    public int Year { get; set; }
    /// <summary>Whether the platform invoices (it has its PAC).</summary>
    public bool Available { get; set; }
    /// <summary>The PAC's test environment: invoices without fiscal effect.</summary>
    public bool Sandbox { get; set; }
    /// <summary>Why the business cannot invoice now (the platform, Mi negocio or its CSD).</summary>
    public string? Problem { get; set; }
    /// <summary>What stops this global invoice (the period, its sales, its form of payment).</summary>
    public List<string> Blockers { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool IsReadyToStamp { get; set; }
    /// <summary>c_FormaPago: the method that paid the most of its sales.</summary>
    public string? PaymentForm { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TotalTransferred { get; set; }
    public decimal Total { get; set; }
    /// <summary>Its transfers, one per IVA rate (exempt ones without rate or amount).</summary>
    public List<CfdiTransferDto> Transfers { get; set; } = [];
    public int SaleCount { get; set; }
    /// <summary>One per sale and IVA rate it charged.</summary>
    public int ConceptCount { get; set; }
    public List<GlobalInvoiceSaleDto> Sales { get; set; } = [];
    public List<GlobalInvoiceLeftOutDto> LeftOut { get; set; } = [];
    /// <summary>
    /// Which sales it takes and for how much, in a few characters: stamping sends it back, and if the period's sales changed
    /// since this preview (even keeping their number and total), nothing is stamped.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>The preview of a global invoice with what stamping it needs: its period, its CFDI and its sales.</summary>
public sealed record GlobalInvoiceDraft(
    GlobalInvoiceDraftDto Dto,
    GlobalInvoicePeriod Period,
    CfdiDocumentIssuer? Issuer,
    string? ExpeditionPostalCode,
    IReadOnlyList<CfdiDocumentConcept> Concepts,
    IReadOnlyList<CfdiDocumentTransfer> Transfers,
    IReadOnlyList<GlobalInvoiceSale> Sales)
{
    /// <summary>
    /// The CFDI of a draft ready to stamp, with its serie, folio and date (local to the business): to the general public,
    /// with the issuer's postal code, and the InformacionGlobal of its period.
    /// </summary>
    public CfdiDocument Document(string serie, long folio, DateTime localDate)
    {
        if (!Dto.IsReadyToStamp || Issuer is null || ExpeditionPostalCode is null)
            throw new InvalidOperationException("Only a global invoice without blockers is stamped.");

        return new CfdiDocument
        {
            Serie = serie,
            Folio = folio.ToString(CultureInfo.InvariantCulture),
            Date = localDate.ToString(CfdiDocument.DateFormat, CultureInfo.InvariantCulture),
            PaymentForm = Dto.PaymentForm!,
            PaymentMethod = CfdiInvoice.PaidInOneExhibition,
            ExpeditionPostalCode = ExpeditionPostalCode,
            SubTotal = Dto.SubTotal,
            TotalTransferred = Dto.TotalTransferred,
            Total = Dto.Total,
            Issuer = Issuer,
            Receiver = new CfdiDocumentReceiver(
                SatCatalog.GeneralPublicRfc,
                SatCatalog.GeneralPublicName,
                ExpeditionPostalCode,
                SatCatalog.GeneralPublicRegime,
                SatCatalog.GeneralPublicUse),
            Concepts = Concepts,
            Transfers = Transfers,
            GlobalInformation = new CfdiDocumentGlobalInformation(Period.PeriodicityCode, Period.Months, Period.Year)
        };
    }
}

/// <summary>
/// Builds the global invoice of a period (Fase 28B, ADR 0046), as the SAT's guide for it says: one concept per sale
/// (ClaveProdServ 01010101, ACT, «Venta», its folio as NoIdentificacion, its subtotal before taxes), with a transfer per IVA
/// rate it charged, worked out from the bases and IVA of its own CFDI draft, so the global invoice adds up to what the sales
/// charged; what a sale charged that is not subject to IVA is a second concept with the same folio. A sale goes in when it
/// was paid in full during the period (the day of its last payment, in the business's time zone), at once (not on credit:
/// those go in a global invoice of their own), is not cancelled, has nothing given back or forgiven (that needs a credit
/// note), has no invoice of its own and is not in another global invoice. A sale paid in an earlier period that already has
/// its global invoice, and could only go in after it was stamped, goes in the next one. FormaPago is the form of its largest
/// sale.
/// </summary>
public static class GlobalInvoiceDrafts
{
    public const string ProductCode = SatCatalog.GenericProductCode;
    public const string UnitCode = "ACT";
    public const string ConceptDescription = "Venta";

    /// <summary>How many years back the SAT takes a global invoice (CFDI40136: the current one and five before it).</summary>
    public const int YearsBack = 5;

    /// <summary>c_RegimenFiscal of the only issuers that issue it every two months (CFDI40132).</summary>
    public const string BimonthlyRegime = "621";

    public const string NoSalesMessage = "No hay ventas por facturar en este periodo.";
    public const string OtherPaymentBlocker =
        "La venta más grande del periodo se cobró con «Otro», que no es una forma de pago del SAT, y la factura global lleva la "
        + "forma de pago de su venta más grande. Revisa con tu contador cómo se cobró esa venta.";
    public const string BimonthlyBlocker =
        "La factura global bimestral es solo para el Régimen de Incorporación Fiscal (621): elige otra periodicidad.";
    public const string LateWarning =
        "El SAT pide emitir la factura global dentro de las 24 horas siguientes al cierre del periodo: esta va tarde. "
        + "Consulta con tu contador si hay que presentarla con alguna aclaración.";

    public static string TooOldMessage(int oldestYear)
        => $"El SAT recibe facturas globales del año en curso y de los {YearsBack} anteriores (desde {oldestYear}).";

    public static string NotEndedMessage(DateOnly to)
        => $"El periodo termina el {to.ToString("d 'de' MMMM 'de' yyyy", Spanish)}: su factura global se emite a partir del día siguiente.";

    public static string OwnInvoiceReason(string number) => $"Tiene su propia factura ({number}).";
    public static string InGlobalReason(string number) => $"Ya está en la factura global {number}.";
    public const string NotPaidReason = "No está pagada por completo: entra en la factura global del periodo en que se termine de pagar.";
    public const string CreditReason =
        "Se vendió a crédito (fiado): el SAT pide reflejar las ventas en parcialidades en una factura global aparte, que TienditApp todavía no genera.";
    public const string RefundedReason = "Tiene reembolsos: lo devuelto requiere una nota de crédito, que TienditApp todavía no genera.";
    public const string WrittenOffReason = "Se condonó parte de ella: lo condonado requiere una nota de crédito, que TienditApp todavía no genera.";
    public const string RefundInCourseReason = "Tiene un reembolso en curso en Mercado Pago.";
    public const string CanStillChangeReason =
        "Todavía puede cambiar (sigue pendiente o es una cuenta abierta): acéptala o ciérrala para que entre en esta factura global; "
        + "si lo haces después de timbrarla, entra en la siguiente que emitas.";

    public static string LateSalesWarning(int count) => count == 1
        ? "Incluye 1 venta de un periodo anterior que no entró en su factura global: se aceptó o se cerró, o se resolvió su factura o su reembolso, después de timbrarla."
        : $"Incluye {count} ventas de periodos anteriores que no entraron en su factura global: se aceptaron o se cerraron, o se resolvió su factura o su reembolso, después de timbrarla.";

    public static string CoveredRefundsWarning(int count) => count == 1
        ? "Incluye 1 venta con devoluciones que ya estaba en una factura global cancelada: va por su total, como la primera; la devolución requiere una nota de crédito."
        : $"Incluye {count} ventas con devoluciones que ya estaban en una factura global cancelada: van por su total, como la primera; las devoluciones requieren una nota de crédito.";
    public const string AmountsReason = "Sus importes no cuadran con lo que cobró: revisa su vista previa del CFDI en Pedidos.";

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>The global invoice of the period of that periodicity that holds <paramref name="day"/>.</summary>
    public static async Task<GlobalInvoiceDraft> BuildAsync(
        IApplicationDbContext context,
        Guid tenantId,
        IInvoicingGateway gateway,
        GlobalInvoicePeriodicity periodicity,
        DateOnly day,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var period = GlobalInvoicePeriod.Containing(periodicity, day);
        var settings = await InvoicingSettings.ReadAsync(context, tenantId, gateway, now, cancellationToken);
        var timeZone = await context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = TenantDateTimeResolver.Resolve(timeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var start = TenantDateTimeResolver.StartOfLocalDayUtc(period.From.ToDateTime(TimeOnly.MinValue), zone);
        var end = TenantDateTimeResolver.StartOfLocalDayUtc(period.To.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);

        var dto = new GlobalInvoiceDraftDto
        {
            Periodicity = period.Periodicity,
            PeriodFrom = period.From,
            PeriodTo = period.To,
            Months = period.Months,
            Year = period.Year,
            Available = settings.Available,
            Sandbox = settings.Sandbox,
            Problem = InvoicingSettings.ReadinessProblem(settings)
        };
        if (!period.EndedBy(today))
            dto.Blockers.Add(NotEndedMessage(period.To));
        else if (now > end.AddHours(24))
            dto.Warnings.Add(LateWarning);
        if (period.Year < today.Year - YearsBack)
            dto.Blockers.Add(TooOldMessage(today.Year - YearsBack));
        if (period.Periodicity == GlobalInvoicePeriodicity.Bimonthly && settings.FiscalRegime != BimonthlyRegime)
            dto.Blockers.Add(BimonthlyBlocker);

        // The sales whose last payment falls in the period: the ones it may cover. Cancelled ones are not sales.
        var paidInPeriod = context.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled
                && o.Payments.Any()
                && o.Payments.Max(p => p.PaidAt) >= start
                && o.Payments.Max(p => p.PaidAt) < end);
        var candidates = await Candidates(paidInPeriod).ToListAsync(cancellationToken);

        // And the ones paid in an earlier period that already has its global invoice, in no live invoice: a sale that could
        // only go in after it was stamped (accepted or closed, its own invoice refused or cancelled, its refund in Mercado Pago
        // turned down) would otherwise stay without a CFDI, so it goes in this one.
        var stampedBefore = (await context.GlobalInvoices.AsNoTracking()
                .Where(g => (g.Status == OrderInvoiceStatus.Stamping || g.Status == OrderInvoiceStatus.Stamped
                        || g.Status == OrderInvoiceStatus.Cancelling)
                    && g.PeriodTo < period.From)
                .Select(g => new { g.PeriodFrom, g.PeriodTo })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(g => (
                From: TenantDateTimeResolver.StartOfLocalDayUtc(g.PeriodFrom.ToDateTime(TimeOnly.MinValue), zone),
                To: TenantDateTimeResolver.StartOfLocalDayUtc(g.PeriodTo.AddDays(1).ToDateTime(TimeOnly.MinValue), zone)))
            .OrderBy(range => range.From)
            .Aggregate(new List<(DateTimeOffset From, DateTimeOffset To)>(), (merged, range) =>
            {
                // Day after day, one stretch.
                if (merged.Count > 0 && range.From <= merged[^1].To)
                    merged[^1] = (merged[^1].From, range.To > merged[^1].To ? range.To : merged[^1].To);
                else
                    merged.Add(range);
                return merged;
            });
        var earliest = stampedBefore.Count == 0 ? start : stampedBefore.Min(range => range.From);
        var paidBefore = context.Orders.AsNoTracking()
            .Where(o => stampedBefore.Count > 0
                && o.Status != OrderStatus.Cancelled
                && o.Status != OrderStatus.Pending
                && (!o.IsTab || o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Completed)
                && !o.SoldOnCredit
                && o.Total > 0
                && o.Total - o.PaidAmount <= 0
                && o.WrittenOffAmount == 0
                && o.Payments.Any()
                && o.Payments.Max(p => p.PaidAt) >= earliest
                && o.Payments.Max(p => p.PaidAt) < start
                && !context.OrderInvoices.Any(i => i.OrderId == o.Id
                    && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                        || i.Status == OrderInvoiceStatus.Cancelling))
                && !context.GlobalInvoiceOrders.Any(g => g.OrderId == o.Id && !g.Released));
        var late = stampedBefore.Count == 0
            ? []
            : (await Candidates(paidBefore).ToListAsync(cancellationToken))
                .Where(candidate => stampedBefore.Any(range => candidate.PaidAt >= range.From && candidate.PaidAt < range.To))
                .ToList();

        var candidateIds = paidInPeriod.Select(o => o.Id).Concat(paidBefore.Select(o => o.Id));
        var ownInvoices = await context.OrderInvoices.AsNoTracking()
            .Where(i => candidateIds.Contains(i.OrderId)
                && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                    || i.Status == OrderInvoiceStatus.Cancelling))
            .Select(i => new { i.OrderId, i.Serie, i.Folio })
            .ToListAsync(cancellationToken);
        var inGlobals = await context.GlobalInvoiceOrders.AsNoTracking()
            .Where(o => !o.Released && candidateIds.Contains(o.OrderId))
            .Join(context.GlobalInvoices.AsNoTracking(), o => o.GlobalInvoiceId, g => g.Id, (o, g) => new { o.OrderId, g.Serie, g.Folio })
            .ToListAsync(cancellationToken);
        var refundsInCourse = await context.OnlinePaymentRefunds.AsNoTracking()
            .Where(r => r.Status == OnlinePaymentRefundStatus.Requested && r.OrderId != null && candidateIds.Contains(r.OrderId.Value))
            .Select(r => r.OrderId!.Value)
            .ToListAsync(cancellationToken);
        // Sales a stamped global invoice covered until it was cancelled (motive 04, a customer asked for theirs): issued again
        // as they were then. A refund since asks for a credit note, not for leaving the sale without a CFDI.
        var coveredBefore = (await context.GlobalInvoiceOrders.AsNoTracking()
            .Where(o => o.Released && candidateIds.Contains(o.OrderId))
            .Join(context.GlobalInvoices.AsNoTracking().Where(g => g.Status == OrderInvoiceStatus.Cancelled),
                o => o.GlobalInvoiceId, g => g.Id, (o, g) => o.OrderId)
            .ToListAsync(cancellationToken)).ToHashSet();
        var ownByOrder = ownInvoices.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => $"{g.First().Serie}-{g.First().Folio}");
        var globalByOrder = inGlobals.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => $"{g.First().Serie}-{g.First().Folio}");
        var refunding = refundsInCourse.ToHashSet();

        var included = new List<(Guid Id, DateTimeOffset PaidAt, bool Late)>();
        var coveredWithRefunds = 0;
        // The earlier periods' first, in the order they were paid.
        foreach (var (candidate, isLate) in late.Select(c => (c, true)).Concat(candidates.Select(c => (c, false))))
        {
            var covered = coveredBefore.Contains(candidate.Id);
            var reason = ownByOrder.TryGetValue(candidate.Id, out var own) ? OwnInvoiceReason(own)
                : globalByOrder.TryGetValue(candidate.Id, out var global) ? InGlobalReason(global)
                : candidate.Total <= 0 ? null
                // What it charges later would stay out of the global invoice and could not be invoiced on its own.
                : Order.TotalCanChange(candidate.Status, candidate.IsTab) ? CanStillChangeReason
                : candidate.WrittenOffAmount > 0 ? WrittenOffReason
                : candidate.Total - candidate.PaidAmount > 0 ? NotPaidReason
                : candidate.SoldOnCredit ? CreditReason
                : candidate.RefundedAmount > 0 && !covered ? RefundedReason
                : refunding.Contains(candidate.Id) && !covered ? RefundInCourseReason
                : string.Empty;
            // A sale without amount is not invoiced, nor listed; one of an earlier period, only when it goes in: why it does
            // not was said in its own period's preview.
            if (reason is null || (isLate && reason.Length > 0))
                continue;
            if (reason.Length == 0 && covered && (candidate.RefundedAmount > 0 || refunding.Contains(candidate.Id)))
                coveredWithRefunds++;
            if (reason.Length == 0)
                included.Add((candidate.Id, candidate.PaidAt, isLate));
            else
                dto.LeftOut.Add(new GlobalInvoiceLeftOutDto
                {
                    OrderId = candidate.Id,
                    OrderNumber = candidate.OrderNumber,
                    Total = candidate.Total,
                    Reason = reason
                });
        }

        var includedIds = included.Select(sale => sale.Id).ToList();
        var orders = included.Count == 0
            ? []
            : await context.Orders.AsNoTracking()
                .Include(o => o.Items).ThenInclude(i => i.Options)
                .Include(o => o.Payments)
                .Where(o => includedIds.Contains(o.Id))
                .ToListAsync(cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        var concepts = new List<CfdiDocumentConcept>();
        var sales = new List<GlobalInvoiceSale>();
        var largest = (Total: 0m, Form: (string?)null);
        var platformSales = 0;
        var lateSales = 0;
        foreach (var (orderId, paidAt, isLate) in included)
        {
            // Read a moment ago; outside a transaction a sale can be cancelled in between, and then it is not a sale.
            if (!ordersById.TryGetValue(orderId, out var order))
                continue;
            var orderConcepts = Concepts(order);
            if (orderConcepts is null && isLate)
                continue;
            if (orderConcepts is null)
            {
                dto.LeftOut.Add(new GlobalInvoiceLeftOutDto
                {
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    Total = order.Total,
                    Reason = AmountsReason
                });
                continue;
            }

            concepts.AddRange(orderConcepts);
            sales.Add(new GlobalInvoiceSale(order.Id, order.OrderNumber, order.Total));
            // The largest sale gives its form of payment; between two as large, one with a form of the SAT.
            var form = SatCatalog.PaymentForm(GetOrderCfdiDraftQueryHandler.MainPaymentMethod(
                order.Payments.Select(payment => (payment.Method, payment.Amount))));
            if (order.Total > largest.Total
                || (order.Total == largest.Total && largest.Form == SatCatalog.PaymentFormToBeDefined))
                largest = (order.Total, form);
            if (order.IsDeliveryPlatformOrder)
                platformSales++;
            if (isLate)
                lateSales++;
            dto.Sales.Add(new GlobalInvoiceSaleDto { OrderId = order.Id, OrderNumber = order.OrderNumber, PaidAt = paidAt, Total = order.Total });
        }

        var transfers = concepts
            .SelectMany(concept => concept.AllTransfers)
            .GroupBy(transfer => (transfer.FactorType, transfer.Rate))
            .Select(group => new CfdiDocumentTransfer(
                group.Sum(transfer => transfer.Base),
                CfdiTransfer.IvaTaxCode,
                group.Key.FactorType,
                group.Key.Rate,
                group.Key.Rate is null ? null : group.Sum(transfer => transfer.Amount ?? 0m)))
            .OrderBy(transfer => transfer.Rate is null)
            .ThenByDescending(transfer => transfer.Rate)
            .ToList();

        dto.SaleCount = sales.Count;
        dto.ConceptCount = concepts.Count;
        dto.SubTotal = concepts.Sum(concept => concept.Amount);
        dto.TotalTransferred = transfers.Sum(transfer => transfer.Amount ?? 0m);
        dto.Total = dto.SubTotal + dto.TotalTransferred;
        dto.Transfers = transfers.Select(transfer => new CfdiTransferDto
        {
            Base = transfer.Base,
            FactorType = transfer.FactorType,
            RateOrQuota = transfer.Rate,
            Amount = transfer.Amount
        }).ToList();

        if (sales.Count == 0)
            dto.Blockers.Add(NoSalesMessage);
        else if (sales.Count > GlobalInvoice.MaxSales)
            dto.Blockers.Add(GlobalInvoice.TooManySalesMessage(sales.Count));
        if (largest.Form is not null)
        {
            dto.PaymentForm = largest.Form;
            if (dto.PaymentForm == SatCatalog.PaymentFormToBeDefined)
                dto.Blockers.Add(OtherPaymentBlocker);
        }
        if (lateSales > 0)
            dto.Warnings.Add(LateSalesWarning(lateSales));
        if (coveredWithRefunds > 0)
            dto.Warnings.Add(CoveredRefundsWarning(coveredWithRefunds));
        if (platformSales > 0)
        {
            dto.Warnings.Add(platformSales == 1
                ? "Incluye 1 pedido de Rappi, Uber Eats o DiDi Food: confirma con tu contador cómo declarar los pedidos de plataformas."
                : $"Incluye {platformSales} pedidos de Rappi, Uber Eats o DiDi Food: confirma con tu contador cómo declarar los pedidos de plataformas.");
        }
        dto.IsReadyToStamp = dto.Problem is null && dto.Blockers.Count == 0;
        dto.Fingerprint = Fingerprint(sales);

        var tenant = await context.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Rfc, t.LegalName, t.TaxRegime, PostalCode = t.Address == null ? null : t.Address.ZipCode })
            .FirstOrDefaultAsync(cancellationToken);
        var issuer = GetOrderCfdiDraftQueryHandler.Issuer(tenant?.Rfc, tenant?.LegalName, tenant?.TaxRegime);
        var postalCode = tenant?.PostalCode?.Trim();
        var issuerReady = !GetOrderCfdiDraftQueryHandler.IssuerBlockers(issuer, postalCode).Any();
        return new GlobalInvoiceDraft(
            dto,
            period,
            issuerReady ? new CfdiDocumentIssuer(issuer.Rfc!, issuer.Name!, issuer.FiscalRegime!) : null,
            issuerReady ? postalCode : null,
            concepts,
            transfers,
            sales);
    }

    /// <summary>A sale the global invoice may cover, with what decides whether it goes in.</summary>
    private sealed class Candidate
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = null!;
        public decimal Total { get; init; }
        public decimal PaidAmount { get; init; }
        public decimal RefundedAmount { get; init; }
        public decimal WrittenOffAmount { get; init; }
        public bool SoldOnCredit { get; init; }
        public OrderStatus Status { get; init; }
        public bool IsTab { get; init; }
        public DateTimeOffset PaidAt { get; init; }
    }

    /// <summary>Those sales, in the order they were paid in full.</summary>
    private static IQueryable<Candidate> Candidates(IQueryable<Order> sales)
        => sales
            .OrderBy(o => o.Payments.Max(p => p.PaidAt))
            .ThenBy(o => o.OrderNumber)
            .Select(o => new Candidate
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                Total = o.Total,
                PaidAmount = o.PaidAmount,
                RefundedAmount = o.RefundedAmount,
                WrittenOffAmount = o.WrittenOffAmount,
                SoldOnCredit = o.SoldOnCredit,
                Status = o.Status,
                IsTab = o.IsTab,
                PaidAt = o.Payments.Max(p => p.PaidAt)
            });

    /// <summary>
    /// The concepts of one sale: its subtotal before taxes, with a transfer per IVA rate it charged (the bases and IVA of its
    /// CFDI draft), and what it charged that is not subject to IVA as a second concept, both with its folio. Null when they
    /// do not add up to what it charged, or an IVA falls outside what the SAT accepts for its base.
    /// </summary>
    public static IReadOnlyList<CfdiDocumentConcept>? Concepts(Order order)
    {
        var amounts = CfdiAmountCalculator.Calculate(order.PricesIncludeTax, GetOrderCfdiDraftQueryHandler.Lines(order));
        if (amounts.Total != order.Total || amounts.Concepts.Count == 0)
            return null;

        var concepts = new List<CfdiDocumentConcept>();
        var subject = amounts.Concepts.Where(concept => concept.Transfer is not null).ToList();
        if (subject.Count > 0)
        {
            var transfers = new List<CfdiDocumentTransfer>();
            foreach (var rate in subject
                .GroupBy(concept => (concept.Transfer!.IsExempt, concept.Transfer.RatePercent))
                .OrderBy(group => group.Key.IsExempt)
                .ThenByDescending(group => group.Key.RatePercent))
            {
                var taxBase = rate.Sum(concept => concept.Transfer!.Base);
                var tax = rate.Sum(concept => concept.Transfer!.Amount);
                // Only free lines at that rate: nothing to transfer, and the SAT takes no base of 0.
                if (taxBase == 0 && tax == 0)
                    continue;
                if (!rate.Key.IsExempt && (tax < CfdiAmountCalculator.MinimumTax(taxBase, rate.Key.RatePercent)
                        || tax > CfdiAmountCalculator.MaximumTax(taxBase, rate.Key.RatePercent)))
                    return null;
                var transfer = new CfdiTransfer(taxBase, rate.Key.IsExempt, rate.Key.RatePercent, tax);
                transfers.Add(new CfdiDocumentTransfer(
                    taxBase, CfdiTransfer.IvaTaxCode, transfer.FactorType, transfer.RateOrQuota, rate.Key.IsExempt ? null : tax));
            }
            if (transfers.Count > 0)
                concepts.Add(Concept(order, subject.Sum(concept => concept.Amount), CfdiTaxObject.Subject, transfers));
        }

        var notSubject = amounts.Concepts.Where(concept => concept.Transfer is null).ToList();
        var notSubjectAmount = notSubject.Sum(concept => concept.Amount);
        if (notSubjectAmount > 0)
            concepts.Add(Concept(order, notSubjectAmount, CfdiTaxObject.NotSubject, null));
        return concepts.Count == 0 ? null : concepts;
    }

    /// <summary>The sales and their totals, in order, as a short hash: two previews with the same one take the same sales.</summary>
    public static string Fingerprint(IEnumerable<GlobalInvoiceSale> sales)
    {
        var lines = sales
            .OrderBy(sale => sale.OrderId)
            .Select(sale => string.Create(CultureInfo.InvariantCulture, $"{sale.OrderId:N}:{sale.Total:0.00}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();
    }

    private static CfdiDocumentConcept Concept(
        Order order, decimal amount, CfdiTaxObject taxObject, IReadOnlyList<CfdiDocumentTransfer>? transfers)
        => new(
            ProductCode,
            UnitCode,
            ConceptDescription,
            1m,
            amount,
            amount,
            SatCatalog.Code(taxObject),
            null,
            order.OrderNumber,
            transfers);
}
