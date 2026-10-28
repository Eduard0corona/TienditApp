using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
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
/// The credit note of a refund of a sale a global invoice covers (Fase 28I, ADR 0054): to the general public, related to the
/// global invoice, for what was given back with the rates the sale had in it, never more than what the sale charged in it;
/// TienditApp issues it on its own (also a voided sale's), a person issues it again or cancels it, it never goes by email,
/// and the global invoice is not cancelled while one stands.
/// </summary>
public class GlobalInvoiceCreditNoteFlowTests
{
    private const string Rfc = "EKU9003173C9";
    private const string OtherRfc = "XIA190128J61";

    // The sales are made on Sunday 4 October 2026 (11:00 in Mexico City); the next morning the day is over.
    private static readonly DateOnly SaleDay = new(2026, 10, 4);
    private static readonly DateTimeOffset NextMorning = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Worker_ShouldIssueTheCreditNoteOfARefund_ToTheGeneralPublic_RelatedToTheGlobalInvoice()
    {
        await using var book = await Book.OpenAsync();
        var card = await book.PaidOrderAsync(2, OrderPaymentMethod.Card);
        var cash = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        var global = await book.IssueGlobalAsync(2, 300m);
        var refund = await book.RefundAsync(card.Id, 20m, OrderPaymentMethod.Card);

        Assert.Equal(1, (await book.IssuePendingAsync()).Value);

        var note = Assert.Single(await book.Context.CreditNotes.ToListAsync());
        Assert.Equal(("NC-1", OrderInvoiceStatus.Stamped, refund.Id, (Guid?)null, (Guid?)global.Id, "04", 17.24m, 2.76m, 20m, true),
            (note.Number, note.Status, note.OrderRefundId, note.OrderInvoiceId, note.GlobalInvoiceId, note.PaymentForm, note.SubTotal,
                note.TaxTotal, note.Total, note.RequestedByUserId is null));
        var document = book.Gateway.StampRequests[^1].Document;
        Assert.Equal(("E", "PUE", "04", 17.24m, 2.76m, 20m),
            (document.VoucherType, document.PaymentMethod, document.PaymentForm, document.SubTotal, document.TotalTransferred, document.Total));
        Assert.Equal(("01", global.Uuid), (document.Related!.RelationType, Assert.Single(document.Related.Uuids)));
        // To the general public, at the place it is issued from, without the global invoice's InformacionGlobal.
        Assert.Equal(new CfdiDocumentReceiver("XAXX010101000", "PUBLICO EN GENERAL", "06000", "616", "S01"), document.Receiver);
        Assert.Equal(("06000", Rfc), (document.ExpeditionPostalCode, document.Issuer.Rfc));
        Assert.Null(document.GlobalInformation);
        // Its concept carries the sale's folio, as the sale's concept in the global invoice does.
        var concept = Assert.Single(document.Concepts);
        Assert.Equal(("84111506", "ACT", "Devolución", card.OrderNumber, 17.24m),
            (concept.ProductCode, concept.UnitCode, concept.Description, concept.Identification, concept.Amount));
        Assert.Equal((17.24m, 0.16m, 2.76m), (concept.Transfer!.Base, concept.Transfer.Rate, concept.Transfer.Amount));
        // Nobody to send it to.
        Assert.Empty(await book.Context.CfdiEmails.ToListAsync());

        // Nothing new: nothing is issued twice.
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var invoicing = await book.OrderInvoicingAsync(card.Id);
        Assert.Equal("F-1", invoicing.GlobalInvoiceNumber);
        var shown = Assert.Single(invoicing.Adjustments);
        Assert.Equal((refund.Id, 20m, false, false, null), (shown.AdjustmentId, shown.Amount, shown.CanIssue, shown.AwaitsAutomaticIssue, shown.Problem));
        var shownNote = Assert.Single(shown.CreditNotes);
        Assert.Equal(("NC-1", (Guid?)null, (Guid?)global.Id, "F-1"),
            (shownNote.Number, shownNote.OrderInvoiceId, shownNote.GlobalInvoiceId, shownNote.GlobalInvoiceNumber));
        Assert.Empty((await book.OrderInvoicingAsync(cash.Id)).Adjustments);
        // The global invoice's detail lists it under its sale.
        var detail = await book.GlobalDetailAsync(global.Id);
        Assert.Equal(["NC-1"], detail.Orders.Single(o => o.OrderId == card.Id).CreditNotes.Select(n => n.Number));
        Assert.Empty(detail.Orders.Single(o => o.OrderId == cash.Id).CreditNotes);
    }

    [Fact]
    public async Task SaleGivenBackInPart_BeforeItsGlobalInvoice_ShouldGoInForItsTotal_AndTakeItsCreditNote()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var returned = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        // The same day: $20 of one back, the other whole.
        var refund = await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        await book.RefundAsync(returned.Id, 100m, OrderPaymentMethod.Cash);

        var global = await book.IssueGlobalAsync(1, 100m);

        Assert.Equal(sale.OrderNumber, Assert.Single((await book.GlobalDetailAsync(global.Id)).Orders).OrderNumber);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.Equal((refund.Id, (Guid?)global.Id, 20m), (note.OrderRefundId, note.GlobalInvoiceId, note.Total));
        // Given back whole, it has nothing to invoice: neither the global invoice nor a credit note.
        Assert.Empty((await book.OrderInvoicingAsync(returned.Id)).Adjustments);
    }

    [Fact]
    public async Task CreditNote_ShouldSplitWhatWasGivenBack_ByTheRatesTheSaleHadInTheGlobalInvoice()
    {
        await using var book = await Book.OpenAsync();
        var book2 = await book.Fixture.AddProductAsync(book.Tenant, "Libro", 100m, isTaxExempt: true);
        var sale = await book.SaleAsync(OrderPaymentMethod.Cash, (book.Product, 1), (book2, 1));
        await book.IssueGlobalAsync(1, 200m);
        await book.RefundAsync(sale.Id, 100m, OrderPaymentMethod.Cash);

        Assert.Equal(1, (await book.IssuePendingAsync()).Value);

        // Half of the sale: half of each rate's base and IVA.
        var document = book.Gateway.StampRequests[^1].Document;
        Assert.Equal(("01", 93.10m, 6.90m, 100m), (document.PaymentForm, document.SubTotal, document.TotalTransferred, document.Total));
        Assert.Equal(
            [(43.10m, "Tasa", (decimal?)6.90m, sale.OrderNumber), (50m, "Exento", null, sale.OrderNumber)],
            document.Concepts.Select(c => (c.Amount, c.Transfer!.FactorType, c.Transfer.Amount, c.Identification)));
    }

    [Fact]
    public async Task CreditNote_IssuedAgain_ShouldFitWhatTheSaleChargedAfterItsOtherRefunds()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        await book.IssueGlobalAsync(1, 100m);
        var first = await book.RefundAsync(sale.Id, 30m, OrderPaymentMethod.Cash);
        var second = await book.RefundAsync(sale.Id, 70m, OrderPaymentMethod.Cash);
        Assert.Equal(2, (await book.IssuePendingAsync()).Value);
        var firstNote = await book.Context.CreditNotes.SingleAsync(n => n.OrderRefundId == first.Id);

        // A refund's credit note is cancelled only for errors without relation (02).
        Assert.Equal(CreditNote.RefundCancellationMotivesMessage,
            (await book.CancelCreditNoteAsync(firstNote.Id, CfdiCancellationMotive.NotCarriedOut)).Error);
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        Assert.Equal(OrderInvoiceStatus.Cancelled,
            (await book.CancelCreditNoteAsync(firstNote.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).Value.Status);

        // TienditApp does not issue it again on its own; a person does, within what the other refund left.
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var shown = (await book.OrderInvoicingAsync(sale.Id)).Adjustments.Single(a => a.AdjustmentId == first.Id);
        Assert.Equal((true, false, null), (shown.CanIssue, shown.AwaitsAutomaticIssue, shown.Problem));
        var again = await book.IssueCreditNoteAsync(first.Id);
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(("NC-3", OrderInvoiceStatus.Stamped, 30m), (again.Value.Number, again.Value.Status, again.Value.Total));
        // Never two that stand at once; together they credit what the sale charged.
        Assert.Equal(CreditNotes.LiveCreditNoteProblem(await book.Context.CreditNotes.SingleAsync(n => n.Id == again.Value.Id)),
            (await book.IssueCreditNoteAsync(first.Id)).Error);
        var standing = await book.Context.CreditNotes.Where(n => n.Status == OrderInvoiceStatus.Stamped).ToListAsync();
        Assert.Equal([(first.Id, 30m), (second.Id, 70m)],
            standing.OrderBy(n => n.Total).Select(n => (n.OrderRefundId!.Value, n.Total)));
    }

    [Fact]
    public async Task CreditNote_ShouldNotCreditMoreThanTheSaleChargedInTheGlobalInvoice_AfterItsOtherCreditNotes()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        await book.IssueGlobalAsync(1, 100m);
        // The sale charged less in the global invoice than what is given back (as a hand-edited row would say).
        var row = await book.Context.GlobalInvoiceOrders.SingleAsync(o => o.OrderId == sale.Id);
        book.Context.Entry(row).Property(o => o.Total).CurrentValue = 80m;
        await book.Context.SaveChangesAsync();
        book.Context.ChangeTracker.Clear();
        var first = await book.RefundAsync(sale.Id, 30m, OrderPaymentMethod.Cash);
        var second = await book.RefundAsync(sale.Id, 70m, OrderPaymentMethod.Cash);

        // The first fits; the second does not fit in what the first left.
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        Assert.Equal(first.Id, (await book.Context.CreditNotes.SingleAsync()).OrderRefundId);
        var message = CreditNotes.AboveSaleMessage("F-1", sale.OrderNumber);
        Assert.Equal(message, (await book.IssueCreditNoteAsync(second.Id)).Error);
        var shown = (await book.OrderInvoicingAsync(sale.Id)).Adjustments.Single(a => a.AdjustmentId == second.Id);
        Assert.Equal((false, false, message), (shown.CanIssue, shown.AwaitsAutomaticIssue, shown.Problem));
        Assert.Single(await book.Context.CreditNotes.ToListAsync());
    }

    [Fact]
    public async Task GlobalInvoice_ShouldNotBeCancelled_WhileACreditNoteOfItsSalesStands()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var global = await book.IssueGlobalAsync(1, 100m);
        await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);

        Assert.Equal(GlobalInvoicing.LiveCreditNotesMessage(["NC-1"]),
            (await book.CancelGlobalAsync(global.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).Error);
        Assert.DoesNotContain(book.Gateway.Cancellations, c => c.Uuid == global.Uuid);

        // Its credit note goes first; then the global invoice is cancelled and its sales are free again.
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.True((await book.CancelCreditNoteAsync(note.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        var cancelled = await book.CancelGlobalAsync(global.Id, CfdiCancellationMotive.ErrorsWithoutReplacement);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(OrderInvoiceStatus.Cancelled, cancelled.Value.Status);
        // Nothing covers the sale now: its refund waits for the CFDI that will.
        var invoicing = await book.OrderInvoicingAsync(sale.Id);
        Assert.Null(invoicing.GlobalInvoiceNumber);
        Assert.Equal(CreditNotes.NoStampedInvoiceMessage, Assert.Single(invoicing.Adjustments).Problem);
    }

    [Fact]
    public async Task VoidedSale_OfAGlobalInvoice_ShouldTakeItsRefundsCreditNote()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        await book.IssueGlobalAsync(1, 100m);
        // «Anular venta» gives everything back first.
        var refund = await book.RefundAsync(sale.Id, 100m, OrderPaymentMethod.Card);
        await book.CancelOrderAsync(sale.Id);

        // The global invoice is not cancelled for one of its sales: the credit note undoes it.
        var shown = Assert.Single((await book.OrderInvoicingAsync(sale.Id)).Adjustments);
        Assert.Equal((true, true, null), (shown.CanIssue, shown.AwaitsAutomaticIssue, shown.Problem));
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.Equal((refund.Id, 100m, OrderInvoiceStatus.Stamped), (note.OrderRefundId, note.Total, note.Status));
    }

    [Fact]
    public async Task Refund_BeforeTheSaleEnteredTheGlobalInvoiceIssuedAgain_ShouldTakeItsCreditNoteOfTheNewOne()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var first = await book.IssueGlobalAsync(1, 100m);
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        Assert.True((await book.CancelGlobalAsync(first.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        var refund = await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);

        // Nothing to relate it to yet.
        Assert.Equal(CreditNotes.NoStampedInvoiceMessage, (await book.IssueCreditNoteAsync(refund.Id)).Error);
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);

        // The period's global invoice issued again takes the sale for its total, as the first did.
        var second = await book.IssueGlobalAsync(1, 100m);
        Assert.Equal("F-2", second.Number);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.Equal(((Guid?)second.Id, 20m), (note.GlobalInvoiceId, note.Total));
        Assert.Equal(second.Uuid, Assert.Single(book.Gateway.StampRequests[^1].Document.Related!.Uuids));
    }

    [Fact]
    public async Task CreditNote_ShouldWait_WhileTheGlobalInvoiceStamps_AndNotBeIssuedWithAnotherRfcOrForm()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        book.Gateway.Unavailable = true;
        var global = await book.IssueGlobalAsync(1, 100m);
        Assert.Equal(OrderInvoiceStatus.Stamping, global.Status);
        var refund = await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        var other = await book.RefundAsync(sale.Id, 10m, OrderPaymentMethod.Other);

        var stamping = CreditNotes.GlobalInvoiceProblem(await book.Context.GlobalInvoices.SingleAsync());
        Assert.Equal($"La factura global F-1 todavía se está timbrando: la nota de crédito se emite cuando quede timbrada.", stamping);
        Assert.Equal(stamping, (await book.IssueCreditNoteAsync(refund.Id)).Error);
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);

        book.Gateway.Unavailable = false;
        Assert.Equal(OrderInvoiceStatus.Stamped, (await book.RetryGlobalAsync(global.Id)).Value.Status);
        Assert.Equal(CreditNotes.OtherFormMessage, (await book.IssueCreditNoteAsync(other.Id)).Error);

        // Mi negocio now has another RFC: the global invoice's credit notes are issued by the RFC that stamped it.
        var tenant = await book.Context.Tenants.SingleAsync(t => t.Id == book.Tenant.Id);
        tenant.SetBusinessInfo(
            OtherRfc, "XENON INDUSTRIAL ARTICLES", "Comercio", "601 - General de Ley Personas Morales",
            new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
        await book.Context.SaveChangesAsync();
        await book.UploadCsdAsync(OtherRfc);
        Assert.Equal(CreditNotes.OtherRfcMessage("F-1"), (await book.IssueCreditNoteAsync(refund.Id)).Error);
        var adjustments = (await book.OrderInvoicingAsync(sale.Id)).Adjustments;
        Assert.Equal(
            [(refund.Id, CreditNotes.OtherRfcMessage("F-1"), false), (other.Id, CreditNotes.OtherFormMessage, false)],
            adjustments.Select(a => (a.AdjustmentId, a.Problem, a.AwaitsAutomaticIssue)));
        Assert.Empty(await book.Context.CreditNotes.ToListAsync());
    }

    [Fact]
    public async Task CreditNote_OfAGlobalInvoice_ShouldNotGoByEmail()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        await book.IssueGlobalAsync(1, 100m);
        await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();

        var sent = await new SendCfdiByEmailCommandHandler(book.Context, book.Fixture.CurrentUser, new InvoicingLock(book.Context), book.Clock)
            .Handle(new SendCfdiByEmailCommand(CfdiEmailDocument.CreditNote, note.Id, "karla@example.com", false), CancellationToken.None);

        Assert.Equal(CfdiEmail.GlobalCreditNoteMessage, sent.Error);
        Assert.Empty(await book.Context.CfdiEmails.ToListAsync());
    }

    private sealed class Book : IAsyncDisposable, ISender
    {
        private Book(SalesTestFixture fixture, Tenant tenant, Product product)
        {
            Fixture = fixture;
            Tenant = tenant;
            Product = product;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        /// <summary>Café, $100 with IVA.</summary>
        public Product Product { get; }
        public FakeInvoicingGateway Gateway { get; } = new();
        public MemoryPrivateStorage Storage { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Book> OpenAsync()
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m));
            tenant.SetBusinessInfo(
                Rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await book.Context.SaveChangesAsync();
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

        /// <summary>A sale of Café paid in full, on the sale day.</summary>
        public Task<OrderDto> PaidOrderAsync(int quantity, OrderPaymentMethod method)
            => SaleAsync(method, (Product, quantity));

        public async Task<OrderDto> SaleAsync(OrderPaymentMethod method, params (Product Product, int Quantity)[] lines)
        {
            var total = lines.Sum(line => line.Product.PriceAmount * line.Quantity);
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Público",
                Items = lines.Select(line => new CreateOrderItemDto { ProductId = line.Product.Id, Quantity = line.Quantity }).ToList(),
                Payments = [new CreateOrderPaymentDto { Amount = total, Method = method }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        /// <summary>
        /// Stamps the sale day's global invoice the next morning (or later), as its preview showed it, and expects it saved.
        /// </summary>
        public async Task<GlobalInvoiceDto> IssueGlobalAsync(int saleCount, decimal total)
        {
            if (Clock.UtcNow < NextMorning)
                Clock.UtcNow = NextMorning;
            var draft = await new GetGlobalInvoiceDraftQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new GetGlobalInvoiceDraftQuery(GlobalInvoicePeriodicity.Daily, SaleDay), CancellationToken.None);
            var issued = await new IssueGlobalInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueGlobalInvoiceCommand(GlobalInvoicePeriodicity.Daily, SaleDay, saleCount, total, draft.Fingerprint),
                    CancellationToken.None);
            Assert.True(issued.IsSuccess, issued.Error);
            Context.ChangeTracker.Clear();
            return issued.Value;
        }

        public Task<Result<GlobalInvoiceDto>> RetryGlobalAsync(Guid invoiceId)
            => new RetryGlobalInvoiceCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new RetryGlobalInvoiceCommand(invoiceId), CancellationToken.None);

        public async Task<Result<GlobalInvoiceDto>> CancelGlobalAsync(Guid invoiceId, CfdiCancellationMotive motive)
        {
            var cancelled = await new CancelGlobalInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelGlobalInvoiceCommand(invoiceId, motive), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return cancelled;
        }

        public Task<GlobalInvoiceDetailDto> GlobalDetailAsync(Guid invoiceId)
            => new GetGlobalInvoiceQueryHandler(Context).Handle(new GetGlobalInvoiceQuery(invoiceId), CancellationToken.None);

        public async Task<OrderRefund> RefundAsync(Guid orderId, decimal amount, OrderPaymentMethod method)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).Include(o => o.Items).SingleAsync(o => o.Id == orderId);
            var refund = order.RegisterRefund(amount, method, "Devolución", Clock.UtcNow, "cajero", null, null, false, []);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return refund;
        }

        /// <summary>The order ends cancelled, as «Anular venta» leaves one it voided.</summary>
        public async Task CancelOrderAsync(Guid orderId)
        {
            var order = await Context.Orders.SingleAsync(o => o.Id == orderId);
            Context.Entry(order).Property(o => o.Status).CurrentValue = OrderStatus.Cancelled;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public Task<OrderInvoicingDto> OrderInvoicingAsync(Guid orderId)
            => new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);

        public async Task<Result<CreditNoteDto>> IssueCreditNoteAsync(Guid refundId)
        {
            var issued = await new IssueCreditNoteCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueCreditNoteCommand(CreditNoteAdjustment.Refund, refundId), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return issued;
        }

        public async Task<Result<CreditNoteDto>> CancelCreditNoteAsync(Guid creditNoteId, CfdiCancellationMotive motive)
        {
            var cancelled = await new CancelCreditNoteCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelCreditNoteCommand(creditNoteId, motive), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return cancelled;
        }

        public async Task<Result<int>> IssuePendingAsync()
        {
            var issued = await new IssuePendingCreditNotesCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingCreditNotesCommand(), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return issued;
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The requests the handlers send go to their own handlers, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                RequestGlobalInvoiceCommand command => await new RequestGlobalInvoiceCommandHandler(
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
