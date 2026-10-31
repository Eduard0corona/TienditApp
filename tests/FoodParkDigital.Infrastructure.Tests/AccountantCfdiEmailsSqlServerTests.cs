using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The month before to the accountant, on its own, on SQL Server (Fase 28O, ADR 0060): the invoicing worker finds the active
/// businesses that asked for it and have not queued it these days, and each one queues the month once when it has CFDI, even
/// when two workers do it at the same moment (the unique index keeps one). The queries run as SQL Server translates them,
/// which the in-memory worker tests do not show.
/// </summary>
public class AccountantCfdiEmailsSqlServerTests
{
    private const string Accountant = "contador@despacho.mx";

    // 9:00 of the first of November in Mexico City (UTC-6).
    private static readonly DateTimeOffset NovemberFirst = new(2026, 11, 1, 15, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task MonthlyPass_ShouldQueueTheMonthBefore_OnceForEachActiveBusinessThatAskedForIt_WithCfdi()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var invoiced = await AddBusinessAsync(database, monthly: true, invoicedIn: 10);
        var withoutCfdi = await AddBusinessAsync(database, monthly: true, invoicedIn: null);
        // Its CFDI are of September: October has none to send.
        var september = await AddBusinessAsync(database, monthly: true, invoicedIn: 9);
        var suspended = await AddBusinessAsync(database, monthly: true, invoicedIn: 10, TenantStatus.Suspended);
        var byHand = await AddBusinessAsync(database, monthly: false, invoicedIn: 10);
        var clock = new MutableClock { UtcNow = NovemberFirst };
        await using var services = Services(database, clock);
        var worker = services.GetRequiredService<InvoicingWorkerService>();

        Assert.Equal(3, await worker.RunPassAsync(CancellationToken.None));

        await using (var context = database.CreateContext(invoiced))
        {
            var email = await context.CfdiMonthEmails.SingleAsync();
            Assert.Equal((2026, 10, Accountant, (Guid?)null, NovemberFirst), (email.Year, email.Month, email.To, email.RequestedByUserId, email.RequestedAt));
        }
        foreach (var tenantId in new[] { withoutCfdi, september, suspended, byHand })
        {
            await using var context = database.CreateContext(tenantId);
            Assert.Empty(await context.CfdiMonthEmails.ToListAsync());
        }

        // An hour later the one that queued it is not looked up again; the others are, and still queue nothing.
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, await worker.RunPassAsync(CancellationToken.None));
        await using (var context = database.CreateContext(invoiced))
            Assert.Equal(1, await context.CfdiMonthEmails.CountAsync());
    }

    [SqlServerFact]
    public async Task MonthlyCommand_ShouldQueueTheMonthOnce_WhenAnotherWorkerQueuesItAtTheSameMoment()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await AddBusinessAsync(database, monthly: true, invoicedIn: 10);
        var meanwhile = new QueuedMeanwhile(database, tenantId);
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        await using var context = database.CreateProductionLikeContext(tenant, new InMemoryApplicationDbContext.NullMediator(), meanwhile);

        var queued = await new QueueMonthlyAccountantEmailCommandHandler(context, tenant, new MutableClock { UtcNow = NovemberFirst })
            .Handle(new QueueMonthlyAccountantEmailCommand(), CancellationToken.None);

        Assert.True(queued.IsSuccess, queued.Error);
        Assert.Equal(0, queued.Value);
        Assert.False(meanwhile.Armed);
        await using var fresh = database.CreateContext(tenantId);
        Assert.Equal(meanwhile.TheirsId, (await fresh.CfdiMonthEmails.SingleAsync()).Id);
    }

    /// <summary>
    /// A business in Mexico City with its accountant's email and, when <paramref name="invoicedIn"/>, the stamped invoice of a
    /// paid order dated the 15th of that month of 2026.
    /// </summary>
    private static async Task<Guid> AddBusinessAsync(
        SqlServerTestDatabase database, bool monthly, int? invoicedIn, TenantStatus status = TenantStatus.Active)
    {
        var slug = $"tienda-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        tenant.SetStatus(status);
        var profile = InvoicingProfile.Create(tenant.Id);
        profile.UseAccountant(Accountant, monthly);
        await using var context = database.CreateContext(tenant.Id);
        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail), profile);
        if (invoicedIn is { } month)
        {
            var at = new DateTimeOffset(2026, month, 15, 18, 0, 0, TimeSpan.Zero);
            var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla", "+525512345678",
                null, null, pricesIncludeTax: true);
            order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
            order.RegisterPayment(200m, OrderPaymentMethod.Cash, at, null, null, null, null);
            var invoice = OrderInvoice.Request(
                tenant.Id, order.Id, "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"), "01", "{}",
                172.41m, 27.59m, 200m, Guid.NewGuid(), at);
            invoice.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "pac", at, "cfdi/f1.xml");
            context.AddRange(order, invoice);
        }
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static ServiceProvider Services(SqlServerTestDatabase database, MutableClock clock)
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
        services.AddSingleton<IInvoicingGateway, ConfiguredGateway>();
        services.AddScoped<ISender, MonthlySender>();
        services.AddSingleton<InvoicingWorkerService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Another worker queues the same month while this one is about to save it.</summary>
    private sealed class QueuedMeanwhile(SqlServerTestDatabase database, Guid tenantId) : SaveChangesInterceptor
    {
        public bool Armed { get; private set; } = true;
        public Guid TheirsId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed)
                return result;
            Armed = false;
            await using var other = database.CreateContext(tenantId);
            var theirs = CfdiMonthEmail.Request(tenantId, 2026, 10, Accountant, null, NovemberFirst);
            other.CfdiMonthEmails.Add(theirs);
            await other.SaveChangesAsync(cancellationToken);
            TheirsId = theirs.Id;
            return result;
        }
    }

    /// <summary>Sends <see cref="QueueMonthlyAccountantEmailCommand"/> to its handler, in the scope that sends it; nothing else is due.</summary>
    private sealed class MonthlySender(IServiceProvider services) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is not QueueMonthlyAccountantEmailCommand command)
                throw new NotSupportedException(request.GetType().Name);
            var handler = ActivatorUtilities.CreateInstance<QueueMonthlyAccountantEmailCommandHandler>(services);
            return (TResponse)(object)await handler.Handle(command, cancellationToken);
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
        public Task<StampedCfdi?> FindStampedAsync(string uuid, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> RenderPdfAsync(string stampedXml, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CfdiCancellationOutcome> CancelAsync(
            string issuerRfc, string uuid, Domain.Fiscal.Enums.CfdiCancellationMotive motive, string? replacementUuid, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
