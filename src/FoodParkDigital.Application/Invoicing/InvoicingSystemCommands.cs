using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// Asks the PAC again for a business's invoices that stayed «Timbrando» (the invoicing worker sends it in the business's
/// scope, Fase 28A; global invoices too, Fase 28B): each with its own CFDI and key, while the PAC still answers a repeated
/// key with the stamp it gave.
/// </summary>
[AllowAnonymousRequest("Lo envía el worker de facturación (sistema), sin usuario, en el alcance del negocio.")]
public record StampPendingInvoicesCommand : IRequest<Result<int>>;

public class StampPendingInvoicesCommandHandler : IRequestHandler<StampPendingInvoicesCommand, Result<int>>
{
    /// <summary>An invoice this young may still be in the hands of whoever asked for it.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the PAC gives back the stamp of a repeated key (SW Sapien: 72 h), and the oldest date a CFDI may carry when
    /// it is stamped. After that the invoice is only asked for by a person.
    /// </summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromHours(71);

    public const int MaxPerPass = 20;

    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;
    private readonly OrderInvoiceStamper _stamper;
    private readonly ILogger<OrderInvoiceStamper> _logger;

    public StampPendingInvoicesCommandHandler(
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        IDateTimeProvider clock,
        ILogger<OrderInvoiceStamper> logger)
    {
        _context = context;
        _gateway = gateway;
        _clock = clock;
        _logger = logger;
        _stamper = new OrderInvoiceStamper(context, gateway, storage, clock, logger);
    }

    /// <returns>Invoices that ended up stamped.</returns>
    public async Task<Result<int>> Handle(StampPendingInvoicesCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Success(0);

        var stamped = await StampAsync(_context.OrderInvoices, _context.OrderInvoices, cancellationToken)
            + await StampAsync(_context.GlobalInvoices, GlobalInvoicing.WithSales(_context), cancellationToken);
        return Result.Success(stamped);
    }

    private async Task<int> StampAsync<TInvoice>(
        IQueryable<TInvoice> invoices, IQueryable<TInvoice> loaded, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var now = _clock.UtcNow;
        var settled = now - SettleTime;
        var oldest = now - RetryWindow;
        var pending = await invoices.AsNoTracking()
            .Where(i => i.Status == OrderInvoiceStatus.Stamping && i.RequestedAt <= settled && i.RequestedAt > oldest)
            .OrderBy(i => i.RequestedAt)
            .Select(i => i.Id)
            .Take(MaxPerPass)
            .ToListAsync(cancellationToken);

        var stamped = 0;
        foreach (var invoiceId in pending)
        {
            try
            {
                var invoice = await _stamper.StampAsync(loaded, invoiceId, cancellationToken);
                if (invoice.Status == OrderInvoiceStatus.Stamped)
                    stamped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One that fails (its file, its XML, the database) does not hold up the others: the next pass asks again.
                _logger.LogError("Invoice {InvoiceId} could not be stamped in this pass: {Error}", invoiceId, ex.GetType().Name);
            }
            _context.ChangeTracker.Clear();
        }
        return stamped;
    }
}

/// <summary>
/// Asks the SAT about a business's invoices whose cancellation waits for the customer (the invoicing worker sends it in
/// the business's scope, Fase 28A; global invoices too, Fase 28B): cancelled (accepted, or the customer let the time pass)
/// or kept, with the reason.
/// </summary>
[AllowAnonymousRequest("Lo envía el worker de facturación (sistema), sin usuario, en el alcance del negocio.")]
public record CheckInvoiceCancellationsCommand : IRequest<Result<int>>;

public class CheckInvoiceCancellationsCommandHandler : IRequestHandler<CheckInvoiceCancellationsCommand, Result<int>>
{
    public const string RefusedByReceiverMessage =
        "El cliente rechazó la cancelación en el SAT: la factura sigue vigente. Habla con él o vuelve a pedirla.";

    public const int MaxPerPass = 50;

    private readonly IApplicationDbContext _context;
    private readonly IInvoicingGateway _gateway;
    private readonly IPrivateFileStorage _storage;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CheckInvoiceCancellationsCommandHandler> _logger;

    public CheckInvoiceCancellationsCommandHandler(
        IApplicationDbContext context,
        IInvoicingGateway gateway,
        IPrivateFileStorage storage,
        IDateTimeProvider clock,
        ILogger<CheckInvoiceCancellationsCommandHandler> logger)
    {
        _context = context;
        _gateway = gateway;
        _storage = storage;
        _clock = clock;
        _logger = logger;
    }

    /// <returns>Invoices whose cancellation the SAT settled.</returns>
    public async Task<Result<int>> Handle(CheckInvoiceCancellationsCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Success(0);

        var settled = await CheckAsync(_context.OrderInvoices, _context.OrderInvoices, cancellationToken)
            + await CheckAsync(_context.GlobalInvoices, GlobalInvoicing.WithSales(_context), cancellationToken);
        return Result.Success(settled);
    }

    private async Task<int> CheckAsync<TInvoice>(
        IQueryable<TInvoice> invoices, IQueryable<TInvoice> loaded, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var waiting = await invoices.AsNoTracking()
            .Where(i => i.Status == OrderInvoiceStatus.Cancelling)
            .OrderBy(i => i.CancellationRequestedAt)
            .Take(MaxPerPass)
            .ToListAsync(cancellationToken);

        var settled = 0;
        foreach (var invoice in waiting)
        {
            try
            {
                if (await SettleAsync(loaded, invoice, cancellationToken))
                    settled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One that fails (its file, its XML, the database) does not hold up the others: the next pass asks again.
                _logger.LogError("The cancellation of invoice {InvoiceId} could not be checked in this pass: {Error}",
                    invoice.Id, ex.GetType().Name);
            }
            _context.ChangeTracker.Clear();
        }
        return settled;
    }

    /// <returns>Whether the SAT settled its cancellation and that was saved.</returns>
    private async Task<bool> SettleAsync<TInvoice>(IQueryable<TInvoice> loaded, TInvoice invoice, CancellationToken cancellationToken)
        where TInvoice : Cfdi
    {
        var status = await StatusAsync(invoice, cancellationToken);
        if (status is null || (status.State != CfdiSatState.Cancelled && !status.CancellationRefused))
            return false;

        var tracked = await loaded.FirstAsync(i => i.Id == invoice.Id, cancellationToken);
        if (tracked.Status != OrderInvoiceStatus.Cancelling)
            return false;
        if (status.State == CfdiSatState.Cancelled)
            tracked.Cancel(_clock.UtcNow);
        else
            tracked.KeepAfterRefusedCancellation(RefusedByReceiverMessage);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone changed it meanwhile: the next pass asks again.
            return false;
        }
    }

    private async Task<CfdiSatStatus?> StatusAsync(Cfdi invoice, CancellationToken cancellationToken)
    {
        var xml = invoice.XmlFileKey is null
            ? null
            : await OrderInvoiceStamper.ReadTextAsync(_storage, invoice.XmlFileKey, cancellationToken);
        var sealTail = xml is null ? null : OrderInvoiceStamper.SealTail(xml);
        if (sealTail is null)
        {
            _logger.LogWarning("Invoice {InvoiceId} has no stamped XML to ask the SAT about", invoice.Id);
            return null;
        }

        // The parties and the total the CFDI was stamped with: the receiver of a global invoice is the general public, and a
        // payment complement's total is zero.
        var document = CfdiDocument.FromJson(invoice.Document);
        try
        {
            return await _gateway.GetSatStatusAsync(
                new CfdiStatusQuery(document.Issuer.Rfc, document.Receiver.Rfc, document.Total, invoice.Uuid!, sealTail), cancellationToken);
        }
        catch (InvoicingGatewayUnavailableException)
        {
            return null;
        }
    }
}
