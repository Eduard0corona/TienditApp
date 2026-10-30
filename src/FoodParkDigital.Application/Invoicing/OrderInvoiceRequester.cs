using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// Who asks for an order's invoice and what else must hold: a person of the business in Pedidos (Fase 28A), or the customer
/// with the ticket (Fase 28F, ADR 0050), who also needs the sale paid in full and within its month.
/// </summary>
internal sealed record OrderInvoiceRequest(
    Guid OrderId,
    CfdiReceiverInput Receiver,
    string? ReceiverEmail,
    Guid RequestedByUserId,
    string PerformedBy)
{
    /// <summary>Why this one cannot go on with the draft and the order it was built from, checked before the folio.</summary>
    public Func<CfdiDraftDto, Order, string?>? Check { get; init; }

    /// <summary>The WhatsApp its CFDI go to (Fase 28M), already normalized and checked; none, nowhere.</summary>
    public string? ReceiverWhatsApp { get; init; }

    /// <summary>What else is saved with the invoice (Pedidos keeps the fiscal data in the customer).</summary>
    public Func<Order, OrderInvoice, Task>? BeforeSave { get; init; }

    /// <summary>
    /// The order's stamped invoice this one replaces because it has errors (Fase 28G, ADR 0051): the new one carries its
    /// UUID with the relation 04, and it is cancelled with motive 01 once the new one is stamped.
    /// </summary>
    public Guid? Replaces { get; init; }
}

/// <summary>
/// The invoice of an order fixed before the PAC is asked: its folio, its date, its key and the CFDI built from the order's
/// draft, in the caller's transaction, so a refused request gives the folio back. The same for Pedidos, for the customer who
/// invoices the ticket and for the one that replaces an invoice with errors (Fase 28G): under the business's invoicing lock,
/// one live invoice per order that no other replaces, never one of a sale in a live global invoice.
/// </summary>
internal sealed class OrderInvoiceRequester
{
    private readonly IApplicationDbContext _context;
    private readonly IInvoicingLock _lock;
    private readonly IFolioGenerator _folios;
    private readonly IDateTimeProvider _clock;

    public OrderInvoiceRequester(IApplicationDbContext context, IInvoicingLock invoicingLock, IFolioGenerator folios, IDateTimeProvider clock)
    {
        _context = context;
        _lock = invoicingLock;
        _folios = folios;
        _clock = clock;
    }

    public async Task<Result<OrderInvoice>> RequestAsync(Guid tenantId, OrderInvoiceRequest request, CancellationToken cancellationToken)
    {
        // A global invoice of this business asked for at the same time waits for this one (Fase 28B): the sale is in one or
        // the other, never in both.
        await _lock.AcquireAsync(tenantId, cancellationToken);
        var built = await GetOrderCfdiDraftQueryHandler.BuildAsync(_context, request.OrderId, request.Receiver, cancellationToken);
        if (built.IsFailure)
            return Result.Failure<OrderInvoice>(built.Error!);
        var (draft, order, _) = built.Value;
        if (!draft.IsReadyToStamp)
            return Result.Failure<OrderInvoice>(draft.Blockers[0]);

        var live = await _context.OrderInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == order.Id && i.ReplacedByInvoiceId == null
                && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                    || i.Status == OrderInvoiceStatus.Cancelling), cancellationToken);
        // The one it replaces is the order's live invoice: the replacement takes its place.
        OrderInvoice? replaced = null;
        string? replaceProblem = null;
        if (request.Replaces is { } replacesId)
        {
            replaced = live?.Id == replacesId ? await _context.OrderInvoices.FirstAsync(i => i.Id == replacesId, cancellationToken) : null;
            replaceProblem = await OrderInvoiceSubstitutions.ProblemAsync(
                    _context,
                    replaced ?? await _context.OrderInvoices.AsNoTracking()
                        .FirstOrDefaultAsync(i => i.Id == replacesId && i.OrderId == order.Id, cancellationToken),
                    draft.Issuer.Rfc,
                    cancellationToken)
                ?? (replaced is null ? OrderInvoiceSubstitutions.NotCurrentMessage : null);
        }
        var orderProblem = (request.Replaces is null ? OrderInvoicing.LiveInvoiceProblem(live) : replaceProblem)
            ?? await OrderInvoicing.GlobalInvoiceProblemAsync(_context, order.Id, cancellationToken)
            ?? await OrderInvoicing.OrderProblemAsync(_context, order, cancellationToken)
            ?? request.Check?.Invoke(draft, order);
        if (orderProblem is not null)
            return Result.Failure<OrderInvoice>(orderProblem);

        var now = _clock.UtcNow;
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var certificateProblem = InvoicingSettings.CertificateProblem(profile, draft.Issuer.Rfc, now);
        if (certificateProblem is not null)
            return Result.Failure<OrderInvoice>(certificateProblem);

        CfdiReceiver receiver;
        try
        {
            receiver = OrderInvoicing.Receiver(request.Receiver);
        }
        catch (DomainException ex)
        {
            return Result.Failure<OrderInvoice>(ex.Message);
        }

        var timeZone = await _context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var serie = profile!.Serie;
        var folio = await _folios.NextInvoiceFolioAsync(tenantId, serie, cancellationToken);
        var document = CfdiDocument.From(draft, receiver, serie, folio, TenantDateTimeResolver.LocalNow(now, timeZone), replaced?.Uuid);
        // Paid in full, in one exhibition with what paid it; otherwise in installments, «por definir» (Fase 28C).
        var invoice = OrderInvoice.Request(
            tenantId, order.Id, serie, folio, receiver, draft.PaymentForm, document.ToJson(),
            draft.SubTotal, draft.TotalTransferred, draft.Total, request.RequestedByUserId, now, draft.PaymentMethod);
        invoice.SendTo(request.ReceiverEmail);
        invoice.SendWhatsAppTo(request.ReceiverWhatsApp);

        try
        {
            if (replaced is not null)
            {
                // The replaced one stops counting as the order's live invoice before its replacement is saved: SQL Server
                // checks the index of one live invoice per order statement by statement.
                replaced.ReplaceWith(invoice);
                await _context.SaveChangesAsync(cancellationToken);
            }

            _context.OrderInvoices.Add(invoice);
            if (request.BeforeSave is not null)
                await request.BeforeSave(order, invoice);
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenantId,
                entityName: nameof(OrderInvoice),
                entityId: invoice.Id.ToString(),
                action: "OrderInvoiceRequested",
                performedBy: request.PerformedBy,
                metadata: JsonSerializer.Serialize(new
                {
                    invoice.OrderId, order.OrderNumber, Number = invoice.Number, receiver.Rfc, invoice.Total, invoice.PaymentMethod,
                    Replaces = replaced?.Number
                })));
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) when (replaced is not null)
        {
            // Its PDF, its cancellation check or a person changed the replaced one meanwhile: nothing is kept, folio included.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure<OrderInvoice>(OrderInvoiceSubstitutions.ChangedMessage(replaced.Number));
        }
        catch (DbUpdateException)
        {
            // Someone invoiced the same order at the same time (the index of one live invoice per order kept theirs): this
            // one is undone whole, folio included. Anything else is thrown on, for the execution strategy to retry. A
            // replacement is requested with the order's live invoice in hand, under the invoicing lock: no one else invoiced
            // the order meanwhile, and the replaced one is live again once this is undone, so its failure is always thrown on.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (replaced is null && await _context.OrderInvoices.AsNoTracking().AnyAsync(
                    i => i.OrderId == request.OrderId && i.ReplacedByInvoiceId == null
                        && (i.Status == OrderInvoiceStatus.Stamping || i.Status == OrderInvoiceStatus.Stamped
                            || i.Status == OrderInvoiceStatus.Cancelling),
                    cancellationToken))
                return Result.Failure<OrderInvoice>(OrderInvoicing.AlreadyInvoicingMessage);
            throw;
        }

        return Result.Success(invoice);
    }
}
