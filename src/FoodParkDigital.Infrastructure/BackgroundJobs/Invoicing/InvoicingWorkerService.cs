using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Invoicing;

/// <summary>
/// What invoicing leaves pending (Fase 28A, ADR 0045): every few minutes it asks the PAC again for the invoices that stayed
/// «Timbrando» (<see cref="StampPendingInvoicesCommand"/>), issues the payment complements (Fase 28C) and the credit notes
/// (Fase 28D) the businesses owe, and every half hour it asks the SAT about the cancellations that wait for the customer and
/// to cancel the invoices whose replacement is stamped (<see cref="CheckInvoiceCancellationsCommand"/>, Fase 28G), each in
/// its business's scope. Without the PAC configured it does nothing.
/// </summary>
public sealed class InvoicingWorkerService : BackgroundService
{
    private const string WorkerActor = "invoicing";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly IInvoicingGateway _gateway;
    private readonly BackgroundJobOptions.InvoicingOptions _options;
    private readonly ILogger<InvoicingWorkerService> _logger;
    private DateTimeOffset? _cancellationsCheckedAt;

    /// <summary>
    /// The businesses whose last complements pass stamped none (Fase 28C), and when it ran. They go after the others, the
    /// least recently tried first: a business whose oldest payments can never be issued here (its invoice was stamped with
    /// the RFC Mi negocio had before, say) never keeps the others waiting, and those businesses take turns among
    /// themselves. It lives in memory: after a restart they go by their oldest payment again until a pass marks them.
    /// </summary>
    private readonly Dictionary<Guid, DateTimeOffset> _complementsWithoutProgress = [];

    /// <summary>The same for the credit notes (Fase 28D): the businesses whose last pass stamped none go after the others.</summary>
    private readonly Dictionary<Guid, DateTimeOffset> _creditNotesWithoutProgress = [];

    public InvoicingWorkerService(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IInvoicingGateway gateway,
        IOptions<BackgroundJobOptions> options,
        ILogger<InvoicingWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _gateway = gateway;
        _options = options.Value.Invoicing;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Invoicing pass failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <returns>Businesses handled.</returns>
    internal async Task<int> RunPassAsync(CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return 0;

        var now = _clock.UtcNow;
        var checkCancellations = _cancellationsCheckedAt is null
            || now - _cancellationsCheckedAt >= TimeSpan.FromMinutes(Math.Max(1, _options.CancellationIntervalMinutes));
        List<Guid> stamping;
        List<Guid> complementing;
        List<Guid> crediting;
        List<Guid> cancelling = [];
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var max = Math.Max(1, _options.MaxBusinessesPerPass);
            var settled = now - StampPendingInvoicesCommandHandler.SettleTime;
            var oldest = now - StampPendingInvoicesCommandHandler.RetryWindow;
            // Only to find the businesses with work: each one is handled in its own scope. The longest waiting goes first,
            // whether its invoice is an order's or a global one (Fase 28B).
            stamping = Businesses(
                max,
                await context.OrderInvoices
                    .IgnoreQueryFilters()
                    .Where(i => i.Status == OrderInvoiceStatus.Stamping && i.RequestedAt <= settled && i.RequestedAt > oldest)
                    .GroupBy(i => i.TenantId)
                    .Select(g => new Waiting(g.Key, g.Min(i => i.RequestedAt)))
                    .ToListAsync(cancellationToken),
                await context.GlobalInvoices
                    .IgnoreQueryFilters()
                    .Where(i => i.Status == OrderInvoiceStatus.Stamping && i.RequestedAt <= settled && i.RequestedAt > oldest)
                    .GroupBy(i => i.TenantId)
                    .Select(g => new Waiting(g.Key, g.Min(i => i.RequestedAt)))
                    .ToListAsync(cancellationToken),
                await context.PaymentComplements
                    .IgnoreQueryFilters()
                    .Where(c => c.Status == OrderInvoiceStatus.Stamping && c.RequestedAt <= settled && c.RequestedAt > oldest)
                    .GroupBy(c => c.TenantId)
                    .Select(g => new Waiting(g.Key, g.Min(c => c.RequestedAt)))
                    .ToListAsync(cancellationToken),
                await context.CreditNotes
                    .IgnoreQueryFilters()
                    .Where(n => n.Status == OrderInvoiceStatus.Stamping && n.RequestedAt <= settled && n.RequestedAt > oldest)
                    .GroupBy(n => n.TenantId)
                    .Select(g => new Waiting(g.Key, g.Min(n => n.RequestedAt)))
                    .ToListAsync(cancellationToken));
            // The payments of stamped invoices in installments that never had a complement (Fase 28C), each business's own
            // (the payment, its invoice and its complements are of the same business, joined by its order). Only of the
            // businesses whose CSD seals now: one that cannot issue them would take the places of the ones that can, pass
            // after pass, while it waits for its new CSD.
            var sealing = context.InvoicingProfiles
                .IgnoreQueryFilters()
                .Where(p => p.CertificateNumber != null && p.CertificateValidFrom <= now && p.CertificateValidTo > now)
                .Select(p => p.TenantId);
            var owing = await PaymentComplements.Pending(
                    context.OrderPayments.IgnoreQueryFilters(),
                    context.OrderInvoices.IgnoreQueryFilters(),
                    context.PaymentComplements.IgnoreQueryFilters())
                .Where(p => sealing.Contains(p.TenantId))
                .GroupBy(p => p.TenantId)
                .Select(g => new Waiting(g.Key, g.Min(p => p.PaidAt)))
                .ToListAsync(cancellationToken);
            complementing = Fairly(owing, _complementsWithoutProgress, max);
            // The refunds and write-offs of orders with a stamped invoice that never had a credit note for it (Fase 28D), and
            // those of sales a stamped global invoice covers (Fases 28I and 28J), each business's own, of the businesses whose
            // CSD seals now, by the same turns.
            var refunds = await CreditNotes.PendingRefunds(
                    context.OrderRefunds.IgnoreQueryFilters(),
                    context.Orders.IgnoreQueryFilters(),
                    context.OrderInvoices.IgnoreQueryFilters(),
                    context.GlobalInvoiceOrders.IgnoreQueryFilters(),
                    context.GlobalInvoices.IgnoreQueryFilters(),
                    context.CreditNotes.IgnoreQueryFilters())
                .Where(r => sealing.Contains(r.TenantId))
                .GroupBy(r => r.TenantId)
                .Select(g => new Waiting(g.Key, g.Min(r => r.RefundedAt)))
                .ToListAsync(cancellationToken);
            var writeOffs = await CreditNotes.PendingWriteOffs(
                    context.OrderWriteOffs.IgnoreQueryFilters(),
                    context.OrderInvoices.IgnoreQueryFilters(),
                    context.GlobalInvoiceOrders.IgnoreQueryFilters(),
                    context.GlobalInvoices.IgnoreQueryFilters(),
                    context.CreditNotes.IgnoreQueryFilters())
                .Where(w => sealing.Contains(w.TenantId))
                .GroupBy(w => w.TenantId)
                .Select(g => new Waiting(g.Key, g.Min(w => w.WrittenOffAt)))
                .ToListAsync(cancellationToken);
            crediting = Fairly(
                refunds.Concat(writeOffs)
                    .GroupBy(w => w.TenantId)
                    .Select(g => new Waiting(g.Key, g.Min(w => w.Since)))
                    .ToList(),
                _creditNotesWithoutProgress,
                max);
            if (checkCancellations)
            {
                cancelling = Businesses(
                    max,
                    await context.OrderInvoices
                        .IgnoreQueryFilters()
                        .Where(i => i.Status == OrderInvoiceStatus.Cancelling)
                        .GroupBy(i => i.TenantId)
                        .Select(g => new Waiting(g.Key, g.Min(i => i.CancellationRequestedAt)))
                        .ToListAsync(cancellationToken),
                    await context.GlobalInvoices
                        .IgnoreQueryFilters()
                        .Where(i => i.Status == OrderInvoiceStatus.Cancelling)
                        .GroupBy(i => i.TenantId)
                        .Select(g => new Waiting(g.Key, g.Min(i => i.CancellationRequestedAt)))
                        .ToListAsync(cancellationToken),
                    await context.PaymentComplements
                        .IgnoreQueryFilters()
                        .Where(c => c.Status == OrderInvoiceStatus.Cancelling)
                        .GroupBy(c => c.TenantId)
                        .Select(g => new Waiting(g.Key, g.Min(c => c.CancellationRequestedAt)))
                        .ToListAsync(cancellationToken),
                    await context.CreditNotes
                        .IgnoreQueryFilters()
                        .Where(n => n.Status == OrderInvoiceStatus.Cancelling)
                        .GroupBy(n => n.TenantId)
                        .Select(g => new Waiting(g.Key, g.Min(n => n.CancellationRequestedAt)))
                        .ToListAsync(cancellationToken),
                    // The invoices whose replacement is stamped, to be cancelled with motive 01 (Fase 28G): waiting since
                    // their replacement was stamped.
                    await OrderInvoiceSubstitutions.WaitingForCancellation(
                            context.OrderInvoices.IgnoreQueryFilters(), now - OrderInvoiceSubstitutions.CancellationRetryWindow)
                        .Join(context.OrderInvoices.IgnoreQueryFilters(), i => i.ReplacedByInvoiceId, r => (Guid?)r.Id,
                            (i, r) => new { i.TenantId, r.StampedAt })
                        .GroupBy(w => w.TenantId)
                        .Select(g => new Waiting(g.Key, g.Min(w => w.StampedAt)))
                        .ToListAsync(cancellationToken));
                _cancellationsCheckedAt = now;
            }
        }

        foreach (var tenantId in stamping)
            await SendAsync(tenantId, new StampPendingInvoicesCommand(), "stamped", cancellationToken);
        foreach (var tenantId in complementing)
        {
            if (await SendAsync(tenantId, new IssuePendingPaymentComplementsCommand(), "payment complements issued", cancellationToken) > 0)
                _complementsWithoutProgress.Remove(tenantId);
            else
                _complementsWithoutProgress[tenantId] = now;
        }
        foreach (var tenantId in crediting)
        {
            if (await SendAsync(tenantId, new IssuePendingCreditNotesCommand(), "credit notes issued", cancellationToken) > 0)
                _creditNotesWithoutProgress.Remove(tenantId);
            else
                _creditNotesWithoutProgress[tenantId] = now;
        }
        foreach (var tenantId in cancelling)
            await SendAsync(tenantId, new CheckInvoiceCancellationsCommand(), "with their cancellation settled", cancellationToken);

        return stamping.Union(complementing).Union(crediting).Union(cancelling).Count();
    }

    private sealed record Waiting(Guid TenantId, DateTimeOffset? Since);

    /// <summary>
    /// The businesses that owe CFDI, the longest waiting first, except that the ones whose last pass stamped nothing go after
    /// the others, the least recently tried first. The ones that no longer owe any are forgotten.
    /// </summary>
    private static List<Guid> Fairly(List<Waiting> owing, Dictionary<Guid, DateTimeOffset> withoutProgress, int max)
    {
        foreach (var tenantId in withoutProgress.Keys.Except(owing.Select(w => w.TenantId)).ToList())
            withoutProgress.Remove(tenantId);
        return owing
            .OrderBy(w => withoutProgress.ContainsKey(w.TenantId))
            .ThenBy(w => withoutProgress.TryGetValue(w.TenantId, out var tried) ? tried : w.Since)
            .Select(w => w.TenantId)
            .Take(max)
            .ToList();
    }

    /// <summary>The businesses with work of any kind, the one that has waited longest first.</summary>
    private static List<Guid> Businesses(int max, params List<Waiting>[] kinds)
        => kinds.SelectMany(kind => kind)
            .GroupBy(waiting => waiting.TenantId)
            .OrderBy(group => group.Min(waiting => waiting.Since))
            .Select(group => group.Key)
            .Take(max)
            .ToList();

    /// <returns>What the command settled, or 0 when it failed.</returns>
    private async Task<int> SendAsync(Guid tenantId, IRequest<Result<int>> command, string what, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var scope = _scopeFactory.CreateTenantScope(tenantId, WorkerActor);
            var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
            if (result.IsSuccess && result.Value > 0)
                _logger.LogInformation("Invoices of tenant {TenantId}: {Count} {What}", tenantId, result.Value, what);
            return result.IsSuccess ? result.Value : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Invoicing of tenant {TenantId} failed", tenantId);
            return 0;
        }
    }
}
