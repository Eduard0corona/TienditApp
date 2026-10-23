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
/// «Timbrando» (<see cref="StampPendingInvoicesCommand"/>), and every half hour it asks the SAT about the cancellations that
/// wait for the customer (<see cref="CheckInvoiceCancellationsCommand"/>), each in its business's scope. Without the PAC
/// configured it does nothing.
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
                max);
            if (checkCancellations)
            {
                cancelling = Businesses(
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
                    max);
                _cancellationsCheckedAt = now;
            }
        }

        foreach (var tenantId in stamping)
            await SendAsync(tenantId, new StampPendingInvoicesCommand(), "stamped", cancellationToken);
        foreach (var tenantId in cancelling)
            await SendAsync(tenantId, new CheckInvoiceCancellationsCommand(), "with their cancellation settled", cancellationToken);

        return stamping.Union(cancelling).Count();
    }

    private sealed record Waiting(Guid TenantId, DateTimeOffset? Since);

    /// <summary>The businesses with work of either kind, the one that has waited longest first.</summary>
    private static List<Guid> Businesses(List<Waiting> orders, List<Waiting> globals, int max)
        => orders.Concat(globals)
            .GroupBy(waiting => waiting.TenantId)
            .OrderBy(group => group.Min(waiting => waiting.Since))
            .Select(group => group.Key)
            .Take(max)
            .ToList();

    private async Task SendAsync(Guid tenantId, IRequest<Result<int>> command, string what, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var scope = _scopeFactory.CreateTenantScope(tenantId, WorkerActor);
            var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
            if (result.IsSuccess && result.Value > 0)
                _logger.LogInformation("Invoices of tenant {TenantId}: {Count} {What}", tenantId, result.Value, what);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Invoicing of tenant {TenantId} failed", tenantId);
        }
    }
}
