using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Invoicing;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// The invoicing worker (Fase 28A; global invoices too, Fase 28B, and payment complements, Fase 28C) finds the businesses with
/// invoices still stamping within the PAC's window, the ones whose payments owe a complement, and every half hour the ones
/// with cancellations waiting for the SAT, and sends each one its command in its own scope.
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

    [Fact]
    public async Task Pass_ShouldAskAgain_ForTheGlobalInvoicesStillStamping_WithinThePacsWindow_OnceForABusinessWithBothKinds()
    {
        var sent = new SentCommands();
        await using var host = Host(sent);
        var now = host.Clock.UtcNow;
        var both = await AddInvoiceAsync(host, "pedido-y-global", requestedAt: now.AddMinutes(-5));
        await AddGlobalInvoiceAsync(host, "pedido-y-global", requestedAt: now.AddMinutes(-9), tenantId: both);
        await AddGlobalInvoiceAsync(host, "global-reciente", requestedAt: now.AddSeconds(-20));
        await AddGlobalInvoiceAsync(host, "global-vieja", requestedAt: now - StampPendingInvoicesCommandHandler.RetryWindow - TimeSpan.FromMinutes(1));
        await AddGlobalInvoiceAsync(host, "global-rechazada", requestedAt: now.AddMinutes(-5), refused: true);
        var worker = host.Get<InvoicingWorkerService>();

        Assert.Equal(1, await worker.RunPassAsync(CancellationToken.None));

        Assert.Equal([both], sent.Stamps);
        Assert.Empty(sent.Checks);
    }

    [Fact]
    public async Task Pass_ShouldFindTheBusinessesWhosePaymentsOweAComplement_AndTheComplementsStampingOrCancelling()
    {
        var sent = new SentCommands();
        await using var host = Host(sent);
        var now = host.Clock.UtcNow;
        var owing = await AddInstallmentsAsync(host, "debe-complemento", paidAt: now.AddDays(-3));
        // Paid with «Otro», of an invoice paid in one exhibition, or already with a complement (even refused): nothing owed.
        await AddInstallmentsAsync(host, "cobro-otro", paidAt: now.AddDays(-4), method: Domain.Orders.Enums.OrderPaymentMethod.Other);
        await AddInstallmentsAsync(host, "factura-pue", paidAt: now.AddDays(-4), inInstallments: false);
        await AddInstallmentsAsync(host, "complemento-rechazado", paidAt: now.AddDays(-4), complement: c => c.Refuse("CRP20206", now));
        // They owe one, but cannot issue it until their CSD seals again: they never take the places of the ones that can.
        await AddInstallmentsAsync(host, "sin-csd", paidAt: now.AddDays(-9), csd: Csd.None);
        await AddInstallmentsAsync(host, "csd-vencido", paidAt: now.AddDays(-9), csd: Csd.Expired);
        var stamping = await AddInstallmentsAsync(host, "complemento-timbrando", paidAt: now.AddDays(-2), complement: _ => { }, requestedAt: now.AddMinutes(-5));
        var cancelling = await AddInstallmentsAsync(host, "complemento-cancelandose", paidAt: now.AddDays(-2), complement: c =>
        {
            c.Stamp(Guid.NewGuid().ToString(), "sw", now, "invoices/p.xml");
            c.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, Guid.NewGuid(), now);
        });
        var worker = host.Get<InvoicingWorkerService>();

        Assert.Equal(3, await worker.RunPassAsync(CancellationToken.None));

        Assert.Equal([owing], sent.Complements);
        Assert.Equal([stamping], sent.Stamps);
        Assert.Equal([cancelling], sent.Checks);
    }

    [Fact]
    public async Task Pass_ShouldPutTheBusinessesWhoseComplementsStampedNothing_AfterTheOthers_AndTurnThemAround()
    {
        var sent = new SentCommands();
        await using var host = Host(sent, maxBusinessesPerPass: 1);
        var now = host.Clock.UtcNow;
        // Their oldest payments are never issued here (their invoices were stamped with the RFC Mi negocio had before, say):
        // their passes stamp nothing.
        var stuck = await AddInstallmentsAsync(host, "rfc-anterior", paidAt: now.AddDays(-9));
        var alsoStuck = await AddInstallmentsAsync(host, "otro-rfc-anterior", paidAt: now.AddDays(-8));
        var owing = await AddInstallmentsAsync(host, "debe-complemento", paidAt: now.AddDays(-3));
        sent.Stamping.Add(owing);
        var worker = host.Get<InvoicingWorkerService>();

        async Task PassAsync()
        {
            await worker.RunPassAsync(CancellationToken.None);
            host.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        // The longest waiting goes first until a pass of theirs stamps nothing; then the others go before them, with one
        // place per pass, so the business that can issue its complement is not kept waiting.
        await PassAsync();
        await PassAsync();
        await PassAsync();
        Assert.Equal([stuck, alsoStuck, owing], sent.Complements);

        // While nothing is stamped, they take turns: the least recently tried first.
        sent.Stamping.Clear();
        await PassAsync();
        await PassAsync();
        await PassAsync();
        Assert.Equal([stuck, alsoStuck, owing, owing, stuck, alsoStuck], sent.Complements);
    }

    [Fact]
    public async Task Pass_ShouldFindTheBusinessesWhoseAdjustmentsOweACreditNote_AndTheCreditNotesStampingOrCancelling()
    {
        var sent = new SentCommands();
        await using var host = Host(sent);
        var now = host.Clock.UtcNow;
        var refunding = await AddAdjustmentAsync(host, "debe-nota-reembolso", now.AddDays(-3), writeOff: false);
        var forgiving = await AddAdjustmentAsync(host, "debe-nota-condonacion", now.AddDays(-2), writeOff: true);
        // Refunded with «Otro», of a cancelled order (its invoice is cancelled instead), a write-off undone, or already with a
        // credit note (even refused): nothing owed.
        await AddAdjustmentAsync(host, "reembolso-otro", now.AddDays(-4), writeOff: false, method: Domain.Orders.Enums.OrderPaymentMethod.Other);
        await AddAdjustmentAsync(host, "pedido-cancelado", now.AddDays(-4), writeOff: false, orderCancelled: true);
        await AddAdjustmentAsync(host, "condonacion-deshecha", now.AddDays(-4), writeOff: true, reversed: true);
        await AddAdjustmentAsync(host, "nota-rechazada", now.AddDays(-4), writeOff: false, note: n => n.Refuse("CFDI40108", now));
        // It owes one, but cannot issue it until its CSD seals again.
        await AddAdjustmentAsync(host, "nota-sin-csd", now.AddDays(-9), writeOff: false, csd: Csd.None);
        var stamping = await AddAdjustmentAsync(host, "nota-timbrando", now.AddDays(-2), writeOff: false, note: _ => { }, requestedAt: now.AddMinutes(-5));
        var cancelling = await AddAdjustmentAsync(host, "nota-cancelandose", now.AddDays(-2), writeOff: true, note: n =>
        {
            n.Stamp(Guid.NewGuid().ToString(), "sw", now, "invoices/nc.xml");
            n.RequestCancellation(CfdiCancellationMotive.NotCarriedOut, Guid.NewGuid(), now);
        });
        var worker = host.Get<InvoicingWorkerService>();

        Assert.Equal(4, await worker.RunPassAsync(CancellationToken.None));

        Assert.Equal([refunding, forgiving], sent.CreditNotes);
        Assert.Equal([stamping], sent.Stamps);
        Assert.Equal([cancelling], sent.Checks);
        Assert.Empty(sent.Complements);
    }

    private enum Csd { Valid, Expired, None }

    /// <summary>
    /// A business with a $100 sale, a payment of $40 and its stamped invoice (in installments unless said otherwise), its CSD
    /// (valid unless said otherwise), and the complement of that payment when <paramref name="complement"/> says what
    /// happened to it.
    /// </summary>
    private static Task<Guid> AddInstallmentsAsync(
        OutboxTestHost host,
        string slug,
        DateTimeOffset paidAt,
        Domain.Orders.Enums.OrderPaymentMethod method = Domain.Orders.Enums.OrderPaymentMethod.Cash,
        bool inInstallments = true,
        Action<PaymentComplement>? complement = null,
        DateTimeOffset? requestedAt = null,
        Csd csd = Csd.Valid)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            if (csd != Csd.None)
            {
                var profile = InvoicingProfile.Create(tenant.Id);
                var now = host.Clock.UtcNow;
                var uploadedAt = csd == Csd.Valid ? now.AddDays(-30) : now.AddYears(-4);
                profile.UseCertificate(
                    "30001000000500003416", "EKU9003173C9", uploadedAt.AddDays(-1), uploadedAt.AddYears(4).AddDays(csd == Csd.Valid ? 0 : -60),
                    Guid.NewGuid(), uploadedAt);
                context.InvoicingProfiles.Add(profile);
            }
            var order = new Domain.Orders.Entities.Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Retail,
                Domain.Orders.Enums.OrderType.Retail, "Karla", null, null, null, pricesIncludeTax: true);
            order.AddItem(Guid.NewGuid(), "Café", 1, 100m);
            var payment = order.RegisterPayment(40m, method, paidAt, null, null, null, null);
            var invoice = OrderInvoice.Request(
                tenant.Id, order.Id, "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla", "26015", "612", "G03"),
                inInstallments ? "99" : "04", "{}", 86.21m, 13.79m, 100m, Guid.NewGuid(), paidAt.AddDays(-1),
                inInstallments ? "PPD" : "PUE");
            invoice.Stamp(Guid.NewGuid().ToString(), "sw", paidAt.AddDays(-1), "invoices/f.xml");
            context.Tenants.Add(tenant);
            context.Orders.Add(order);
            context.OrderInvoices.Add(invoice);
            if (complement is not null)
            {
                var issued = PaymentComplement.Request(invoice, payment, "P", 1, 1, 100m, "{}", null, requestedAt ?? paidAt);
                complement(issued);
                context.PaymentComplements.Add(issued);
            }
            await context.SaveChangesAsync();
            return tenant.Id;
        });

    /// <summary>
    /// A business with a $100 sale on credit, paid $40 and invoiced (stamped; paid in one exhibition, so its payment owes no
    /// complement and only the credit note counts), its CSD (valid unless said
    /// otherwise), and a $10 refund (by card unless said otherwise) or a $20 write-off of that sale, with its credit note when
    /// <paramref name="note"/> says what happened to it.
    /// </summary>
    private static Task<Guid> AddAdjustmentAsync(
        OutboxTestHost host,
        string slug,
        DateTimeOffset at,
        bool writeOff,
        Domain.Orders.Enums.OrderPaymentMethod method = Domain.Orders.Enums.OrderPaymentMethod.Card,
        bool reversed = false,
        Action<CreditNote>? note = null,
        DateTimeOffset? requestedAt = null,
        Csd csd = Csd.Valid,
        bool orderCancelled = false)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            if (csd != Csd.None)
            {
                var profile = InvoicingProfile.Create(tenant.Id);
                var uploadedAt = host.Clock.UtcNow.AddDays(-30);
                profile.UseCertificate("30001000000500003416", "EKU9003173C9", uploadedAt.AddDays(-1), uploadedAt.AddYears(4), Guid.NewGuid(), uploadedAt);
                context.InvoicingProfiles.Add(profile);
            }
            var order = new Domain.Orders.Entities.Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Retail,
                Domain.Orders.Enums.OrderType.Retail, "Karla", "+525512345678", null, null, pricesIncludeTax: true);
            order.AddItem(Guid.NewGuid(), "Café", 1, 100m);
            order.RegisterPayment(40m, Domain.Orders.Enums.OrderPaymentMethod.Cash, at.AddDays(-2), null, null, null, null);
            order.SellOnCredit();
            var invoice = OrderInvoice.Request(
                tenant.Id, order.Id, "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla", "26015", "612", "G03"),
                "04", "{}", 86.21m, 13.79m, 100m, Guid.NewGuid(), at.AddDays(-1), "PUE");
            invoice.Stamp(Guid.NewGuid().ToString(), "sw", at.AddDays(-1), "invoices/f.xml");
            CreditNote? issued = null;
            if (writeOff)
            {
                var forgiven = order.WriteOff(20m, Domain.Orders.Enums.OrderWriteOffReason.WontBeCollected, null, at, "dueña");
                if (note is not null)
                    issued = CreditNote.ForWriteOff(invoice, forgiven, "NC", 1, 17.24m, 2.76m, 0, "{}", null, requestedAt ?? at);
                if (reversed)
                    order.ReverseWriteOff(forgiven.Id, at.AddHours(1), "dueña");
            }
            else
            {
                var refund = order.RegisterRefund(10m, method, "Devolución", at, "cajero", null, null, false, []);
                if (note is not null)
                    issued = CreditNote.ForRefund(invoice, refund, "NC", 1, 8.62m, 1.38m, 0, "{}", null, requestedAt ?? at);
            }
            context.Tenants.Add(tenant);
            context.Orders.Add(order);
            // As «Anular venta» leaves an order it voided before delivering it.
            if (orderCancelled)
                context.Entry(order).Property(o => o.Status).CurrentValue = Domain.Orders.Enums.OrderStatus.Cancelled;
            context.OrderInvoices.Add(invoice);
            if (issued is not null)
            {
                note!(issued);
                context.CreditNotes.Add(issued);
            }
            await context.SaveChangesAsync();
            return tenant.Id;
        });

    /// <summary>A global invoice of a new business, or of <paramref name="tenantId"/>'s.</summary>
    private static Task<Guid> AddGlobalInvoiceAsync(
        OutboxTestHost host, string slug, DateTimeOffset requestedAt, bool refused = false, bool cancelling = false, Guid? tenantId = null)
        => host.InScopeAsync(async services =>
        {
            var tenant = tenantId is null ? new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com") : null;
            var id = tenant?.Id ?? tenantId!.Value;
            services.GetRequiredService<ICurrentTenantService>().SetTenant(id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var invoice = GlobalInvoice.Request(
                id, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Monthly, new DateOnly(2026, 9, 1)), "F", 2, "01", "{}",
                86.21m, 13.79m, 100m, [new GlobalInvoiceSale(Guid.NewGuid(), "ORD-000002", 100m)], Guid.NewGuid(), requestedAt);
            if (refused)
                invoice.Refuse("Rechazada", requestedAt);
            if (cancelling)
            {
                invoice.Stamp(Guid.NewGuid().ToString(), "sw", requestedAt, "invoices/g.xml");
                invoice.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, Guid.NewGuid(), requestedAt);
            }
            if (tenant is not null)
                context.Tenants.Add(tenant);
            context.GlobalInvoices.Add(invoice);
            await context.SaveChangesAsync();
            return id;
        });

    private static OutboxTestHost Host(SentCommands sent, int? maxBusinessesPerPass = null)
        => OutboxTestHost.Create(configure: services =>
        {
            if (maxBusinessesPerPass is { } max)
                services.Configure<BackgroundJobOptions>(options => options.Invoicing.MaxBusinessesPerPass = max);
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
        public List<Guid> Complements { get; } = [];
        public List<Guid> CreditNotes { get; } = [];
        /// <summary>The businesses whose complements pass stamps one.</summary>
        public HashSet<Guid> Stamping { get; } = [];
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
                IssuePendingPaymentComplementsCommand => sent.Complements,
                IssuePendingCreditNotesCommand => sent.CreditNotes,
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            list.Add(tenant.TenantId);
            var settled = request is IssuePendingPaymentComplementsCommand && sent.Stamping.Contains(tenant.TenantId) ? 1 : 0;
            return Task.FromResult((TResponse)(object)Result.Success(settled));
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
        public Task<CfdiCancellationOutcome> CancelAsync(
            string issuerRfc, string uuid, CfdiCancellationMotive motive, string? replacementUuid, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
