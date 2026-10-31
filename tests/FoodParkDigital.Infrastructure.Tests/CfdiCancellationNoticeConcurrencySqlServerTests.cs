using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Fiscal.Events;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.BackgroundJobs;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The notice that the SAT cancelled a CFDI (Fase 28N, ADR 0059) on SQL Server: the request that asked for the cancellation
/// and the invoicing worker that hears the SAT settle it both confirm it through <see cref="CfdiCancellations.ConfirmAsync"/>,
/// each in its own context, and each adds its notice to the same save. The row version of the invoice, the payment
/// complement and the credit note lets only the first one save, so the customer gets one notice and the outbox one email.
/// The in-memory tests throw the conflict by hand; here SQL Server refuses it.
/// </summary>
public class CfdiCancellationNoticeConcurrencySqlServerTests
{
    private const string IssuerRfc = "EKU9003173C9";
    private const string Email = "karla@ejemplo.mx";

    [SqlServerFact]
    public async Task Worker_ThatHearsTheSatAfterTheRequestConfirmedIt_ShouldLeaveOnlyTheNoticeOfTheRequest()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var clock = new MutableClock();
        var seed = await SeedAsync(database, clock.UtcNow);
        // The request hears the SAT confirm each CFDI after the worker read it as waiting, and saves before the worker.
        var request = new ConfirmedMeanwhile(database, seed.TenantId, clock);
        await using (var worker = Context(database, seed.TenantId, clock, request))
        {
            var settled = await new CheckInvoiceCancellationsCommandHandler(
                    worker, new SatCancelledThem(), new StampedXml(), clock, NullLogger<CheckInvoiceCancellationsCommandHandler>.Instance)
                .Handle(new CheckInvoiceCancellationsCommand(), CancellationToken.None);

            // SQL Server refused each save of the worker: the request had changed the row version.
            Assert.Equal(0, settled.Value);
        }

        Assert.Equal(new[] { seed.InvoiceId, seed.ComplementId, seed.CreditNoteId }.Order(), request.Notices.Keys.Order());
        await using (var context = database.CreateContext(seed.TenantId))
        {
            Cfdi[] cfdis =
            [
                await context.OrderInvoices.SingleAsync(),
                await context.PaymentComplements.SingleAsync(),
                await context.CreditNotes.SingleAsync()
            ];
            Assert.All(cfdis, cfdi => Assert.Equal(OrderInvoiceStatus.Cancelled, cfdi.Status));
            // One notice per CFDI, the request's, to the invoice's email.
            var notices = await context.CfdiEmails.ToListAsync();
            Assert.All(notices, notice => Assert.Equal((CfdiEmailKind.Cancellation, Email), (notice.Kind, notice.To)));
            Assert.Equal(request.Notices.OrderBy(n => n.Key), notices.Select(n => KeyValuePair.Create(n.CfdiId, n.Id)).OrderBy(n => n.Key));
            // And one email each in the outbox.
            var emails = await context.OutboxMessages
                .Where(m => m.Type == typeof(CfdiEmailRequestedEvent).FullName)
                .Select(m => m.Payload)
                .ToListAsync();
            Assert.Equal(3, emails.Count);
            Assert.All(request.Notices.Values, id => Assert.Single(emails, payload => payload.Contains(id.ToString())));
        }

        // The other business's cancellation waits for its own worker pass, without a notice.
        await using var foreign = database.CreateContext(seed.OtherTenantId);
        Assert.Equal(OrderInvoiceStatus.Cancelling, (await foreign.OrderInvoices.SingleAsync()).Status);
        Assert.Empty(await foreign.CfdiEmails.ToListAsync());
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid InvoiceId, Guid ComplementId, Guid CreditNoteId);

    /// <summary>
    /// Two businesses. The first sold an order on credit and invoiced it in installments (PPD) to Karla, with her email; the
    /// invoice, the complement of its payment and the credit note of what it forgave are stamped, and the business asked to
    /// cancel each one (the SAT settles each on its own). The other business asked to cancel an invoice of its own.
    /// </summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database, DateTimeOffset now)
    {
        var tenants = Enumerable.Range(0, 2).Select(_ =>
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        }).ToList();
        var userId = Guid.NewGuid();

        var order = Order(tenants[0].Id);
        var payment = order.RegisterPayment(100m, OrderPaymentMethod.Cash, now.AddDays(-3), null, null, null, null);
        order.SellOnCredit();
        var writeOff = order.WriteOff(30m, OrderWriteOffReason.WontBeCollected, null, now.AddDays(-2), "dueña");
        var invoice = OrderInvoice.Request(tenants[0].Id, order.Id, "F", 1, Receiver, "99", Document("F", 200m), 172.41m, 27.59m, 200m,
            userId, now.AddDays(-4), "PPD");
        invoice.SendTo(Email);
        invoice.Stamp(Guid.NewGuid().ToString(), "pac-1", now.AddDays(-4), "invoices/1.xml");
        var complement = PaymentComplement.Request(invoice, payment, "P", 1, 1, 200m, Document("P", 0m), null, now.AddDays(-3));
        complement.Stamp(Guid.NewGuid().ToString(), "pac-p1", now.AddDays(-3), "complements/1.xml");
        var note = CreditNote.ForWriteOff(invoice, writeOff, "NC", 1, 25.86m, 4.14m, 1, Document("NC", 30m), null, now.AddDays(-2));
        note.Stamp(Guid.NewGuid().ToString(), "pac-nc1", now.AddDays(-2), "credit-notes/1.xml");

        var otherOrder = Order(tenants[1].Id);
        otherOrder.RegisterPayment(200m, OrderPaymentMethod.Cash, now.AddDays(-3), null, null, null, null);
        var otherInvoice = OrderInvoice.Request(tenants[1].Id, otherOrder.Id, "F", 1, Receiver, "01", Document("F", 200m), 172.41m, 27.59m,
            200m, userId, now.AddDays(-3));
        otherInvoice.SendTo(Email);
        otherInvoice.Stamp(Guid.NewGuid().ToString(), "pac-2", now.AddDays(-3), "invoices/2.xml");

        foreach (var cfdi in new Cfdi[] { invoice, complement, note, otherInvoice })
            cfdi.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, userId, now.AddHours(-1));
        await using var context = database.CreateContext();
        foreach (var tenant in tenants)
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        context.Orders.AddRange(order, otherOrder);
        context.AddRange(invoice, complement, note, otherInvoice);
        await context.SaveChangesAsync();
        return new Seed(tenants[0].Id, tenants[1].Id, invoice.Id, complement.Id, note.Id);
    }

    private static CfdiReceiver Receiver => CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03");

    private static Order Order(Guid tenantId)
    {
        var order = new Order(tenantId, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla",
            "+525512345678", null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        return order;
    }

    /// <summary>The parties and total the worker asks the SAT about.</summary>
    private static string Document(string serie, decimal total) => new CfdiDocument
    {
        Serie = serie,
        Folio = "1",
        Date = DateTimeOffset.UtcNow.ToString(CfdiDocument.DateFormat),
        ExpeditionPostalCode = "26015",
        SubTotal = total,
        TotalTransferred = 0m,
        Total = total,
        Issuer = new CfdiDocumentIssuer(IssuerRfc, "Tienda Luna", "601"),
        Receiver = new CfdiDocumentReceiver(Receiver.Rfc, Receiver.Name, Receiver.PostalCode, Receiver.FiscalRegime, Receiver.CfdiUse),
        Concepts = [],
        Transfers = []
    }.ToJson();

    /// <summary>A context set up like production, which writes the domain events to the outbox in the same save.</summary>
    private static ApplicationDbContext Context(
        SqlServerTestDatabase database, Guid tenantId, IDateTimeProvider clock, params IInterceptor[] interceptors)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return database.CreateProductionLikeContext(tenant, new InMemoryApplicationDbContext.NullMediator(),
            [.. interceptors, new OutboxSaveChangesInterceptor(new NoUser(), clock, new OutboxSignal())]);
    }

    /// <summary>
    /// Right before the context it is attached to saves a CFDI it confirmed as cancelled, the request that asked for that
    /// cancellation confirms the same CFDI in its own context, with its notice, and saves first. Once per CFDI.
    /// </summary>
    private sealed class ConfirmedMeanwhile(SqlServerTestDatabase database, Guid tenantId, IDateTimeProvider clock) : SaveChangesInterceptor
    {
        /// <summary>The notice the request saved, by CFDI.</summary>
        public Dictionary<Guid, Guid> Notices { get; } = [];

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var confirmed = eventData.Context!.ChangeTracker.Entries<Cfdi>()
                .Where(entry => entry.State == EntityState.Modified && entry.Entity.Status == OrderInvoiceStatus.Cancelled
                    && !Notices.ContainsKey(entry.Entity.Id))
                .Select(entry => entry.Entity)
                .ToList();
            foreach (var mine in confirmed)
            {
                await using var request = Context(database, tenantId, clock);
                Cfdi theirs = mine switch
                {
                    OrderInvoice => await request.OrderInvoices.SingleAsync(i => i.Id == mine.Id, cancellationToken),
                    PaymentComplement => await request.PaymentComplements.SingleAsync(c => c.Id == mine.Id, cancellationToken),
                    _ => await request.CreditNotes.SingleAsync(n => n.Id == mine.Id, cancellationToken)
                };
                await CfdiCancellations.ConfirmAsync(request, theirs, clock.UtcNow, cancellationToken);
                var notice = request.ChangeTracker.Entries<CfdiEmail>().Single(entry => entry.State == EntityState.Added).Entity;
                await request.SaveChangesAsync(cancellationToken);
                Notices.Add(mine.Id, notice.Id);
            }

            return result;
        }
    }

    /// <summary>The SAT answers that every CFDI it is asked about is cancelled.</summary>
    private sealed class SatCancelledThem : IInvoicingGateway
    {
        public bool IsConfigured => true;
        public bool Sandbox => false;
        public Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new CfdiSatStatus(CfdiSatState.Cancelled, CancellationRefused: false));

        public Task SaveCertificateAsync(byte[] certificate, byte[] privateKey, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveCertificateAsync(string certificateNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StampedCfdi> StampAsync(CfdiDocument document, Guid idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StampedCfdi?> FindStampedAsync(string uuid, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> RenderPdfAsync(string stampedXml, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CfdiCancellationOutcome> CancelAsync(
            string issuerRfc, string uuid, CfdiCancellationMotive motive, string? replacementUuid, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    /// <summary>The stamped XML of every CFDI, with the seal the worker sends the SAT the end of.</summary>
    private sealed class StampedXml : IPrivateFileStorage
    {
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("""<Comprobante Sello="QUJDREVGR0hJSktMTU5PUA==" />""")));
    }

    /// <summary>The worker and the request run without a person in these tests.</summary>
    private sealed class NoUser : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => null;
        public string? Role => null;
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }
}
