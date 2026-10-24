using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The credit note (CFDI de egreso, Fase 28D, ADR 0048) of each refund and each write-off of an invoiced order: TienditApp
/// issues it on its own, related to the invoice, for exactly what was given back or forgiven, with its IVA; a write-off's
/// lowers what the invoice in installments still owes, a refund's does not; a person issues one again or cancels one; and
/// neither the invoice is cancelled nor the write-off undone while it stands.
/// </summary>
public class CreditNoteFlowTests
{
    private const string Rfc = "EKU9003173C9";
    private const string OtherRfc = "XIA190128J61";
    private const string CustomerPhone = "+525512345678";

    private static readonly CfdiReceiverInput Receiver = new()
    {
        Rfc = "xiqb891116qe4",
        Name = "Karla Fuente Nolasco",
        PostalCode = "26015",
        FiscalRegime = "612",
        CfdiUse = "G03"
    };

    [Fact]
    public async Task Worker_ShouldIssueTheCreditNoteOfARefund_RelatedToTheInvoice_WithTheRefundsFormAndItsIva()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        var invoice = (await book.IssueAsync(sale.Id)).Value;
        var refund = await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);

        Assert.Equal(1, (await book.IssuePendingAsync()).Value);

        var note = Assert.Single(await book.Context.CreditNotes.ToListAsync());
        Assert.Equal(("NC-1", OrderInvoiceStatus.Stamped, refund.Id, invoice.Id, "04", 17.24m, 2.76m, 20m, 0, true),
            (note.Number, note.Status, note.OrderRefundId, note.OrderInvoiceId, note.PaymentForm, note.SubTotal, note.TaxTotal,
                note.Total, note.AfterInstallment, note.RequestedByUserId is null));
        var document = book.Gateway.StampRequests[^1].Document;
        Assert.Equal(("E", "PUE", "04", "MXN", 17.24m, 2.76m, 20m),
            (document.VoucherType, document.PaymentMethod, document.PaymentForm, document.Currency, document.SubTotal,
                document.TotalTransferred, document.Total));
        Assert.Equal(("01", invoice.Uuid), (document.Related!.RelationType, Assert.Single(document.Related.Uuids)));
        // The invoice's receiver, with the use of a credit note.
        Assert.Equal(("XIQB891116QE4", "G02", Rfc), (document.Receiver.Rfc, document.Receiver.CfdiUse, document.Issuer.Rfc));
        var concept = Assert.Single(document.Concepts);
        Assert.Equal(("84111506", "ACT", "Devolución", 17.24m, "02"),
            (concept.ProductCode, concept.UnitCode, concept.Description, concept.Amount, concept.TaxObject));
        Assert.Equal((17.24m, 0.16m, 2.76m), (concept.Transfer!.Base, concept.Transfer.Rate, concept.Transfer.Amount));

        // Nothing new: nothing is issued twice.
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Adjustments);
        Assert.Equal((CreditNoteAdjustment.Refund, refund.Id, 20m, false, false), (shown.Kind, shown.AdjustmentId, shown.Amount, shown.CanIssue,
            shown.AwaitsAutomaticIssue));
        Assert.Equal("NC-1", Assert.Single(shown.CreditNotes).Number);
    }

    [Fact]
    public async Task Sale_WithSomethingGivenBack_ShouldBeInvoicedWhole_AndItsCreditNoteFollow()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        var refund = await book.RefundAsync(sale.Id, 30m, OrderPaymentMethod.Card);

        // The 28A block is lifted: the invoice goes for the whole sale, and says so.
        Assert.Null((await book.InvoicingAsync(sale.Id)).Problem);
        Assert.Contains(GetOrderCfdiDraftQueryHandler.RefundedWarning(30m), (await book.DraftAsync(sale.Id)).Warnings);
        // Before the invoice, the refund has nothing to relate to.
        Assert.Equal(CreditNotes.NoStampedInvoiceMessage, (await book.IssueCreditNoteAsync(CreditNoteAdjustment.Refund, refund.Id)).Error);
        var invoice = (await book.IssueAsync(sale.Id)).Value;
        Assert.Equal(100m, invoice.Total);

        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Adjustments);
        Assert.Equal((true, true, null), (shown.CanIssue, shown.AwaitsAutomaticIssue, shown.Problem));
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        Assert.Equal(30m, (await book.Context.CreditNotes.SingleAsync()).Total);
    }

    [Fact]
    public async Task CreditNote_OfAWriteOff_ShouldBeByCondonacion_AndLowerWhatTheInvoiceInInstallmentsOwes()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        var invoice = (await book.IssueAsync(sale.Id)).Value;
        Assert.Equal("PPD", invoice.PaymentMethod);
        Assert.Equal(1, (await book.IssuePendingComplementsAsync()).Value);

        var writeOff = await book.WriteOffAsync(sale.Id, 50m);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.Equal((writeOff.Id, "15", 50m, 1), (note.OrderWriteOffId, note.PaymentForm, note.Total, note.AfterInstallment));
        Assert.Equal("Condonación", Assert.Single(book.Gateway.StampRequests[^1].Document.Concepts).Description);

        // The rest is paid: its complement goes from what the payment and the write-off left, and settles the invoice,
        // taking what is left of the base and the IVA after the down payment's complement and the credit note.
        await book.PayAsync(sale.Id, 100m, OrderPaymentMethod.Transfer);
        Assert.Equal(1, (await book.IssuePendingComplementsAsync()).Value);
        var last = await book.Context.PaymentComplements.SingleAsync(c => c.Installment == 2);
        Assert.Equal((100m, 100m, 0m), (last.PreviousBalance, last.Amount, last.RemainingBalance));
        var settled = Assert.Single(book.Gateway.StampRequests[^1].Document.Payment!.Invoice.Transfers);
        Assert.Equal((86.21m, 13.79m), (settled.Base, settled.Amount));
    }

    [Fact]
    public async Task CreditNote_OfARefund_ShouldNotTouchTheComplements_OfAnInvoiceAlreadyPaid()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);
        await book.PayAsync(sale.Id, 150m, OrderPaymentMethod.Transfer);
        Assert.Equal(2, (await book.IssuePendingComplementsAsync()).Value);

        // The money it gives back was received, and its complement said so: the credit note goes whole, with its form.
        await book.RefundAsync(sale.Id, 30m, OrderPaymentMethod.Cash);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.Equal(("01", 30m, 2), (note.PaymentForm, note.Total, note.AfterInstallment));
        Assert.Equal([150m, 0m], (await book.Context.PaymentComplements.OrderBy(c => c.Installment).ToListAsync()).Select(c => c.RemainingBalance));
    }

    [Theory]
    [InlineData(OrderPaymentMethod.Other)]
    [InlineData(OrderPaymentMethod.CustomerBalance)]
    public async Task Refund_WithoutAFormOfTheSat_ShouldGetNoCreditNote_AndSayWhy(OrderPaymentMethod method)
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        await book.IssueAsync(sale.Id);
        var refund = await book.RefundAsync(sale.Id, 10m, method);

        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Adjustments);
        var problem = CreditNotes.FormProblem(method);
        Assert.Equal((false, problem, false), (shown.CanIssue, shown.Problem, shown.AwaitsAutomaticIssue));
        Assert.Equal(problem, (await book.IssueCreditNoteAsync(CreditNoteAdjustment.Refund, refund.Id)).Error);
        Assert.Empty(await book.Context.CreditNotes.ToListAsync());
    }

    [Fact]
    public async Task CreditNote_RefusedByThePac_ShouldBeIssuedAgainByAPerson_NotByTheWorker()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        await book.IssueAsync(sale.Id);
        var refund = await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);

        book.Gateway.Refusal = "CFDI40108 - El TipoDeComprobante es E y la FormaPago no es válida.";
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var refused = await book.Context.CreditNotes.SingleAsync();
        Assert.Equal(OrderInvoiceStatus.Refused, refused.Status);
        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Adjustments);
        Assert.Equal((true, false), (shown.CanIssue, shown.AwaitsAutomaticIssue));
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);

        book.Gateway.Refusal = null;
        var again = await book.IssueCreditNoteAsync(CreditNoteAdjustment.Refund, refund.Id);
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(("NC-2", OrderInvoiceStatus.Stamped, false), (again.Value.Number, again.Value.Status, again.Value.IssuedAutomatically));
        // Never two that stand at once.
        Assert.Equal(CreditNotes.LiveCreditNoteProblem(await book.Context.CreditNotes.SingleAsync(n => n.Id == again.Value.Id)),
            (await book.IssueCreditNoteAsync(CreditNoteAdjustment.Refund, refund.Id)).Error);
    }

    [Fact]
    public async Task Invoice_ShouldNotBeCancelled_WhileItsCreditNotesStand()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        var invoice = (await book.IssueAsync(sale.Id)).Value;
        await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        await book.IssuePendingAsync();
        var note = await book.Context.CreditNotes.SingleAsync();

        Assert.Equal(CancelOrderInvoiceCommandHandler.LiveCreditNotesMessage(["NC-1"]), (await book.CancelInvoiceAsync(invoice.Id)).Error);

        // The credit note goes first; while the SAT waits for the customer it still stands.
        var cancelling = await book.CancelCreditNoteAsync(note.Id, CfdiCancellationMotive.ErrorsWithoutReplacement);
        Assert.Equal(OrderInvoiceStatus.Cancelling, cancelling.Value.Status);
        Assert.Equal(CancelOrderInvoiceCommandHandler.LiveCreditNotesMessage(["NC-1"]), (await book.CancelInvoiceAsync(invoice.Id)).Error);

        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.Cancelled, false);
        Assert.Equal(1, (await book.CheckCancellationsAsync()).Value);
        Assert.Equal(OrderInvoiceStatus.Cancelled, (await book.Context.CreditNotes.SingleAsync()).Status);
        Assert.True((await book.CancelInvoiceAsync(invoice.Id)).IsSuccess);
    }

    [Fact]
    public async Task CreditNote_ShouldBeCancelledByAManager_WithMotive02Or03_AndItsFilesDownloaded()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        await book.IssueAsync(sale.Id);
        await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        await book.IssuePendingAsync();
        var note = await book.Context.CreditNotes.SingleAsync();

        var xml = await book.DownloadCreditNoteAsync(note.Id);
        var pdf = await book.DownloadCreditNoteAsync(note.Id, OrderInvoiceFileKind.Pdf);
        Assert.Equal(("application/xml", $"NC-1_{note.Uuid}.xml"), (xml.Value.ContentType, xml.Value.FileName));
        Assert.Equal("application/pdf", pdf.Value.ContentType);

        var validator = new CancelCreditNoteCommandValidator();
        Assert.False(validator.Validate(new CancelCreditNoteCommand(note.Id, CfdiCancellationMotive.ErrorsWithReplacement)).IsValid);
        Assert.False(validator.Validate(new CancelCreditNoteCommand(note.Id, CfdiCancellationMotive.NominativeInGlobalInvoice)).IsValid);
        Assert.True(validator.Validate(new CancelCreditNoteCommand(note.Id, CfdiCancellationMotive.NotCarriedOut)).IsValid);

        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        var cancelled = await book.CancelCreditNoteAsync(note.Id, CfdiCancellationMotive.NotCarriedOut);
        Assert.Equal(OrderInvoiceStatus.Cancelled, cancelled.Value.Status);
        Assert.Equal((Rfc, note.Uuid, CfdiCancellationMotive.NotCarriedOut), book.Gateway.Cancellations[^1]);
        Assert.Equal("Esta nota de crédito ya está cancelada.",
            (await book.CancelCreditNoteAsync(note.Id, CfdiCancellationMotive.NotCarriedOut)).Error);
    }

    [Fact]
    public async Task WriteOff_ShouldNotBeUndone_WhileItsCreditNoteStands_NorTakeOneOnceUndone()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);
        var writeOff = await book.WriteOffAsync(sale.Id, 50m);
        await book.IssuePendingAsync();
        var note = await book.Context.CreditNotes.SingleAsync();

        var refused = await book.ReverseWriteOffAsync(writeOff.Id);
        Assert.Equal(ReverseCustomerWriteOffCommandHandler.LiveCreditNoteMessage("NC-1"), refused.Error);

        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        Assert.True((await book.CancelCreditNoteAsync(note.Id, CfdiCancellationMotive.NotCarriedOut)).IsSuccess);
        var undone = await book.ReverseWriteOffAsync(writeOff.Id);
        Assert.True(undone.IsSuccess, undone.Error);

        // Undone, it takes no credit note: neither the worker nor a person issues one.
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Adjustments);
        Assert.Equal((true, false, CreditNote.ReversedWriteOffMessage), (shown.Reversed, shown.CanIssue, shown.Problem));
        Assert.Equal(CreditNote.ReversedWriteOffMessage, (await book.IssueCreditNoteAsync(CreditNoteAdjustment.WriteOff, writeOff.Id)).Error);
    }

    [Fact]
    public async Task CreditNote_ShouldGoWithTheBusinessesSerie_AndItsRfc()
    {
        await using var book = await Book.OpenAsync();
        var changed = await book.ChangeSeriesAsync("F", creditNoteSerie: " egr ");
        Assert.Equal("EGR", changed.Value.CreditNoteSerie);
        Assert.Contains("las notas de crédito", (await book.ChangeSeriesAsync("F", creditNoteSerie: "E-1")).Error);
        var sale = await book.PaidSaleAsync();
        await book.IssueAsync(sale.Id);
        var refund = await book.RefundAsync(sale.Id, 10m, OrderPaymentMethod.Card);

        // Mi negocio now has another RFC: the invoice's credit notes are issued by the RFC that stamped it.
        var tenant = await book.Context.Tenants.SingleAsync(t => t.Id == book.Tenant.Id);
        tenant.SetBusinessInfo(
            OtherRfc, "XENON INDUSTRIAL ARTICLES", "Comercio", "601 - General de Ley Personas Morales",
            new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
        await book.Context.SaveChangesAsync();
        await book.UploadCsdAsync(OtherRfc);
        Assert.Equal(CreditNotes.OtherRfcMessage("F-1"), (await book.IssueCreditNoteAsync(CreditNoteAdjustment.Refund, refund.Id)).Error);
        Assert.Equal(CreditNotes.OtherRfcMessage("F-1"), Assert.Single((await book.InvoicingAsync(sale.Id)).Adjustments).Problem);

        tenant.SetBusinessInfo(
            Rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
            new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
        await book.Context.SaveChangesAsync();
        await book.UploadCsdAsync();
        var issued = await book.IssueCreditNoteAsync(CreditNoteAdjustment.Refund, refund.Id);
        Assert.Equal("EGR-1", issued.Value.Number);
    }

    [Fact]
    public void Room_ShouldNeverCreditMoreThanTheInvoiceAmountsTo_NorForgiveMoreThanItOwes()
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var order = new Order(tenantId, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla", CustomerPhone,
            null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        var payment = order.RegisterPayment(120m, OrderPaymentMethod.Cash, now, null, null, null, null);
        order.SellOnCredit();
        var invoice = OrderInvoice.Request(tenantId, order.Id, "F", 1,
            CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"), "99", "{}",
            172.41m, 27.59m, 200m, Guid.NewGuid(), now, CfdiInvoice.PaidInInstallments);
        invoice.Stamp("5F0B7C1E-2A3D-4E5F-8A9B-0C1D2E3F4A5B", "pac-1", now, "f.xml");
        var complement = PaymentComplement.Request(invoice, payment, "P", 1, 1, 200m, "{}", null, now);
        var refund = order.RegisterRefund(50m, OrderPaymentMethod.Cash, "Devolución", now, "cajero", null, null, false, []);
        var refundNote = CreditNote.ForRefund(invoice, refund, "NC", 1, 43.10m, 6.90m, 1, "{}", null, now);
        var writeOff = order.WriteOff(30m, OrderWriteOffReason.WontBeCollected, null, now, "dueña");
        var other = order.WriteOff(10m, OrderWriteOffReason.WontBeCollected, null, now, "dueña");
        var otherNote = CreditNote.ForWriteOff(invoice, other, "NC", 2, 8.62m, 1.38m, 1, "{}", null, now);

        var standing = CreditNotes.StandingOf([complement], [refundNote, otherNote], []);

        // A write-off forgives at most what is owed: $200 − $120 paid − $10 forgiven. A refund gives back money received:
        // only the other credit notes limit it, $200 − $50 − $10.
        Assert.Equal(new CreditNotes.Room(70m, 1), CreditNotes.RoomFor(invoice, standing, CreditNoteAdjustment.WriteOff, writeOff.Id));
        Assert.Equal(new CreditNotes.Room(140m, 1), CreditNotes.RoomFor(invoice, standing, CreditNoteAdjustment.Refund, Guid.NewGuid()));
        // Its own earlier credit note does not count against it, and an undone write-off's no longer counts.
        Assert.Equal(190m, CreditNotes.RoomFor(invoice, standing, CreditNoteAdjustment.Refund, refund.Id).Available);
        Assert.Equal(80m, CreditNotes.RoomFor(invoice, CreditNotes.StandingOf([complement], [refundNote, otherNote], [other.Id]),
            CreditNoteAdjustment.WriteOff, writeOff.Id).Available);
    }

    [Fact]
    public void Requests_ShouldBeOfWhoeverManagesTheOrders_AndCancellingOfAManager()
    {
        Assert.Equal(TenantPermission.ManageOrders, Permission<IssueCreditNoteCommand>());
        Assert.Equal(TenantPermission.ManageOrders, Permission<RequestCreditNoteCommand>());
        Assert.Equal(TenantPermission.ManageOrders, Permission<RetryCreditNoteCommand>());
        Assert.Equal(TenantPermission.ManageOrders, Permission<GetCreditNoteFileQuery>());
        Assert.True(typeof(CancelCreditNoteCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));
        Assert.True(typeof(IssuePendingCreditNotesCommand).IsDefined(typeof(AllowAnonymousRequestAttribute), false));
        Assert.True(typeof(RequestAutomaticCreditNoteCommand).IsDefined(typeof(AllowAnonymousRequestAttribute), false));
    }

    private static TenantPermission Permission<TRequest>()
        => Assert.Single(((RequiresPermissionAttribute)Attribute.GetCustomAttribute(typeof(TRequest), typeof(RequiresPermissionAttribute))!).AnyOf);

    private sealed class Book : IAsyncDisposable, ISender
    {
        private Book(SalesTestFixture fixture, Tenant tenant, Product product, Customer customer)
        {
            Fixture = fixture;
            Tenant = tenant;
            Product = product;
            Customer = customer;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        public Product Product { get; }
        public Customer Customer { get; }
        public FakeInvoicingGateway Gateway { get; } = new();
        public MemoryPrivateStorage Storage { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Book> OpenAsync()
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            var customer = new Customer(tenant.Id, "Karla", CustomerPhone, null);
            customer.SetCreditLimit(1_000m);
            fixture.Context.Customers.Add(customer);
            tenant.SetBusinessInfo(
                Rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await fixture.Context.SaveChangesAsync();
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m), customer);
            await book.UploadCsdAsync();
            return book;
        }

        public async Task UploadCsdAsync(string rfc = Rfc)
        {
            var (cer, key) = TestCsd.Make(uniqueIdentifier: $"{rfc} / VADA800927HSRSRL05");
            var uploaded = await new UploadInvoicingCertificateCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                    CancellationToken.None);
            Assert.True(uploaded.IsSuccess, uploaded.Error);
        }

        public Task<Result<InvoicingSettingsDto>> ChangeSeriesAsync(string serie, string? creditNoteSerie = null)
            => new ChangeInvoiceSerieCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new ChangeInvoiceSerieCommand(serie, CreditNoteSerie: creditNoteSerie), CancellationToken.None);

        /// <summary>A $200 sale on credit to Karla (2 cafés, IVA included), with what she paid at the counter.</summary>
        public async Task<OrderDto> CreditSaleAsync(decimal downPayment = 0m)
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerId = Customer.Id,
                OnCredit = true,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 2 }],
                Payments = downPayment > 0 ? [new CreateOrderPaymentDto { Amount = downPayment, Method = OrderPaymentMethod.Cash }] : []
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        /// <summary>A $100 sale to Karla, paid by card.</summary>
        public async Task<OrderDto> PaidSaleAsync()
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Karla",
                CustomerPhone = CustomerPhone,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Card }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        /// <summary>A payment registered later; the clock moves a minute first, so nothing shares its time.</summary>
        public async Task<OrderPayment> PayAsync(Guid orderId, decimal amount, OrderPaymentMethod method)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == orderId);
            var payment = order.RegisterPayment(amount, method, Clock.UtcNow, null, null, null, null);
            await Context.SaveChangesAsync();
            return payment;
        }

        public async Task<OrderRefund> RefundAsync(Guid orderId, decimal amount, OrderPaymentMethod method)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).Include(o => o.Items).SingleAsync(o => o.Id == orderId);
            var refund = order.RegisterRefund(amount, method, "Devolución de un café", Clock.UtcNow, "cajero", null, null, false, []);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return refund;
        }

        public async Task<OrderWriteOff> WriteOffAsync(Guid orderId, decimal amount)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.WriteOffs).SingleAsync(o => o.Id == orderId);
            var writeOff = order.WriteOff(amount, OrderWriteOffReason.WontBeCollected, null, Clock.UtcNow, "dueña");
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return writeOff;
        }

        public Task<Result<CustomerAccountDto>> ReverseWriteOffAsync(Guid writeOffId)
            => new ReverseCustomerWriteOffCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, new PhoneNormalizationService(), Clock,
                    new CustomerCreditLock(Context), new InvoicingLock(Context))
                .Handle(new ReverseCustomerWriteOffCommand { WriteOffId = writeOffId }, CancellationToken.None);

        public async Task<CfdiDraftDto> DraftAsync(Guid orderId)
        {
            var draft = await new GetOrderCfdiDraftQueryHandler(Context)
                .Handle(new GetOrderCfdiDraftQuery { OrderId = orderId, Receiver = Receiver }, CancellationToken.None);
            Assert.True(draft.IsSuccess, draft.Error);
            return draft.Value;
        }

        public Task<OrderInvoicingDto> InvoicingAsync(Guid orderId)
            => new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> IssueAsync(Guid orderId)
            => new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(orderId, Receiver, false), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> CancelInvoiceAsync(Guid invoiceId)
            => new CancelOrderInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelOrderInvoiceCommand(invoiceId, CfdiCancellationMotive.ErrorsWithoutReplacement), CancellationToken.None);

        public Task<Result<CreditNoteDto>> IssueCreditNoteAsync(CreditNoteAdjustment adjustment, Guid adjustmentId)
            => new IssueCreditNoteCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueCreditNoteCommand(adjustment, adjustmentId), CancellationToken.None);

        public Task<Result<CreditNoteDto>> CancelCreditNoteAsync(Guid creditNoteId, CfdiCancellationMotive motive)
            => new CancelCreditNoteCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelCreditNoteCommand(creditNoteId, motive), CancellationToken.None);

        public Task<Result<PrivateFileDownload>> DownloadCreditNoteAsync(Guid creditNoteId, OrderInvoiceFileKind kind = OrderInvoiceFileKind.Xml)
            => new GetCreditNoteFileQueryHandler(Context, Storage, Gateway, NullLogger<GetCreditNoteFileQueryHandler>.Instance)
                .Handle(new GetCreditNoteFileQuery(creditNoteId, kind), CancellationToken.None);

        public Task<Result<int>> IssuePendingAsync()
            => new IssuePendingCreditNotesCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingCreditNotesCommand(), CancellationToken.None);

        public Task<Result<int>> IssuePendingComplementsAsync()
            => new IssuePendingPaymentComplementsCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingPaymentComplementsCommand(), CancellationToken.None);

        public Task<Result<int>> CheckCancellationsAsync()
            => new CheckInvoiceCancellationsCommandHandler(Context, Gateway, Storage, Clock, NullLogger<CheckInvoiceCancellationsCommandHandler>.Instance)
                .Handle(new CheckInvoiceCancellationsCommand(), CancellationToken.None);

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The requests go to their own handlers, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                RequestOrderInvoiceCommand command => await new RequestOrderInvoiceCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, new InvoicingLock(Context), new FolioGenerator(Context),
                        new PhoneNormalizationService(), Clock)
                    .Handle(command, cancellationToken),
                RequestAutomaticPaymentComplementCommand command => await new RequestAutomaticPaymentComplementCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                RequestCreditNoteCommand command => await new RequestCreditNoteCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                RequestAutomaticCreditNoteCommand command => await new RequestAutomaticCreditNoteCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return (TResponse)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
