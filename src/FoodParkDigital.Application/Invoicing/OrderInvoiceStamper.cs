using System.Text;
using System.Xml.Linq;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// Asks the PAC for an invoice that is «Timbrando» (Fase 28A, ADR 0045; the global ones too, Fase 28B, and the payment
/// complements, Fase 28C), always with the CFDI
/// and the key saved when it was requested, so a repeated attempt (the person, the worker, both at once) gets back the same
/// stamp: its XML is kept in the business's private files and the invoice becomes «Timbrada»; a refusal for good makes it
/// «Rechazada»; without an answer it stays «Timbrando». Its PDF is kept when the PAC gives it; if not, it is rendered when
/// downloaded. The email of a stamped CFDI of an order's invoice to its customer is asked for with the stamp (Fase 28E).
/// </summary>
public sealed class OrderInvoiceStamper
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly IPrivateFileStorage _storage;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<OrderInvoiceStamper> _logger;

    public OrderInvoiceStamper(
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _context = context;
        _gateway = gateway;
        _storage = storage;
        _clock = clock;
        _logger = logger;
    }

    public const string LateRefusalNote =
        "Se pidió hace más de 71 horas: antes de facturar otra vez lo que cubría, revisa en el portal del SAT que no se haya timbrado.";

    /// <summary>The private file of an invoice: «invoices/{business}/{yyyyMM}/{invoice}.xml».</summary>
    public static string FileKey(Cfdi invoice, string extension)
        => $"invoices/{invoice.TenantId}/{invoice.RequestedAt:yyyyMM}/{invoice.Id}.{extension}";

    /// <returns>The invoice of an order as it ends up (still tracked).</returns>
    public Task<OrderInvoice> StampAsync(Guid invoiceId, CancellationToken cancellationToken)
        => StampAsync(_context.OrderInvoices, invoiceId, cancellationToken);

    /// <returns>The invoice as it ends up (still tracked).</returns>
    public async Task<TInvoice> StampAsync<TInvoice>(IQueryable<TInvoice> invoices, Guid invoiceId, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var invoice = await invoices.FirstAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice.Status != OrderInvoiceStatus.Stamping)
            return invoice;

        StampedCfdi stamped;
        try
        {
            stamped = await _gateway.StampAsync(CfdiDocument.FromJson(invoice.Document), invoice.IdempotencyKey, cancellationToken);
        }
        catch (InvoicingGatewayRefusedException ex)
        {
            _logger.LogWarning("The PAC refused invoice {InvoiceId}", invoice.Id);
            // Past the PAC's window it no longer gives back a stamp it made with this key, and the date it refuses may
            // belong to an invoice it did stamp: the person checks before invoicing the order again.
            var reason = _clock.UtcNow - invoice.RequestedAt > StampPendingInvoicesCommandHandler.RetryWindow
                ? $"{LateRefusalNote} {ex.Message}"
                : ex.Message;
            return await RefuseAsync(invoices, invoice, reason, cancellationToken);
        }
        catch (InvoicingGatewayUnavailableException)
        {
            // Asked again with the same key later: by the person, or by the worker within the PAC's window.
            _logger.LogWarning("The PAC did not answer for invoice {InvoiceId}: it stays stamping", invoice.Id);
            return invoice;
        }

        return await KeepStampAsync(invoices, invoice, stamped, cancellationToken);
    }

    /// <summary>
    /// Keeps the stamp of an invoice «Timbrando»: the one the PAC answered, or the one a person found in its portal (Fase 28K).
    /// Its XML goes to the business's private files, its email to the customer is asked for with it, and its PDF is kept
    /// when the PAC renders it.
    /// </summary>
    /// <returns>The invoice as it ends up (still tracked).</returns>
    public async Task<TInvoice> KeepStampAsync<TInvoice>(
        IQueryable<TInvoice> invoices, TInvoice invoice, StampedCfdi stamped, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var xmlKey = FileKey(invoice, "xml");
        await SaveOnceAsync(xmlKey, Encoding.UTF8.GetBytes(stamped.Xml), "application/xml", cancellationToken);
        invoice.Stamp(stamped.Uuid, stamped.Uuid, stamped.StampedAt, xmlKey);
        // Its email to the customer is saved with the stamp, so only the attempt that stamped it sends it.
        await CfdiEmails.QueueOnStampAsync(_context, invoice, _clock.UtcNow, cancellationToken);
        invoice = await SaveAsync(invoices, invoice, cancellationToken);
        if (invoice.Status == OrderInvoiceStatus.Stamped && invoice.PdfFileKey is null)
            invoice = await AttachPdfAsync(invoices, invoice, stamped.Xml, cancellationToken);
        return invoice;
    }

    /// <summary>
    /// Makes an invoice «Timbrando» «Rechazada»: the PAC refused it, or a person checked in its portal that it was not stamped
    /// (Fase 28K). A refused replacement never stood: the invoice it was to replace is the order's invoice again (Fase 28G).
    /// </summary>
    /// <returns>The invoice as it ends up (still tracked).</returns>
    public async Task<TInvoice> RefuseAsync<TInvoice>(
        IQueryable<TInvoice> invoices, TInvoice invoice, string reason, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var now = _clock.UtcNow;
        if (invoice is OrderInvoice
            && await _context.OrderInvoices.AsNoTracking().AnyAsync(i => i.ReplacedByInvoiceId == invoice.Id, cancellationToken))
            return await RefuseReplacementAsync(invoices, invoice.Id, reason, now, cancellationToken);
        invoice.Refuse(reason, now);
        return await SaveAsync(invoices, invoice, cancellationToken);
    }

    /// <summary>
    /// Why a person may not ask the PAC again for an invoice's stamp, or null when they may: past the PAC's window it no
    /// longer gives the stamp back and the CFDI's date is too old to stamp, so it is checked in the SAT's portal (Fase 28K).
    /// </summary>
    public async Task<string?> RetryRefusalAsync<TInvoice>(IQueryable<TInvoice> invoices, Guid invoiceId, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var asked = await invoices.AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => new { i.Status, i.RequestedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return asked is not null && StuckStamps.IsStuck(asked.Status, asked.RequestedAt, _clock.UtcNow)
            ? StuckStamps.PastWindowMessage
            : null;
    }

    /// <summary>The text of a private file, or null when there is none.</summary>
    public static async Task<string?> ReadTextAsync(IPrivateFileStorage storage, string key, CancellationToken cancellationToken)
    {
        await using var content = await storage.OpenReadAsync(key, cancellationToken);
        if (content is null)
            return null;
        using var reader = new StreamReader(content, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>The end of the CFDI's seal (its last 8 characters), which the SAT's status service asks for.</summary>
    public static string? SealTail(string stampedXml)
    {
        var seal = XDocument.Parse(stampedXml).Root?.Attribute("Sello")?.Value;
        return seal is { Length: >= 8 } ? seal[^8..] : null;
    }

    private async Task<TInvoice> AttachPdfAsync<TInvoice>(
        IQueryable<TInvoice> invoices, TInvoice invoice, string stampedXml, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        byte[] pdf;
        try
        {
            pdf = await _gateway.RenderPdfAsync(stampedXml, invoice.TenantId, cancellationToken);
        }
        catch (Exception ex) when (ex is InvoicingGatewayUnavailableException or InvoicingGatewayRefusedException)
        {
            _logger.LogWarning("The PAC did not render the PDF of invoice {InvoiceId}: it is rendered when downloaded", invoice.Id);
            return invoice;
        }

        var pdfKey = FileKey(invoice, "pdf");
        await SaveOnceAsync(pdfKey, pdf, "application/pdf", cancellationToken);
        invoice.AttachPdf(pdfKey);
        return await SaveAsync(invoices, invoice, cancellationToken);
    }

    // Two attempts at once save the same stamp under the same key: the second finds it there.
    private async Task SaveOnceAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        if (await ExistsAsync(key, cancellationToken))
            return;
        try
        {
            await using var stream = new MemoryStream(content);
            await _storage.SaveAsync(key, stream, contentType, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!await ExistsAsync(key, cancellationToken))
                throw;
        }
    }

    private async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        await using var existing = await _storage.OpenReadAsync(key, cancellationToken);
        return existing is not null;
    }

    /// <summary>
    /// The refusal of an invoice that was to replace another (Fase 28G), in one transaction: the replacement leaves the
    /// order's live invoices before the one it was to replace comes back, since SQL Server checks the index of one live
    /// invoice per order statement by statement (in one save, the two updates go in the order of their keys).
    /// </summary>
    private async Task<TInvoice> RefuseReplacementAsync<TInvoice>(
        IQueryable<TInvoice> invoices, Guid invoiceId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        async Task<Result> RefuseInOrderAsync(CancellationToken ct)
        {
            var refused = await _context.OrderInvoices.FirstAsync(i => i.Id == invoiceId, ct);
            if (refused.Status != OrderInvoiceStatus.Stamping)
                return Result.Success();
            refused.Refuse(reason, now);
            await _context.SaveChangesAsync(ct);
            if (await _context.OrderInvoices.FirstOrDefaultAsync(i => i.ReplacedByInvoiceId == invoiceId, ct) is { } replaced)
            {
                replaced.KeepAfterRefusedReplacement(invoiceId);
                await _context.SaveChangesAsync(ct);
            }
            return Result.Success();
        }

        try
        {
            // Inside a caller's transaction, as part of it.
            if (_context.Database.CurrentTransaction is not null)
                await RefuseInOrderAsync(cancellationToken);
            else
                await _context.ExecuteInResilientTransactionAsync(RefuseInOrderAsync, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another attempt wrote it first (the same stamp, or the PAC's refusal): that one stands.
            _context.ChangeTracker.Clear();
        }
        return await invoices.FirstAsync(i => i.Id == invoiceId, cancellationToken);
    }

    // Another attempt wrote it first (the same stamp, or the PAC's refusal): that one stands.
    private async Task<TInvoice> SaveAsync<TInvoice>(IQueryable<TInvoice> invoices, TInvoice invoice, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return invoice;
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            return await invoices.FirstAsync(i => i.Id == invoice.Id, cancellationToken);
        }
    }
}
