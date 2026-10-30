using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Invoicing;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.BackgroundJobs;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The invoicing worker on SQL Server (Fase 28G, ADR 0051): its cancellations pass finds the businesses with a replaced
/// invoice whose replacement was stamped less than 72 hours ago and that was not asked to be cancelled since, and each
/// business's check asks the SAT to cancel those invoices with motive 01 and their replacement's UUID. The queries run as
/// SQL Server translates them, which the in-memory worker tests do not show.
/// </summary>
public class InvoiceSubstitutionWorkerSqlServerTests
{
    private const string IssuerRfc = "EKU9003173C9";

    private static int _certificates;

    [SqlServerFact]
    public async Task CancellationsPass_ShouldAskTheSatToCancelWithRelation_OnlyTheReplacedInvoicesWaitingForIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var clock = new MutableClock();
        var now = clock.UtcNow;
        var waiting = await AddReplacedInvoiceAsync(database, now, replacementStampedAt: now.AddHours(-1));
        // The customer refused a cancellation without relation before it was replaced: that one does not count.
        var refusedBefore = await AddReplacedInvoiceAsync(database, now, replacementStampedAt: now.AddHours(-2),
            refusedCancellationAt: now.AddDays(-3));
        // Its replacement is older than 72 hours, or the customer refused its cancellation with relation: a person asks.
        var late = await AddReplacedInvoiceAsync(database, now,
            replacementStampedAt: now - OrderInvoiceSubstitutions.CancellationRetryWindow - TimeSpan.FromMinutes(1));
        var refusedAfter = await AddReplacedInvoiceAsync(database, now, replacementStampedAt: now.AddHours(-5),
            refusedCancellationAt: now.AddHours(-4));
        var gateway = new RecordingGateway();
        await using var services = Services(database, clock, gateway);

        Assert.Equal(2, await services.GetRequiredService<InvoicingWorkerService>().RunPassAsync(CancellationToken.None));

        // The longest waiting first, each with its replacement as the folio of substitution.
        Assert.Equal(
            [
                (IssuerRfc, refusedBefore.OriginalUuid, CfdiCancellationMotive.ErrorsWithReplacement, refusedBefore.ReplacementUuid),
                (IssuerRfc, waiting.OriginalUuid, CfdiCancellationMotive.ErrorsWithReplacement, waiting.ReplacementUuid)
            ],
            gateway.Cancellations);
        foreach (var (replaced, status, motive) in new[]
        {
            (waiting, OrderInvoiceStatus.Cancelled, CfdiCancellationMotive.ErrorsWithReplacement),
            (refusedBefore, OrderInvoiceStatus.Cancelled, CfdiCancellationMotive.ErrorsWithReplacement),
            (late, OrderInvoiceStatus.Stamped, (CfdiCancellationMotive?)null),
            (refusedAfter, OrderInvoiceStatus.Stamped, CfdiCancellationMotive.ErrorsWithReplacement)
        })
        {
            await using var context = database.CreateContext(replaced.TenantId);
            var original = await context.OrderInvoices.SingleAsync(i => i.Id == replaced.OriginalId);
            Assert.Equal((status, motive, replaced.ReplacementId), (original.Status, original.CancellationMotive, original.ReplacedByInvoiceId!.Value));
        }
    }

    private sealed record Replaced(Guid TenantId, Guid OriginalId, string OriginalUuid, Guid ReplacementId, string ReplacementUuid);

    /// <summary>
    /// A business with its CSD and an order paid in cash whose stamped invoice another one replaces, stamped at
    /// <paramref name="replacementStampedAt"/>. With <paramref name="refusedCancellationAt"/>, the customer refused a
    /// cancellation asked for then: without relation (02) before the replacement was stamped, with relation (01) after.
    /// </summary>
    private static async Task<Replaced> AddReplacedInvoiceAsync(
        SqlServerTestDatabase database, DateTimeOffset now, DateTimeOffset replacementStampedAt, DateTimeOffset? refusedCancellationAt = null)
    {
        var slug = $"tienda-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        var profile = InvoicingProfile.Create(tenant.Id);
        // Each business uploads its own CSD: certificate numbers are unique.
        profile.UseCertificate($"3000100000050000{Interlocked.Increment(ref _certificates):D4}", IssuerRfc, now.AddDays(-31), now.AddYears(4), Guid.NewGuid(), now.AddDays(-30));
        var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla",
            "+525512345678", null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        order.RegisterPayment(200m, OrderPaymentMethod.Cash, now.AddDays(-10), null, null, null, null);
        var original = Invoice(tenant.Id, order.Id, 1, now.AddDays(-10));
        original.Stamp(Guid.NewGuid().ToString(), "pac-1", now.AddDays(-10), "invoices/1.xml");
        if (refusedCancellationAt < replacementStampedAt)
        {
            original.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, Guid.NewGuid(), refusedCancellationAt.Value);
            original.KeepAfterRefusedCancellation("El cliente rechazó la cancelación.");
        }
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail), profile, order, original);
            await context.SaveChangesAsync();
        }

        // As a substitution saves them: the original stops counting as the order's invoice before the replacement is added.
        var replacement = Invoice(tenant.Id, order.Id, 2, replacementStampedAt.AddMinutes(-1));
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.OrderInvoices.Attach(original);
            original.ReplaceWith(replacement);
            await context.SaveChangesAsync();
            replacement.Stamp(Guid.NewGuid().ToString(), "pac-2", replacementStampedAt, "invoices/2.xml");
            context.OrderInvoices.Add(replacement);
            await context.SaveChangesAsync();
            if (refusedCancellationAt >= replacementStampedAt)
            {
                original.RequestCancellation(CfdiCancellationMotive.ErrorsWithReplacement, Guid.NewGuid(), refusedCancellationAt.Value);
                original.KeepAfterRefusedCancellation("El cliente rechazó la cancelación.");
                await context.SaveChangesAsync();
            }
        }
        return new Replaced(tenant.Id, original.Id, original.Uuid!, replacement.Id, replacement.Uuid!);
    }

    private static OrderInvoice Invoice(Guid tenantId, Guid orderId, long folio, DateTimeOffset requestedAt)
    {
        var receiver = CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03");
        var document = new CfdiDocument
        {
            Serie = "F",
            Folio = folio.ToString(),
            Date = requestedAt.ToString(CfdiDocument.DateFormat),
            ExpeditionPostalCode = "26015",
            SubTotal = 172.41m,
            TotalTransferred = 27.59m,
            Total = 200m,
            Issuer = new CfdiDocumentIssuer(IssuerRfc, "Tienda Luna", "601"),
            Receiver = new CfdiDocumentReceiver(receiver.Rfc, receiver.Name, receiver.PostalCode, receiver.FiscalRegime, receiver.CfdiUse),
            Concepts = [],
            Transfers = []
        };
        return OrderInvoice.Request(
            tenantId, orderId, "F", folio, receiver, "01", document.ToJson(), 172.41m, 27.59m, 200m, Guid.NewGuid(), requestedAt);
    }

    /// <summary>The worker over this database, with the real cancellations check in each business's scope.</summary>
    private static ServiceProvider Services(SqlServerTestDatabase database, MutableClock clock, RecordingGateway gateway)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider>(clock);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();
        services.AddScoped<IBackgroundExecutionContext, BackgroundExecutionContext>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped(sp => database.CreateProductionLikeContext(
            sp.GetRequiredService<ICurrentTenantService>(), new InMemoryApplicationDbContext.NullMediator()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<IInvoicingGateway>(gateway);
        services.AddSingleton<IPrivateFileStorage, NoFiles>();
        services.AddScoped<ISender, CancellationsSender>();
        services.AddSingleton<InvoicingWorkerService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Sends <see cref="CheckInvoiceCancellationsCommand"/> to its handler, in the scope that sends it.</summary>
    private sealed class CancellationsSender(IServiceProvider services) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is not CheckInvoiceCancellationsCommand command)
                throw new NotSupportedException(request.GetType().Name);
            var handler = ActivatorUtilities.CreateInstance<CheckInvoiceCancellationsCommandHandler>(services);
            return (TResponse)(object)await handler.Handle(command, cancellationToken);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>The SAT cancels every invoice it is asked to; each request is recorded.</summary>
    private sealed class RecordingGateway : IInvoicingGateway
    {
        public List<(string IssuerRfc, string Uuid, CfdiCancellationMotive Motive, string? ReplacementUuid)> Cancellations { get; } = [];
        public bool IsConfigured => true;
        public bool Sandbox => false;
        public Task SaveCertificateAsync(byte[] certificate, byte[] privateKey, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveCertificateAsync(string certificateNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StampedCfdi> StampAsync(CfdiDocument document, Guid idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StampedCfdi?> FindStampedAsync(string uuid, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<byte[]> RenderPdfAsync(string stampedXml, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CfdiCancellationOutcome> CancelAsync(
            string issuerRfc, string uuid, CfdiCancellationMotive motive, string? replacementUuid, CancellationToken cancellationToken)
        {
            Cancellations.Add((issuerRfc, uuid, motive, replacementUuid));
            return Task.FromResult(CfdiCancellationOutcome.Cancelled);
        }
    }

    private sealed class NoFiles : IPrivateFileStorage
    {
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
