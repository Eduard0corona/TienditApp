using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.Invoicing;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// The invoicing worker (Fase 28A; global invoices too, Fase 28B) finds the businesses with invoices still stamping within the
/// PAC's window, and every half hour the ones with cancellations waiting for the SAT, and sends each one its command in its
/// own scope.
/// </summary>
public class InvoicingWorkerTests
{
    [Fact]
    public async Task Pass_ShouldAskAgain_ForTheInvoicesStillStamping_WithinThePacsWindow_InTheirBusinessesScope()
    {
        var sent = new SentCommands();
        await using var host = Host(sent);
        var now = host.Clock.UtcNow;
        var waiting = await AddInvoiceAsync(host, "esperando", requestedAt: now.AddMinutes(-5));
        await AddInvoiceAsync(host, "reciente", requestedAt: now.AddSeconds(-20));
        await AddInvoiceAsync(host, "vieja", requestedAt: now - StampPendingInvoicesCommandHandler.RetryWindow - TimeSpan.FromMinutes(1));
        await AddInvoiceAsync(host, "rechazada", requestedAt: now.AddMinutes(-5), refused: true);
        var worker = host.Get<InvoicingWorkerService>();

        Assert.Equal(1, await worker.RunPassAsync(CancellationToken.None));

        Assert.Equal([waiting], sent.Stamps);
        Assert.Empty(sent.Checks);
    }

    [Fact]
    public async Task Pass_ShouldAskTheSatAboutCancellations_OnlyEveryHalfHour()
    {
        var sent = new SentCommands();
        await using var host = Host(sent);
        var cancelling = await AddInvoiceAsync(host, "cancelandose", requestedAt: host.Clock.UtcNow.AddDays(-1), cancelling: true);
        var worker = host.Get<InvoicingWorkerService>();

        await worker.RunPassAsync(CancellationToken.None);
        Assert.Equal([cancelling], sent.Checks);
        // A minute short of the half hour it does not ask again; at the half hour it does.
        host.Clock.Advance(TimeSpan.FromMinutes(29));
        await worker.RunPassAsync(CancellationToken.None);
        Assert.Equal([cancelling], sent.Checks);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await worker.RunPassAsync(CancellationToken.None);

        Assert.Equal([cancelling, cancelling], sent.Checks);
        Assert.Empty(sent.Stamps);
    }

    [Fact]
    public async Task Pass_ShouldFindTheBusinessesWithGlobalInvoicesStampingOrCancelling_TheLongestWaitingFirst()
    {
        var sent = new SentCommands();
        await using var host = Host(sent);
        var now = host.Clock.UtcNow;
        var order = await AddInvoiceAsync(host, "pedido", requestedAt: now.AddMinutes(-5));
        var global = await AddGlobalInvoiceAsync(host, "global", requestedAt: now.AddMinutes(-9));
        var cancelling = await AddGlobalInvoiceAsync(host, "global-cancelandose", requestedAt: now.AddDays(-1), cancelling: true);
        var worker = host.Get<InvoicingWorkerService>();

        Assert.Equal(3, await worker.RunPassAsync(CancellationToken.None));

        Assert.Equal([global, order], sent.Stamps);
        Assert.Equal([cancelling], sent.Checks);
    }

    private static Task<Guid> AddGlobalInvoiceAsync(OutboxTestHost host, string slug, DateTimeOffset requestedAt, bool cancelling = false)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var invoice = GlobalInvoice.Request(
                tenant.Id, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Monthly, new DateOnly(2026, 9, 1)), "F", 1, "01", "{}",
                86.21m, 13.79m, 100m, [new GlobalInvoiceSale(Guid.NewGuid(), "ORD-000001", 100m)], Guid.NewGuid(), requestedAt);
            if (cancelling)
            {
                invoice.Stamp(Guid.NewGuid().ToString(), "sw", requestedAt, "invoices/g.xml");
                invoice.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, Guid.NewGuid(), requestedAt);
            }
            context.Tenants.Add(tenant);
            context.GlobalInvoices.Add(invoice);
            await context.SaveChangesAsync();
            return tenant.Id;
        });

    private static OutboxTestHost Host(SentCommands sent)
        => OutboxTestHost.Create(configure: services =>
        {
            services.AddSingleton<IInvoicingGateway, ConfiguredGateway>();
            services.AddSingleton(sent);
            services.AddScoped<ISender, RecordingSender>();
            services.AddSingleton<InvoicingWorkerService>();
        });

    private static Task<Guid> AddInvoiceAsync(
        OutboxTestHost host, string slug, DateTimeOffset requestedAt, bool refused = false, bool cancelling = false)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var invoice = OrderInvoice.Request(
                tenant.Id, Guid.NewGuid(), "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla", "26015", "612", "G03"), "04", "{}",
                86.21m, 13.79m, 100m, Guid.NewGuid(), requestedAt);
            if (refused)
                invoice.Refuse("Rechazada", requestedAt);
            if (cancelling)
            {
                invoice.Stamp(Guid.NewGuid().ToString(), "sw", requestedAt, "invoices/x.xml");
                invoice.RequestCancellation(CfdiCancellationMotive.NotCarriedOut, Guid.NewGuid(), requestedAt);
            }
            context.Tenants.Add(tenant);
            context.OrderInvoices.Add(invoice);
            await context.SaveChangesAsync();
            return tenant.Id;
        });

    private sealed class SentCommands
    {
        public List<Guid> Stamps { get; } = [];
        public List<Guid> Checks { get; } = [];
    }

    /// <summary>Records each command with the business whose scope sent it.</summary>
    private sealed class RecordingSender(SentCommands sent, ICurrentTenantService tenant) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var list = request switch
            {
                StampPendingInvoicesCommand => sent.Stamps,
                CheckInvoiceCancellationsCommand => sent.Checks,
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            list.Add(tenant.TenantId);
            return Task.FromResult((TResponse)(object)Result.Success(0));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ConfiguredGateway : IInvoicingGateway
    {
        public bool IsConfigured => true;
        public bool Sandbox => false;
        public Task SaveCertificateAsync(byte[] certificate, byte[] privateKey, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveCertificateAsync(string certificateNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StampedCfdi> StampAsync(CfdiDocument document, Guid idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> RenderPdfAsync(string stampedXml, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CfdiCancellationOutcome> CancelAsync(string issuerRfc, string uuid, CfdiCancellationMotive motive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
