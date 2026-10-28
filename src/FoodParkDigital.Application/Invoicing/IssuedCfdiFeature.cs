using System.Globalization;
using System.IO.Compression;
using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>Which CFDI a row of Reportes › CFDI emitidos is (Fase 28H, ADR 0052).</summary>
public enum IssuedCfdiKind
{
    /// <summary>The invoice of an order (ingreso, Fase 28A).</summary>
    OrderInvoice,

    /// <summary>A global invoice to the general public (ingreso, Fase 28B).</summary>
    GlobalInvoice,

    /// <summary>A payment complement (pago, Fase 28C).</summary>
    PaymentComplement,

    /// <summary>A credit note (egreso, Fase 28D).</summary>
    CreditNote
}

/// <summary>What the download of a month's CFDI carries: their XML with the list, or the list alone.</summary>
public enum IssuedCfdiExportFormat
{
    Zip,
    Csv
}

/// <summary>A CFDI the business issued, as Reportes › CFDI emitidos lists it (Fase 28H, ADR 0052).</summary>
public class IssuedCfdiDto
{
    public Guid Id { get; set; }
    public IssuedCfdiKind Kind { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? Uuid { get; set; }
    public OrderInvoiceStatus Status { get; set; }

    /// <summary>The date the CFDI carries.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset? StampedAt { get; set; }
    public CfdiCancellationMotive? CancellationMotive { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Why the PAC refused it, or why its cancellation did not go through.</summary>
    public string? FailureMessage { get; set; }

    public string ReceiverRfc { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;

    /// <summary>PUE or PPD; a credit note is always PUE, and a complement has none.</summary>
    public string? PaymentMethod { get; set; }

    public string PaymentForm { get; set; } = string.Empty;
    public decimal? SubTotal { get; set; }
    public decimal? TaxTotal { get; set; }

    /// <summary>The total of an invoice or a credit note; a complement's CFDI totals zero and declares a payment instead.</summary>
    public decimal? Total { get; set; }

    /// <summary>What a payment complement declares paid.</summary>
    public decimal? PaidAmount { get; set; }

    /// <summary>The order it is of; a global invoice covers many.</summary>
    public Guid? OrderId { get; set; }

    public string? OrderNumber { get; set; }

    /// <summary>The invoice a complement or a credit note is of, or the one an invoice replaces (Fase 28G).</summary>
    public string? RelatedNumber { get; set; }

    /// <summary>The invoice that replaces this one (Fase 28G).</summary>
    public string? ReplacedByNumber { get; set; }

    public GlobalInvoicePeriodicity? Periodicity { get; set; }
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public int? OrderCount { get; set; }

    /// <summary>Whether its stamped XML is kept: it was stamped, even if it was cancelled since.</summary>
    public bool HasXml { get; set; }
}

/// <summary>What the CFDI of a month add up to, by kind (Fase 28H).</summary>
public class IssuedCfdiSummaryDto
{
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>The month's first and last day in the business's time zone.</summary>
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }
    public List<IssuedCfdiKindSummaryDto> Kinds { get; set; } = [];
}

public class IssuedCfdiKindSummaryDto
{
    public IssuedCfdiKind Kind { get; set; }

    /// <summary>
    /// The ones in force: stamped, and those whose cancellation waits for the customer (the SAT has them in force until it is
    /// accepted).
    /// </summary>
    public int InForce { get; set; }

    /// <summary>What the ones in force add up to (a complement's CFDI has no subtotal nor taxes of its own: zero).</summary>
    public decimal SubTotal { get; set; }

    public decimal TaxTotal { get; set; }

    /// <summary>The total of the invoices and credit notes in force, or what the complements in force declare paid.</summary>
    public decimal Total { get; set; }

    public int Cancelling { get; set; }
    public int Cancelled { get; set; }
    public int Stamping { get; set; }
    public int Refused { get; set; }
}

/// <summary>
/// The CFDI the business issued with a date in a month, in its time zone, the newest first (owners and administrators):
/// invoices of orders, global invoices, payment complements and credit notes, in any state. A refused one explains a gap in
/// the folios. The search finds one by its folio (F-12, or 12), its UUID, its receiver's RFC or name, or its order's folio.
/// </summary>
[RequiresTenantManager]
public record GetIssuedCfdisQuery(
    int Year,
    int Month,
    IssuedCfdiKind? Kind = null,
    OrderInvoiceStatus? Status = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<PaginatedList<IssuedCfdiDto>>;

public class GetIssuedCfdisQueryValidator : AbstractValidator<GetIssuedCfdisQuery>
{
    public const int MaxSearchLength = 100;

    public GetIssuedCfdisQueryValidator()
    {
        IssuedCfdis.RuleForMonth(this, x => x.Year, x => x.Month);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Search).MaximumLength(MaxSearchLength);
    }
}

public class GetIssuedCfdisQueryHandler : IRequestHandler<GetIssuedCfdisQuery, PaginatedList<IssuedCfdiDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetIssuedCfdisQueryHandler(IApplicationDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<PaginatedList<IssuedCfdiDto>> Handle(GetIssuedCfdisQuery request, CancellationToken cancellationToken)
    {
        var month = await IssuedCfdis.MonthAsync(_context, _tenant.TenantId, request.Year, request.Month, cancellationToken);
        var rows = IssuedCfdis.Search(_context, IssuedCfdis.Rows(_context, month, request.Kind, request.Status), request.Search);
        var page = await PaginatedList<IssuedCfdiRow>.CreateAsync(
            rows.OrderByDescending(r => r.IssuedAt).ThenBy(r => r.Serie).ThenByDescending(r => r.Folio).ThenBy(r => r.Id),
            request.PageNumber,
            request.PageSize,
            cancellationToken);
        return new PaginatedList<IssuedCfdiDto>(
            page.Items.Select(IssuedCfdis.ToDto).ToList(), page.TotalCount, request.PageNumber, request.PageSize);
    }
}

/// <summary>What the CFDI of a month add up to, by kind (owners and administrators).</summary>
[RequiresTenantManager]
public record GetIssuedCfdiSummaryQuery(int Year, int Month) : IRequest<IssuedCfdiSummaryDto>;

public class GetIssuedCfdiSummaryQueryValidator : AbstractValidator<GetIssuedCfdiSummaryQuery>
{
    public GetIssuedCfdiSummaryQueryValidator()
    {
        IssuedCfdis.RuleForMonth(this, x => x.Year, x => x.Month);
    }
}

public class GetIssuedCfdiSummaryQueryHandler : IRequestHandler<GetIssuedCfdiSummaryQuery, IssuedCfdiSummaryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetIssuedCfdiSummaryQueryHandler(IApplicationDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<IssuedCfdiSummaryDto> Handle(GetIssuedCfdiSummaryQuery request, CancellationToken cancellationToken)
    {
        var month = await IssuedCfdis.MonthAsync(_context, _tenant.TenantId, request.Year, request.Month, cancellationToken);

        // Added up here: a month holds few enough, and the amounts are decimal on every provider.
        var amounts = new List<(IssuedCfdiKind Kind, OrderInvoiceStatus Status, decimal SubTotal, decimal TaxTotal, decimal Total)>();
        amounts.AddRange((await _context.OrderInvoices.AsNoTracking()
                .Where(i => i.IssuedAt >= month.Start && i.IssuedAt < month.End)
                .Select(i => new { i.Status, i.SubTotal, i.TaxTotal, i.Total })
                .ToListAsync(cancellationToken))
            .Select(i => (IssuedCfdiKind.OrderInvoice, i.Status, i.SubTotal, i.TaxTotal, i.Total)));
        amounts.AddRange((await _context.GlobalInvoices.AsNoTracking()
                .Where(i => i.IssuedAt >= month.Start && i.IssuedAt < month.End)
                .Select(i => new { i.Status, i.SubTotal, i.TaxTotal, i.Total })
                .ToListAsync(cancellationToken))
            .Select(i => (IssuedCfdiKind.GlobalInvoice, i.Status, i.SubTotal, i.TaxTotal, i.Total)));
        amounts.AddRange((await _context.PaymentComplements.AsNoTracking()
                .Where(c => c.IssuedAt >= month.Start && c.IssuedAt < month.End)
                .Select(c => new { c.Status, c.Amount })
                .ToListAsync(cancellationToken))
            .Select(c => (IssuedCfdiKind.PaymentComplement, c.Status, 0m, 0m, c.Amount)));
        amounts.AddRange((await _context.CreditNotes.AsNoTracking()
                .Where(n => n.IssuedAt >= month.Start && n.IssuedAt < month.End)
                .Select(n => new { n.Status, n.SubTotal, n.TaxTotal, n.Total })
                .ToListAsync(cancellationToken))
            .Select(n => (IssuedCfdiKind.CreditNote, n.Status, n.SubTotal, n.TaxTotal, n.Total)));

        return new IssuedCfdiSummaryDto
        {
            Year = request.Year,
            Month = request.Month,
            From = month.FirstDay,
            To = month.LastDay,
            Kinds = Enum.GetValues<IssuedCfdiKind>().Select(kind =>
            {
                var ofKind = amounts.Where(a => a.Kind == kind).ToList();
                var inForce = ofKind.Where(a => a.Status is OrderInvoiceStatus.Stamped or OrderInvoiceStatus.Cancelling).ToList();
                return new IssuedCfdiKindSummaryDto
                {
                    Kind = kind,
                    InForce = inForce.Count,
                    SubTotal = inForce.Sum(a => a.SubTotal),
                    TaxTotal = inForce.Sum(a => a.TaxTotal),
                    Total = inForce.Sum(a => a.Total),
                    Cancelling = ofKind.Count(a => a.Status == OrderInvoiceStatus.Cancelling),
                    Cancelled = ofKind.Count(a => a.Status == OrderInvoiceStatus.Cancelled),
                    Stamping = ofKind.Count(a => a.Status == OrderInvoiceStatus.Stamping),
                    Refused = ofKind.Count(a => a.Status == OrderInvoiceStatus.Refused)
                };
            }).ToList()
        };
    }
}

/// <summary>
/// The month's CFDI for the accountant (owners and administrators): a ZIP with the stamped XML of each one that was stamped
/// (in force, cancelling or cancelled), by kind, and the list of all of them as CSV; or the list alone.
/// </summary>
[RequiresTenantManager]
public record ExportIssuedCfdisQuery(int Year, int Month, IssuedCfdiExportFormat Format = IssuedCfdiExportFormat.Zip)
    : IRequest<Result<PrivateFileDownload>>;

public class ExportIssuedCfdisQueryValidator : AbstractValidator<ExportIssuedCfdisQuery>
{
    public ExportIssuedCfdisQueryValidator()
    {
        IssuedCfdis.RuleForMonth(this, x => x.Year, x => x.Month);
        RuleFor(x => x.Format).IsInEnum();
    }
}

public class ExportIssuedCfdisQueryHandler : IRequestHandler<ExportIssuedCfdisQuery, Result<PrivateFileDownload>>
{
    /// <summary>The CFDI one download holds: their XML and documents are read into memory.</summary>
    public const int MaxCount = 5_000;

    /// <summary>What the list says of a stamped CFDI whose XML is not in the private files.</summary>
    public const string MissingXml = "No se encontró";

    // How many XML files are read from the private files at a time.
    private const int ReadBatchSize = 8;

    public static string TooManyMessage(int limit = MaxCount) =>
        $"El mes tiene más de {limit:N0} CFDI, los que junta una descarga: pide los XML del mes con la descarga masiva del portal del SAT.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IPrivateFileStorage _storage;
    private readonly ILogger<ExportIssuedCfdisQueryHandler> _logger;

    public ExportIssuedCfdisQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        IPrivateFileStorage storage,
        ILogger<ExportIssuedCfdisQueryHandler> logger)
    {
        _context = context;
        _tenant = tenant;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>The most CFDI a download takes (a test lowers it).</summary>
    internal int Limit { get; init; } = MaxCount;

    public async Task<Result<PrivateFileDownload>> Handle(ExportIssuedCfdisQuery request, CancellationToken cancellationToken)
    {
        var month = await IssuedCfdis.MonthAsync(_context, _tenant.TenantId, request.Year, request.Month, cancellationToken);
        // In the order the accountant reads a month: by date, then by folio. One more than the limit says it is past it.
        var ordered = await IssuedCfdis.Rows(_context, month, kind: null, status: null)
            .OrderBy(r => r.IssuedAt).ThenBy(r => r.Serie).ThenBy(r => r.Folio).ThenBy(r => r.Id)
            .Take(Limit + 1)
            .ToListAsync(cancellationToken);
        if (ordered.Count > Limit)
            return Result.Failure<PrivateFileDownload>(TooManyMessage(Limit));
        var documents = await IssuedCfdis.DocumentsAsync(_context, month, cancellationToken);
        var name = $"CFDI_{request.Year:0000}-{request.Month:00}";

        if (request.Format == IssuedCfdiExportFormat.Csv)
        {
            var csv = IssuedCfdiCsv.Write(ordered, documents, month.Zone, xmlPaths: null);
            return Result.Success(new PrivateFileDownload(new MemoryStream(csv), "text/csv; charset=utf-8", name + ".csv"));
        }

        var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        {
            var paths = new Dictionary<Guid, string?>();
            foreach (var batch in ordered.Where(r => r.XmlFileKey is not null).Chunk(ReadBatchSize))
            {
                var files = await Task.WhenAll(batch.Select(async row => (Row: row, Xml: await ReadAsync(row, cancellationToken))));
                foreach (var (row, xml) in files)
                {
                    if (xml is null)
                    {
                        paths[row.Id] = null;
                        continue;
                    }

                    var path = IssuedCfdiCsv.XmlPath(row);
                    var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                    await using (var entryStream = entry.Open())
                        await entryStream.WriteAsync(xml, cancellationToken);
                    paths[row.Id] = path;
                }
            }

            // The list goes last: it says which XML is in the file and which was not found.
            var csvEntry = zip.CreateEntry(name + ".csv", CompressionLevel.Optimal);
            await using var csvStream = csvEntry.Open();
            await csvStream.WriteAsync(IssuedCfdiCsv.Write(ordered, documents, month.Zone, paths), cancellationToken);
        }

        zipped.Position = 0;
        return Result.Success(new PrivateFileDownload(zipped, "application/zip", name + ".zip"));
    }

    private async Task<byte[]?> ReadAsync(IssuedCfdiRow row, CancellationToken cancellationToken)
    {
        await using var stream = await _storage.OpenReadAsync(row.XmlFileKey!, cancellationToken);
        if (stream is null)
        {
            _logger.LogWarning("The stamped XML of {Kind} {CfdiId} is not in the private files", row.Kind, row.Id);
            return null;
        }

        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }
}

/// <summary>A CFDI of the month as the four tables give it, before it is shown or written.</summary>
internal sealed class IssuedCfdiRow
{
    public Guid Id { get; init; }
    public IssuedCfdiKind Kind { get; init; }
    public string Serie { get; init; } = null!;
    public long Folio { get; init; }
    public string? Uuid { get; init; }
    public OrderInvoiceStatus Status { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? StampedAt { get; init; }
    public CfdiCancellationMotive? CancellationMotive { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public string? FailureMessage { get; init; }
    public string ReceiverRfc { get; init; } = null!;
    public string ReceiverName { get; init; } = null!;
    public string? PaymentMethod { get; init; }
    public string PaymentForm { get; init; } = null!;
    public decimal? SubTotal { get; init; }
    public decimal? TaxTotal { get; init; }
    public decimal? Total { get; init; }
    public decimal? PaidAmount { get; init; }
    public Guid? OrderId { get; init; }
    public string? OrderNumber { get; init; }
    public string? RelatedSerie { get; init; }
    public long? RelatedFolio { get; init; }
    public string? RelatedUuid { get; init; }
    public string? ReplacedBySerie { get; init; }
    public long? ReplacedByFolio { get; init; }
    public GlobalInvoicePeriodicity? Periodicity { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public int? OrderCount { get; init; }
    public string? XmlFileKey { get; init; }
}

/// <summary>A month in the business's time zone: its days and the instants it starts and ends.</summary>
internal sealed record IssuedCfdiMonth(DateOnly FirstDay, DateOnly LastDay, DateTimeOffset Start, DateTimeOffset End, TimeZoneInfo Zone);

/// <summary>The CFDI of a month, from the four tables that keep them (Fase 28H).</summary>
internal static class IssuedCfdis
{
    // CFDI 4.0 is from 2022; nothing is issued through TienditApp before.
    public const int FirstYear = 2022;
    public const int LastYear = 2100;
    public const string MonthMessage = "Elige un mes desde enero de 2022.";

    public static void RuleForMonth<T>(
        AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, int>> year,
        System.Linq.Expressions.Expression<Func<T, int>> month)
    {
        validator.RuleFor(year).InclusiveBetween(FirstYear, LastYear).WithMessage(MonthMessage);
        validator.RuleFor(month).InclusiveBetween(1, 12).WithMessage(MonthMessage);
    }

    public static async Task<IssuedCfdiMonth> MonthAsync(
        IApplicationDbContext context, Guid tenantId, int year, int month, CancellationToken cancellationToken)
    {
        var timeZone = await context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = TenantDateTimeResolver.Resolve(timeZone);
        var first = new DateOnly(year, month, 1);
        var next = first.AddMonths(1);
        return new IssuedCfdiMonth(
            first,
            next.AddDays(-1),
            TenantDateTimeResolver.StartOfLocalDayUtc(first.ToDateTime(TimeOnly.MinValue), zone),
            TenantDateTimeResolver.StartOfLocalDayUtc(next.ToDateTime(TimeOnly.MinValue), zone),
            zone);
    }

    /// <summary>
    /// The month's CFDI of that kind and state (any, if null) as one query: each table with its own filter by business, joined
    /// with UNION ALL. Each projection names the same members in the same order, as a set operation needs.
    /// </summary>
    public static IQueryable<IssuedCfdiRow> Rows(
        IApplicationDbContext context, IssuedCfdiMonth month, IssuedCfdiKind? kind, OrderInvoiceStatus? status)
    {
        var parts = new List<IQueryable<IssuedCfdiRow>>();
        if (kind is null or IssuedCfdiKind.OrderInvoice)
            parts.Add(OrderInvoices(context, month, status));
        if (kind is null or IssuedCfdiKind.GlobalInvoice)
            parts.Add(GlobalInvoices(context, month, status));
        if (kind is null or IssuedCfdiKind.PaymentComplement)
            parts.Add(PaymentComplements(context, month, status));
        if (kind is null or IssuedCfdiKind.CreditNote)
            parts.Add(CreditNotes(context, month, status));
        return parts.Skip(1).Aggregate(parts[0], (all, part) => all.Concat(part));
    }

    public static IQueryable<IssuedCfdiRow> Search(IApplicationDbContext context, IQueryable<IssuedCfdiRow> rows, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return rows;

        // UUIDs, RFCs, receivers' names, series and folios of orders are kept in capitals.
        var upper = search.Trim().ToUpperInvariant();
        var dash = upper.LastIndexOf('-');
        var serie = dash > 0 ? upper[..dash] : null;
        long? folio = long.TryParse(upper[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
        // A global invoice is found by the folio of any sale it covers, as in Reportes › Factura global.
        var globalOrders = context.GlobalInvoiceOrders.AsNoTracking();
        return rows.Where(r =>
            (folio != null && r.Folio == folio && (serie == null || r.Serie == serie))
            || r.Uuid == upper
            || r.ReceiverRfc == upper
            || r.OrderNumber == upper
            || (r.Kind == IssuedCfdiKind.GlobalInvoice && globalOrders.Any(o => o.GlobalInvoiceId == r.Id && o.OrderNumber == upper))
            || r.ReceiverName.Contains(upper));
    }

    public static IssuedCfdiDto ToDto(IssuedCfdiRow row) => new()
    {
        Id = row.Id,
        Kind = row.Kind,
        Number = $"{row.Serie}-{row.Folio}",
        Uuid = row.Uuid,
        Status = row.Status,
        IssuedAt = row.IssuedAt,
        StampedAt = row.StampedAt,
        CancellationMotive = row.CancellationMotive,
        CancelledAt = row.CancelledAt,
        FailureMessage = row.FailureMessage,
        ReceiverRfc = row.ReceiverRfc,
        ReceiverName = row.ReceiverName,
        PaymentMethod = row.PaymentMethod,
        PaymentForm = row.PaymentForm,
        SubTotal = row.SubTotal,
        TaxTotal = row.TaxTotal,
        Total = row.Total,
        PaidAmount = row.PaidAmount,
        OrderId = row.OrderId,
        OrderNumber = row.OrderNumber,
        RelatedNumber = row.RelatedSerie is null ? null : $"{row.RelatedSerie}-{row.RelatedFolio}",
        ReplacedByNumber = row.ReplacedBySerie is null ? null : $"{row.ReplacedBySerie}-{row.ReplacedByFolio}",
        Periodicity = row.Periodicity,
        PeriodFrom = row.PeriodFrom,
        PeriodTo = row.PeriodTo,
        OrderCount = row.OrderCount,
        HasXml = row.XmlFileKey is not null
    };

    /// <summary>
    /// The CFDI each one of the month asked the PAC for (what the CSV says of its receiver and date). A global invoice's is
    /// left out: it can carry 2,000 concepts, and its receiver is always the general public with the use S01.
    /// </summary>
    public static async Task<Dictionary<Guid, string>> DocumentsAsync(
        IApplicationDbContext context, IssuedCfdiMonth month, CancellationToken cancellationToken)
    {
        var documents = new Dictionary<Guid, string>();
        foreach (var cfdi in await context.OrderInvoices.AsNoTracking()
                     .Where(i => i.IssuedAt >= month.Start && i.IssuedAt < month.End)
                     .Select(i => new { i.Id, i.Document }).ToListAsync(cancellationToken))
            documents[cfdi.Id] = cfdi.Document;
        foreach (var cfdi in await context.PaymentComplements.AsNoTracking()
                     .Where(c => c.IssuedAt >= month.Start && c.IssuedAt < month.End)
                     .Select(c => new { c.Id, c.Document }).ToListAsync(cancellationToken))
            documents[cfdi.Id] = cfdi.Document;
        foreach (var cfdi in await context.CreditNotes.AsNoTracking()
                     .Where(n => n.IssuedAt >= month.Start && n.IssuedAt < month.End)
                     .Select(n => new { n.Id, n.Document }).ToListAsync(cancellationToken))
            documents[cfdi.Id] = cfdi.Document;
        return documents;
    }

    private static IQueryable<IssuedCfdiRow> OrderInvoices(IApplicationDbContext context, IssuedCfdiMonth month, OrderInvoiceStatus? status)
    {
        var invoices = context.OrderInvoices.AsNoTracking().Where(i => i.IssuedAt >= month.Start && i.IssuedAt < month.End);
        if (status is { } only)
            invoices = invoices.Where(i => i.Status == only);
        // The one it replaces and the one that replaces it (Fase 28G): at most one each.
        return from i in invoices
               join o in context.Orders.AsNoTracking() on i.OrderId equals o.Id into orders
               from o in orders.DefaultIfEmpty()
               join r in context.OrderInvoices.AsNoTracking() on (Guid?)i.Id equals r.ReplacedByInvoiceId into replacedOnes
               from r in replacedOnes.DefaultIfEmpty()
               join b in context.OrderInvoices.AsNoTracking() on i.ReplacedByInvoiceId equals (Guid?)b.Id into replacements
               from b in replacements.DefaultIfEmpty()
               select new IssuedCfdiRow
               {
                   Id = i.Id,
                   Kind = IssuedCfdiKind.OrderInvoice,
                   Serie = i.Serie,
                   Folio = i.Folio,
                   Uuid = i.Uuid,
                   Status = i.Status,
                   IssuedAt = i.IssuedAt,
                   StampedAt = i.StampedAt,
                   CancellationMotive = i.CancellationMotive,
                   CancelledAt = i.CancelledAt,
                   FailureMessage = i.FailureMessage,
                   ReceiverRfc = i.Receiver.Rfc,
                   ReceiverName = i.Receiver.Name,
                   PaymentMethod = i.PaymentMethod,
                   PaymentForm = i.PaymentForm,
                   SubTotal = i.SubTotal,
                   TaxTotal = i.TaxTotal,
                   Total = i.Total,
                   PaidAmount = null,
                   OrderId = i.OrderId,
                   OrderNumber = o.OrderNumber,
                   RelatedSerie = r.Serie,
                   RelatedFolio = (long?)r.Folio,
                   RelatedUuid = r.Uuid,
                   ReplacedBySerie = b.Serie,
                   ReplacedByFolio = (long?)b.Folio,
                   Periodicity = null,
                   PeriodFrom = null,
                   PeriodTo = null,
                   OrderCount = null,
                   XmlFileKey = i.XmlFileKey
               };
    }

    private static IQueryable<IssuedCfdiRow> GlobalInvoices(IApplicationDbContext context, IssuedCfdiMonth month, OrderInvoiceStatus? status)
    {
        var invoices = context.GlobalInvoices.AsNoTracking().Where(i => i.IssuedAt >= month.Start && i.IssuedAt < month.End);
        if (status is { } only)
            invoices = invoices.Where(i => i.Status == only);
        return invoices.Select(g => new IssuedCfdiRow
        {
            Id = g.Id,
            Kind = IssuedCfdiKind.GlobalInvoice,
            Serie = g.Serie,
            Folio = g.Folio,
            Uuid = g.Uuid,
            Status = g.Status,
            IssuedAt = g.IssuedAt,
            StampedAt = g.StampedAt,
            CancellationMotive = g.CancellationMotive,
            CancelledAt = g.CancelledAt,
            FailureMessage = g.FailureMessage,
            ReceiverRfc = SatCatalog.GeneralPublicRfc,
            ReceiverName = SatCatalog.GeneralPublicName,
            PaymentMethod = g.PaymentMethod,
            PaymentForm = g.PaymentForm,
            SubTotal = g.SubTotal,
            TaxTotal = g.TaxTotal,
            Total = g.Total,
            PaidAmount = null,
            OrderId = null,
            OrderNumber = null,
            RelatedSerie = null,
            RelatedFolio = null,
            RelatedUuid = null,
            ReplacedBySerie = null,
            ReplacedByFolio = null,
            Periodicity = g.Periodicity,
            PeriodFrom = g.PeriodFrom,
            PeriodTo = g.PeriodTo,
            OrderCount = g.OrderCount,
            XmlFileKey = g.XmlFileKey
        });
    }

    private static IQueryable<IssuedCfdiRow> PaymentComplements(IApplicationDbContext context, IssuedCfdiMonth month, OrderInvoiceStatus? status)
    {
        var complements = context.PaymentComplements.AsNoTracking().Where(c => c.IssuedAt >= month.Start && c.IssuedAt < month.End);
        if (status is { } only)
            complements = complements.Where(c => c.Status == only);
        // The receiver is its invoice's: a complement copies it.
        return from c in complements
               join i in context.OrderInvoices.AsNoTracking() on c.OrderInvoiceId equals i.Id
               join o in context.Orders.AsNoTracking() on c.OrderId equals o.Id into orders
               from o in orders.DefaultIfEmpty()
               select new IssuedCfdiRow
               {
                   Id = c.Id,
                   Kind = IssuedCfdiKind.PaymentComplement,
                   Serie = c.Serie,
                   Folio = c.Folio,
                   Uuid = c.Uuid,
                   Status = c.Status,
                   IssuedAt = c.IssuedAt,
                   StampedAt = c.StampedAt,
                   CancellationMotive = c.CancellationMotive,
                   CancelledAt = c.CancelledAt,
                   FailureMessage = c.FailureMessage,
                   ReceiverRfc = i.Receiver.Rfc,
                   ReceiverName = i.Receiver.Name,
                   PaymentMethod = null,
                   PaymentForm = c.PaymentForm,
                   SubTotal = null,
                   TaxTotal = null,
                   Total = null,
                   PaidAmount = c.Amount,
                   OrderId = c.OrderId,
                   OrderNumber = o.OrderNumber,
                   RelatedSerie = i.Serie,
                   RelatedFolio = (long?)i.Folio,
                   RelatedUuid = i.Uuid,
                   ReplacedBySerie = null,
                   ReplacedByFolio = null,
                   Periodicity = null,
                   PeriodFrom = null,
                   PeriodTo = null,
                   OrderCount = null,
                   XmlFileKey = c.XmlFileKey
               };
    }

    private static IQueryable<IssuedCfdiRow> CreditNotes(IApplicationDbContext context, IssuedCfdiMonth month, OrderInvoiceStatus? status)
    {
        var notes = context.CreditNotes.AsNoTracking().Where(n => n.IssuedAt >= month.Start && n.IssuedAt < month.End);
        if (status is { } only)
            notes = notes.Where(n => n.Status == only);
        // The receiver is its invoice's, as the credit note's; a global invoice's is to the general public (Fase 28I).
        return from n in notes
               join i in context.OrderInvoices.AsNoTracking() on n.OrderInvoiceId equals (Guid?)i.Id into invoices
               from i in invoices.DefaultIfEmpty()
               join g in context.GlobalInvoices.AsNoTracking() on n.GlobalInvoiceId equals (Guid?)g.Id into globals
               from g in globals.DefaultIfEmpty()
               join o in context.Orders.AsNoTracking() on n.OrderId equals o.Id into orders
               from o in orders.DefaultIfEmpty()
               select new IssuedCfdiRow
               {
                   Id = n.Id,
                   Kind = IssuedCfdiKind.CreditNote,
                   Serie = n.Serie,
                   Folio = n.Folio,
                   Uuid = n.Uuid,
                   Status = n.Status,
                   IssuedAt = n.IssuedAt,
                   StampedAt = n.StampedAt,
                   CancellationMotive = n.CancellationMotive,
                   CancelledAt = n.CancelledAt,
                   FailureMessage = n.FailureMessage,
                   ReceiverRfc = n.GlobalInvoiceId != null ? SatCatalog.GeneralPublicRfc : i.Receiver.Rfc,
                   ReceiverName = n.GlobalInvoiceId != null ? SatCatalog.GeneralPublicName : i.Receiver.Name,
                   // A credit note is paid in one exhibition: its CFDI says so.
                   PaymentMethod = CfdiInvoice.PaidInOneExhibition,
                   PaymentForm = n.PaymentForm,
                   SubTotal = n.SubTotal,
                   TaxTotal = n.TaxTotal,
                   Total = n.Total,
                   PaidAmount = null,
                   OrderId = n.OrderId,
                   OrderNumber = o.OrderNumber,
                   RelatedSerie = n.GlobalInvoiceId != null ? g.Serie : i.Serie,
                   RelatedFolio = n.GlobalInvoiceId != null ? (long?)g.Folio : (long?)i.Folio,
                   RelatedUuid = n.GlobalInvoiceId != null ? g.Uuid : i.Uuid,
                   ReplacedBySerie = null,
                   ReplacedByFolio = null,
                   Periodicity = null,
                   PeriodFrom = null,
                   PeriodTo = null,
                   OrderCount = null,
                   XmlFileKey = n.XmlFileKey
               };
    }
}

/// <summary>The list of a month's CFDI as the accountant opens it in a spreadsheet (Fase 28H).</summary>
internal static class IssuedCfdiCsv
{
    public static readonly string[] Header =
    [
        "Tipo de comprobante", "Documento", "Serie", "Folio", "UUID", "Fecha", "Fecha de timbrado", "Estado",
        "Fecha de cancelación", "Motivo de cancelación", "RFC del receptor", "Nombre del receptor", "Uso del CFDI",
        "Método de pago", "Forma de pago", "Subtotal", "Impuestos trasladados", "Total", "Monto pagado", "CFDI relacionado",
        "Pedido", "Archivo XML"
    ];

    /// <summary>
    /// UTF-8 with its mark (so Excel reads the accents), commas and a decimal point. <paramref name="xmlPaths"/> are the files
    /// the ZIP holds (null when the list goes alone).
    /// </summary>
    public static byte[] Write(
        IReadOnlyList<IssuedCfdiRow> rows, IReadOnlyDictionary<Guid, string> documents, TimeZoneInfo zone,
        IReadOnlyDictionary<Guid, string?>? xmlPaths)
    {
        var csv = new StringBuilder();
        AppendLine(csv, Header);
        foreach (var row in rows)
        {
            var document = documents.TryGetValue(row.Id, out var json) ? CfdiDocument.FromJson(json) : null;
            string xml;
            if (row.XmlFileKey is null)
                xml = string.Empty;
            else if (xmlPaths is null)
                xml = Path.GetFileName(XmlPath(row));
            else
                xml = xmlPaths.TryGetValue(row.Id, out var path) && path is not null ? path : ExportIssuedCfdisQueryHandler.MissingXml;
            AppendLine(csv,
            [
                VoucherType(row.Kind),
                KindLabel(row.Kind),
                row.Serie,
                row.Folio.ToString(CultureInfo.InvariantCulture),
                row.Uuid,
                document?.Date.Replace('T', ' ') ?? Local(row.IssuedAt, zone),
                Local(row.StampedAt, zone),
                StatusLabel(row.Status),
                Local(row.CancelledAt, zone),
                row.CancellationMotive is { } motive ? ((int)motive).ToString("00", CultureInfo.InvariantCulture) : null,
                document?.Receiver.Rfc ?? row.ReceiverRfc,
                document?.Receiver.Name ?? row.ReceiverName,
                document?.Receiver.CfdiUse ?? (row.Kind == IssuedCfdiKind.GlobalInvoice ? SatCatalog.GeneralPublicUse : null),
                document?.PaymentMethod ?? row.PaymentMethod,
                row.PaymentForm,
                Money(row.SubTotal),
                Money(row.TaxTotal),
                Money(row.Total),
                Money(row.PaidAmount),
                row.RelatedUuid,
                row.OrderNumber,
                xml
            ]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>
    /// Where a stamped XML goes in the ZIP: by kind, named by its folio and UUID. A cancelled one goes in `cancelados/` of its
    /// kind, so a folder of the ones in force can be taken whole; one whose cancellation waits is still in force.
    /// </summary>
    public static string XmlPath(IssuedCfdiRow row)
    {
        var folder = row.Kind switch
        {
            IssuedCfdiKind.PaymentComplement => "pagos",
            IssuedCfdiKind.CreditNote => "egresos",
            _ => "ingresos"
        };
        if (row.Status == OrderInvoiceStatus.Cancelled)
            folder += "/cancelados";
        return $"{folder}/{SafeName(row.Serie)}-{row.Folio}_{SafeName(row.Uuid ?? row.Id.ToString())}.xml";
    }

    public static string VoucherType(IssuedCfdiKind kind) => kind switch
    {
        IssuedCfdiKind.PaymentComplement => "P",
        IssuedCfdiKind.CreditNote => "E",
        _ => "I"
    };

    public static string KindLabel(IssuedCfdiKind kind) => kind switch
    {
        IssuedCfdiKind.OrderInvoice => "Factura",
        IssuedCfdiKind.GlobalInvoice => "Factura global",
        IssuedCfdiKind.PaymentComplement => "Complemento de pago",
        _ => "Nota de crédito"
    };

    /// <summary>As the SAT reads a CFDI's state; the two before it is stamped are TienditApp's.</summary>
    public static string StatusLabel(OrderInvoiceStatus status) => status switch
    {
        OrderInvoiceStatus.Stamping => "Timbrando",
        OrderInvoiceStatus.Stamped => "Vigente",
        OrderInvoiceStatus.Refused => "Rechazado por el PAC",
        OrderInvoiceStatus.Cancelling => "En proceso de cancelación",
        _ => "Cancelado"
    };

    private static string? Local(DateTimeOffset? instant, TimeZoneInfo zone)
        => instant is { } value
            ? TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : null;

    private static string? Money(decimal? amount) => amount?.ToString("0.00", CultureInfo.InvariantCulture);

    // Series are letters, and UUIDs hex with dashes; anything else would not name a file inside the ZIP.
    private static string SafeName(string value)
        => new(value.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_').ToArray());

    private static void AppendLine(StringBuilder csv, IReadOnlyList<string?> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
                csv.Append(',');
            csv.Append(Cell(cells[i]));
        }

        csv.Append("\r\n");
    }

    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        // A spreadsheet would run a cell that starts like a formula: it goes as text.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
