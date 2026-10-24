using System.Text;
using System.Xml.Linq;
using FoodParkDigital.Application.Common.Interfaces;
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
/// downloaded.
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
        "Se pidió hace más de 71 horas: antes de facturar otra vez lo que cubría, revisa en el portal del PAC que no se haya timbrado.";

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
            var now = _clock.UtcNow;
            // Past the PAC's window it no longer gives back a stamp it made with this key, and the date it refuses may
            // belong to an invoice it did stamp: the person checks before invoicing the order again.
            invoice.Refuse(now - invoice.RequestedAt > StampPendingInvoicesCommandHandler.RetryWindow
                ? $"{LateRefusalNote} {ex.Message}"
                : ex.Message, now);
            return await SaveAsync(invoices, invoice, cancellationToken);
        }
        catch (InvoicingGatewayUnavailableException)
        {
            // Asked again with the same key later: by the person, or by the worker within the PAC's window.
            _logger.LogWarning("The PAC did not answer for invoice {InvoiceId}: it stays stamping", invoice.Id);
            return invoice;
        }

        var xmlKey = FileKey(invoice, "xml");
        await SaveOnceAsync(xmlKey, Encoding.UTF8.GetBytes(stamped.Xml), "application/xml", cancellationToken);
        invoice.Stamp(stamped.Uuid, stamped.Uuid, stamped.StampedAt, xmlKey);
        invoice = await SaveAsync(invoices, invoice, cancellationToken);
        if (invoice.Status == OrderInvoiceStatus.Stamped && invoice.PdfFileKey is null)
            invoice = await AttachPdfAsync(invoices, invoice, stamped.Xml, cancellationToken);
        return invoice;
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
            pdf = await _gateway.RenderPdfAsync(stampedXml, cancellationToken);
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
