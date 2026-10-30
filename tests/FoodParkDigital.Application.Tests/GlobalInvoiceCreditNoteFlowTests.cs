using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The credit note of a refund (Fase 28I, ADR 0054) or a write-off (Fase 28J, ADR 0055) of a sale a global invoice covers:
/// to the general public, related to the global invoice, for what was given back or forgiven with the rates the sale had in
/// it, never more than what the sale charged in it; TienditApp issues it on its own (also a voided sale's), a person issues
/// it again or cancels it, it never goes by email, and the global invoice is not cancelled while one stands.
/// </summary>
public class GlobalInvoiceCreditNoteFlowTests
{
    private const string Rfc = "EKU9003173C9";
    private const string OtherRfc = "XIA190128J61";
    private const string KarlaPhone = "+525512345678";

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
        // Nobody to tell (Fase 28N): it was for the general public.
        Assert.Empty(await book.Context.CfdiEmails.ToListAsync());

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
        var message = CreditNotes.AboveSaleMessage("F-1", sale.OrderNumber, CreditNoteAdjustment.Refund);
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

    [Fact]
    public async Task SaleWithARefundWithoutAFormOfTheSat_ShouldStayOutOfTheGlobalInvoice()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var other = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        await book.RefundAsync(other.Id, 20m, OrderPaymentMethod.Other);
        book.Clock.UtcNow = NextMorning;

        // Its refund would never get a credit note: the global invoice would declare what was given back.
        var draft = await book.DraftAsync();
        Assert.Equal([sale.OrderNumber], draft.Sales.Select(s => s.OrderNumber));
        var leftOut = Assert.Single(draft.LeftOut);
        Assert.Equal((other.OrderNumber, GlobalInvoiceDrafts.RefundWithoutFormReason), (leftOut.OrderNumber, leftOut.Reason));
        Assert.Contains(GlobalInvoiceDrafts.RefundsWarning(1), draft.Warnings);
    }

    [Fact]
    public async Task SalesOfACancelledGlobalInvoice_ShouldGoInAsTheyWere_AndThePreviewSayWhichRefundsTakeTheirCreditNote()
    {
        await using var book = await Book.OpenAsync();
        var card = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var other = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        var online = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var first = await book.IssueGlobalAsync(3, 300m);
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        Assert.True((await book.CancelGlobalAsync(first.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        // Since, one gave $20 back by card, another «Otro», and the third has a refund in course in Mercado Pago.
        await book.RefundAsync(card.Id, 20m, OrderPaymentMethod.Card);
        await book.RefundAsync(other.Id, 20m, OrderPaymentMethod.Other);
        book.Context.OnlinePaymentRefunds.Add(OnlinePaymentRefund.ForOrder(
            book.Tenant.Id, online.Id, Guid.NewGuid(), "MP-881", 100m, book.Clock.UtcNow));
        await book.Context.SaveChangesAsync();
        book.Context.ChangeTracker.Clear();

        // The three go in as the first one had them; the refund «Otro» is not promised a credit note.
        var draft = await book.DraftAsync();
        Assert.Equal(300m, draft.Total);
        Assert.Empty(draft.LeftOut);
        Assert.Equal(
            [
                GlobalInvoiceDrafts.RefundsWarning(1),
                GlobalInvoiceDrafts.CoveredRefundWithoutFormWarning(1),
                GlobalInvoiceDrafts.CoveredRefundInCourseWarning(1)
            ],
            draft.Warnings);

        // Once stamped, only the card refund takes its credit note.
        await book.IssueGlobalAsync(3, 300m);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        Assert.Equal(20m, (await book.Context.CreditNotes.SingleAsync()).Total);
    }

    [Fact]
    public async Task SaleGivenBackInPart_LeftOutOfAnEarlierGlobalInvoice_ShouldGoInTheNextOne_AndTakeItsCreditNote()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var other = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        var refund = await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        // The sale day's global invoice, stamped before Fase 28I, left the sale given back in part out.
        var earlier = GlobalInvoice.Request(
            book.Tenant.Id, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Daily, SaleDay), "A", 900, "01", "{}",
            86.21m, 13.79m, 100m, [new GlobalInvoiceSale(other.Id, other.OrderNumber, 100m)], Guid.NewGuid(), NextMorning);
        earlier.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "sw", NextMorning, "cfdi/a-900.xml");
        book.Context.GlobalInvoices.Add(earlier);
        await book.Context.SaveChangesAsync();
        book.Context.ChangeTracker.Clear();

        // The next day's global invoice takes it, for its total, as a sale of an earlier period.
        var nextDay = SaleDay.AddDays(1);
        book.Clock.UtcNow = NextMorning.AddDays(1);
        var draft = await book.DraftAsync(nextDay);
        Assert.Equal([sale.OrderNumber], draft.Sales.Select(s => s.OrderNumber));
        Assert.Contains(GlobalInvoiceDrafts.LateSalesWarning(1), draft.Warnings);
        Assert.Contains(GlobalInvoiceDrafts.RefundsWarning(1), draft.Warnings);
        var global = await book.IssueGlobalAsync(1, 100m, nextDay);

        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.Equal((refund.Id, (Guid?)global.Id, 20m), (note.OrderRefundId, note.GlobalInvoiceId, note.Total));
    }

    [Fact]
    public async Task GlobalInvoice_ShouldNotBeCancelled_WhileACreditNoteOfItsSalesStampsOrCancels()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var global = await book.IssueGlobalAsync(1, 100m);
        var refund = await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);

        // The PAC does not answer: the credit note is still being stamped.
        book.Gateway.Unavailable = true;
        var issued = await book.IssueCreditNoteAsync(refund.Id);
        Assert.True(issued.IsSuccess, issued.Error);
        Assert.Equal((OrderInvoiceStatus.Stamping, "F-1"), (issued.Value.Status, issued.Value.GlobalInvoiceNumber));
        book.Gateway.Unavailable = false;
        Assert.Equal(GlobalInvoicing.LiveCreditNotesMessage(["NC-1"]),
            (await book.CancelGlobalAsync(global.Id, CfdiCancellationMotive.NominativeInGlobalInvoice)).Error);
        // Pedidos says which go first when a customer asks for the sale's own invoice.
        Assert.Equal(OrderInvoicing.InGlobalInvoiceMessage("F-1", ["NC-1"]), (await book.OrderInvoicingAsync(sale.Id)).Problem);

        // Stamped, and its cancellation waits for the SAT: still in force.
        var retried = await new RetryCreditNoteCommandHandler(book.Context, book.Gateway, book.Storage, book.Clock, NullLogger<OrderInvoiceStamper>.Instance)
            .Handle(new RetryCreditNoteCommand(issued.Value.Id), CancellationToken.None);
        Assert.Equal((OrderInvoiceStatus.Stamped, "F-1"), (retried.Value.Status, retried.Value.GlobalInvoiceNumber));
        book.Context.ChangeTracker.Clear();
        var cancelling = await book.CancelCreditNoteAsync(issued.Value.Id, CfdiCancellationMotive.ErrorsWithoutReplacement);
        Assert.Equal((OrderInvoiceStatus.Cancelling, "F-1"), (cancelling.Value.Status, cancelling.Value.GlobalInvoiceNumber));
        Assert.Equal(GlobalInvoicing.LiveCreditNotesMessage(["NC-1"]),
            (await book.CancelGlobalAsync(global.Id, CfdiCancellationMotive.NominativeInGlobalInvoice)).Error);
        Assert.DoesNotContain(book.Gateway.Cancellations, c => c.Uuid == global.Uuid);
    }

    [Fact]
    public async Task VoidedSale_WhoseGlobalInvoiceWasCancelled_ShouldSayNoCfdiCoversIt()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var global = await book.IssueGlobalAsync(1, 100m);
        await book.RefundAsync(sale.Id, 100m, OrderPaymentMethod.Card);
        await book.CancelOrderAsync(sale.Id);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);

        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.True((await book.CancelCreditNoteAsync(note.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        Assert.True((await book.CancelGlobalAsync(global.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);

        // It never had an invoice of its own to cancel with 03.
        Assert.Equal(CreditNotes.CancelledOrderWithoutCfdiMessage, Assert.Single((await book.OrderInvoicingAsync(sale.Id)).Adjustments).Problem);
    }

    [Fact]
    public async Task CreditNotes_OfAGlobalInvoice_ShouldBeInvisibleToAnotherBusiness()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        var global = await book.IssueGlobalAsync(1, 100m);
        var refund = await book.RefundAsync(sale.Id, 20m, OrderPaymentMethod.Card);
        var issued = await book.IssueCreditNoteAsync(refund.Id);
        Assert.True(issued.IsSuccess, issued.Error);
        var pending = await book.RefundAsync(sale.Id, 10m, OrderPaymentMethod.Card);

        // Another business neither reads, issues, cancels nor downloads them, and its worker pass does not see them.
        await book.Fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.OrderInvoicingAsync(sale.Id));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.GlobalDetailAsync(global.Id));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.IssueCreditNoteAsync(pending.Id));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.CancelCreditNoteAsync(issued.Value.Id, CfdiCancellationMotive.ErrorsWithoutReplacement));
        var download = await new GetCreditNoteFileQueryHandler(
                book.Context, book.Storage, book.Gateway, NullLogger<GetCreditNoteFileQueryHandler>.Instance)
            .Handle(new GetCreditNoteFileQuery(issued.Value.Id, OrderInvoiceFileKind.Xml), CancellationToken.None);
        Assert.Equal(GetOrderInvoiceFileQueryHandler.NotFoundMessage, download.Error);
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);

        // Its own business still does.
        book.Fixture.TenantService.SetTenant(book.Tenant.Id);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        Assert.Equal(2, (await book.GlobalDetailAsync(global.Id)).Orders.Single().CreditNotes.Count);
    }

    [Fact]
    public async Task SaleSettledWithAWriteOff_ShouldGoInForItsTotal_AndEachWriteOffTakeItsCreditNote_ByCondonacionOrDiscount()
    {
        await using var book = await Book.OpenAsync();
        // Handed over owing $30 that the business forgives, and owing $20 that it takes off as a discount.
        var forgiven = await book.OwedSaleAsync(70m);
        var discounted = await book.OwedSaleAsync(80m, OrderPaymentMethod.Card);
        var forgivenWriteOff = await book.WriteOffAsync(forgiven.Id, 30m);
        var discount = await book.WriteOffAsync(discounted.Id, 20m, OrderWriteOffReason.Discount);
        book.Clock.UtcNow = NextMorning;

        // Settled, both go in for their total, and the preview says which credit notes follow.
        var draft = await book.DraftAsync();
        Assert.Equal([forgiven.OrderNumber, discounted.OrderNumber], draft.Sales.Select(s => s.OrderNumber));
        Assert.Empty(draft.LeftOut);
        Assert.Equal(200m, draft.Total);
        Assert.Contains(GlobalInvoiceDrafts.WriteOffsWarning(2), draft.Warnings);
        var global = await book.IssueGlobalAsync(2, 200m);

        Assert.Equal(2, (await book.IssuePendingAsync()).Value);

        var notes = await book.Context.CreditNotes.OrderBy(n => n.Folio).ToListAsync();
        Assert.Equal(
            [
                ("NC-1", forgivenWriteOff.Id, (Guid?)global.Id, (Guid?)null, "15", 25.86m, 4.14m, 30m, OrderInvoiceStatus.Stamped),
                ("NC-2", discount.Id, (Guid?)global.Id, (Guid?)null, "15", 17.24m, 2.76m, 20m, OrderInvoiceStatus.Stamped)
            ],
            notes.Select(n => (n.Number, n.OrderWriteOffId!.Value, n.GlobalInvoiceId, n.OrderRefundId, n.PaymentForm, n.SubTotal,
                n.TaxTotal, n.Total, n.Status)));
        // Egreso, in one exhibition by «Condonación» (15), related to the global invoice, to the general public; its concept
        // says whether the business forgave a debt or gave a discount.
        var documents = book.Gateway.StampRequests.TakeLast(2).Select(r => r.Document).ToList();
        Assert.All(documents, document =>
        {
            Assert.Equal(("E", "PUE", "15"), (document.VoucherType, document.PaymentMethod, document.PaymentForm));
            Assert.Equal(("01", global.Uuid), (document.Related!.RelationType, Assert.Single(document.Related.Uuids)));
            Assert.Equal(new CfdiDocumentReceiver("XAXX010101000", "PUBLICO EN GENERAL", "06000", "616", "S01"), document.Receiver);
        });
        Assert.Equal(
            [("Condonación", forgiven.OrderNumber), ("Descuento o bonificación", discounted.OrderNumber)],
            documents.Select(d => (Assert.Single(d.Concepts).Description, d.Concepts[0].Identification)));
        // Nobody to send them to.
        Assert.Empty(await book.Context.CfdiEmails.ToListAsync());

        // Pedidos and the global invoice's detail show each one under its sale; nothing is issued twice.
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var invoicing = await book.OrderInvoicingAsync(discounted.Id);
        Assert.Equal("F-1", invoicing.GlobalInvoiceNumber);
        var shown = Assert.Single(invoicing.Adjustments);
        Assert.Equal((CreditNoteAdjustment.WriteOff, discount.Id, (OrderWriteOffReason?)OrderWriteOffReason.Discount, false, false, (string?)null),
            (shown.Kind, shown.AdjustmentId, shown.WriteOffReason, shown.CanIssue, shown.AwaitsAutomaticIssue, shown.Problem));
        Assert.Equal(("NC-2", "F-1"), (Assert.Single(shown.CreditNotes).Number, shown.CreditNotes[0].GlobalInvoiceNumber));
        var detail = await book.GlobalDetailAsync(global.Id);
        Assert.Equal(["NC-1"], detail.Orders.Single(o => o.OrderId == forgiven.Id).CreditNotes.Select(n => n.Number));
        Assert.Equal(["NC-2"], detail.Orders.Single(o => o.OrderId == discounted.Id).CreditNotes.Select(n => n.Number));
        // And the global invoice is not cancelled while they stand.
        Assert.Equal(GlobalInvoicing.LiveCreditNotesMessage(["NC-1", "NC-2"]),
            (await book.CancelGlobalAsync(global.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).Error);
    }

    [Fact]
    public async Task SaleStillOwed_SoldOnCredit_OrGivenBackWholeBetweenRefundsAndWriteOffs_ShouldStayOutOfTheGlobalInvoice()
    {
        await using var book = await Book.OpenAsync();
        var owed = await book.OwedSaleAsync(70m);
        var onCredit = await book.OwedSaleAsync(70m, onCredit: true);
        // Sold on credit and still owed: its reason is that it was sold on credit, not that it is owed.
        var owedOnCredit = await book.OwedSaleAsync(70m, onCredit: true);
        var givenBack = await book.OwedSaleAsync(70m);
        var paid = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        await book.WriteOffAsync(onCredit.Id, 30m);
        await book.WriteOffAsync(givenBack.Id, 30m);
        await book.RefundAsync(givenBack.Id, 70m, OrderPaymentMethod.Cash);
        book.Clock.UtcNow = NextMorning;

        var draft = await book.DraftAsync();

        Assert.Equal([paid.OrderNumber], draft.Sales.Select(s => s.OrderNumber));
        Assert.Equal(
            [
                (owed.OrderNumber, GlobalInvoiceDrafts.NotPaidReason),
                (onCredit.OrderNumber, GlobalInvoiceDrafts.CreditReason),
                (owedOnCredit.OrderNumber, GlobalInvoiceDrafts.CreditReason),
                (givenBack.OrderNumber, GlobalInvoiceDrafts.FullyGivenBackReason)
            ],
            draft.LeftOut.Select(l => (l.OrderNumber, l.Reason)));
        Assert.DoesNotContain(draft.Warnings, w => w.Contains("condonaciones", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaleSettledWithAWriteOff_AfterItsPeriodsGlobalInvoice_ShouldGoInTheNextOne_AndTakeItsCreditNote()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.OwedSaleAsync(70m);
        await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        // Still owed when the sale day's global invoice is stamped: it stays out.
        var draft = await book.DraftAsync(SaleDay);
        Assert.Equal(GlobalInvoiceDrafts.NotPaidReason, draft.LeftOut.Single(l => l.OrderId == sale.Id).Reason);
        await book.IssueGlobalAsync(1, 100m);

        // The next day the business forgives what is left: settled, it goes in the next global invoice, for its total.
        var writeOff = await book.WriteOffAsync(sale.Id, 30m);
        var nextDay = SaleDay.AddDays(1);
        book.Clock.UtcNow = NextMorning.AddDays(1);
        draft = await book.DraftAsync(nextDay);
        Assert.Equal([sale.OrderNumber], draft.Sales.Select(s => s.OrderNumber));
        Assert.Contains(GlobalInvoiceDrafts.LateSalesWarning(1), draft.Warnings);
        Assert.Contains(GlobalInvoiceDrafts.WriteOffsWarning(1), draft.Warnings);
        var global = await book.IssueGlobalAsync(1, 100m, nextDay);

        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.Equal((writeOff.Id, (Guid?)global.Id, "F-2", 30m), (note.OrderWriteOffId, note.GlobalInvoiceId, global.Number, note.Total));
    }

    [Fact]
    public async Task WriteOff_OfASaleOfTheGlobalInvoice_ShouldNotBeUndone_WhileTheGlobalInvoiceCoversIt()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.OwedSaleAsync(70m);
        await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        var writeOff = await book.WriteOffAsync(sale.Id, 30m);
        await book.IssueGlobalAsync(2, 200m);

        // The global invoice took it settled: undone, the sale would owe inside a CFDI paid in one exhibition. Before its credit
        // note is issued, and after.
        var inGlobal = ReverseCustomerWriteOffCommandHandler.InGlobalInvoiceMessage("F-1");
        Assert.Equal(inGlobal, (await book.ReverseWriteOffAsync(writeOff.Id)).Error);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        Assert.Equal(inGlobal, (await book.ReverseWriteOffAsync(writeOff.Id)).Error);
        // Its credit note cancelled, the global invoice still covers it.
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.True((await book.CancelCreditNoteAsync(note.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        Assert.Equal(inGlobal, (await book.ReverseWriteOffAsync(writeOff.Id)).Error);

        // With the global invoice cancelled it is undone, takes no credit note, and the global invoice issued again leaves it
        // out while it owes.
        Assert.True((await book.CancelGlobalAsync((await book.Context.GlobalInvoices.SingleAsync()).Id,
            CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        var undone = await book.ReverseWriteOffAsync(writeOff.Id);
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var draft = await book.DraftAsync();
        Assert.Equal(GlobalInvoiceDrafts.NotPaidReason, Assert.Single(draft.LeftOut).Reason);
        Assert.Equal(100m, draft.Total);
    }

    [Fact]
    public async Task WriteOff_OfAGlobalInvoiceCancelledAndIssuedAgain_ShouldTakeItsCreditNoteOfTheNewOne()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.OwedSaleAsync(70m);
        var writeOff = await book.WriteOffAsync(sale.Id, 30m, OrderWriteOffReason.Discount);
        var first = await book.IssueGlobalAsync(1, 100m);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);

        // Its credit note and the global invoice cancelled with 02 to correct it: the sale goes in the new one as it was, and the
        // discount takes its credit note of the new one, on its own.
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        var note = await book.Context.CreditNotes.SingleAsync();
        Assert.True((await book.CancelCreditNoteAsync(note.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        Assert.True((await book.CancelGlobalAsync(first.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        var second = await book.IssueGlobalAsync(1, 100m);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);

        var issued = await book.Context.CreditNotes.SingleAsync(n => n.Status == OrderInvoiceStatus.Stamped);
        Assert.Equal(("NC-2", writeOff.Id, (Guid?)second.Id, 30m), (issued.Number, issued.OrderWriteOffId!.Value, issued.GlobalInvoiceId, issued.Total));
        Assert.Equal(("01", second.Uuid), (book.Gateway.StampRequests[^1].Document.Related!.RelationType,
            Assert.Single(book.Gateway.StampRequests[^1].Document.Related!.Uuids)));
    }

    [Fact]
    public void SaleRoom_ShouldCountTheCreditNotesOfItsRefundsAndWriteOffs_ButNotItsOwnNorAnUndoneWriteOffs()
    {
        var now = new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var order = new Order(tenantId, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla", KarlaPhone,
            null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        order.RegisterPayment(150m, OrderPaymentMethod.Cash, now, null, null, null, null);
        order.TransitionTo(OrderStatus.Delivered, OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail), "cajero");
        var refund = order.RegisterRefund(40m, OrderPaymentMethod.Cash, "Devolución", now, "cajero", null, null, false, []);
        var forgiven = order.WriteOff(30m, OrderWriteOffReason.WontBeCollected, null, now, "dueña");
        var undone = order.WriteOff(20m, OrderWriteOffReason.Courtesy, null, now, "dueña");
        var global = GlobalInvoice.Request(
            tenantId, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Daily, SaleDay), "F", 1, "01", "{}",
            172.41m, 27.59m, 200m, [new GlobalInvoiceSale(order.Id, order.OrderNumber, 200m)], Guid.NewGuid(), now);
        global.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "sw", now, "cfdi/f-1.xml");
        var sale = global.Orders.Single();
        var refundNote = CreditNote.ForRefundInGlobalInvoice(global, sale, refund, "NC", 1, 34.48m, 5.52m, "{}", null, now);
        // Refused, and issued again: only the latest counts.
        var refusedNote = CreditNote.ForWriteOffInGlobalInvoice(global, sale, forgiven, "NC", 2, 25.86m, 4.14m, "{}", null, now);
        refusedNote.Refuse("CFDI40108", now);
        var forgivenNote = CreditNote.ForWriteOffInGlobalInvoice(global, sale, forgiven, "NC", 3, 25.86m, 4.14m, "{}", null, now.AddMinutes(1));
        var undoneNote = CreditNote.ForWriteOffInGlobalInvoice(global, sale, undone, "NC", 4, 17.24m, 2.76m, "{}", null, now);
        order.ReverseWriteOff(undone.Id, now.AddMinutes(2), "dueña");

        var standing = CreditNotes.SaleStandingOf([refundNote, refusedNote, forgivenNote, undoneNote], [undone.Id]);

        Assert.Equal([refundNote.Id, forgivenNote.Id], standing.Select(n => n.Id).OrderBy(id => id == forgivenNote.Id));
        // $200 − $40 − $30, for a new adjustment; each one's own credit note does not count against it.
        Assert.Equal(130m, CreditNotes.SaleRoom(sale, standing, Guid.NewGuid()));
        Assert.Equal(170m, CreditNotes.SaleRoom(sale, standing, refund.Id));
        Assert.Equal(160m, CreditNotes.SaleRoom(sale, standing, forgiven.Id));
    }

    [Fact]
    public async Task WriteOffCreditNotes_OfAGlobalInvoice_ShouldBeInvisibleToAnotherBusiness()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.OwedSaleAsync(70m);
        var writeOff = await book.WriteOffAsync(sale.Id, 30m);
        var global = await book.IssueGlobalAsync(1, 100m);

        // Another business neither reads, issues nor takes it in its worker pass.
        await book.Fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.OrderInvoicingAsync(sale.Id));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.IssueCreditNoteAsync(CreditNoteAdjustment.WriteOff, writeOff.Id));
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        Assert.Empty(await book.Context.CreditNotes.IgnoreQueryFilters().ToListAsync());

        // Its own business does.
        book.Fixture.TenantService.SetTenant(book.Tenant.Id);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        Assert.Equal(["NC-1"], (await book.GlobalDetailAsync(global.Id)).Orders.Single().CreditNotes.Select(n => n.Number));
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
            // Karla may buy on credit (Fase 26A).
            var karla = new Customer(tenant.Id, "Karla", KarlaPhone, null);
            karla.SetCreditLimit(1_000m);
            fixture.Context.Customers.Add(karla);
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
        /// A sale of Café to Karla handed over with only part of it paid on the sale day (or sold on credit): she owes the
        /// rest, which the business may forgive.
        /// </summary>
        public async Task<OrderDto> OwedSaleAsync(decimal paid, OrderPaymentMethod method = OrderPaymentMethod.Cash, bool onCredit = false)
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Karla",
                CustomerPhone = KarlaPhone,
                OnCredit = onCredit,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = paid, Method = method }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            var order = await Context.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == created.Value.Id);
            if (order.Status != OrderStatus.Delivered && order.Status != OrderStatus.Completed)
                order.TransitionTo(OrderStatus.Delivered, OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail), "cajero");
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return created.Value;
        }

        public async Task<OrderWriteOff> WriteOffAsync(
            Guid orderId, decimal amount, OrderWriteOffReason reason = OrderWriteOffReason.WontBeCollected)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.WriteOffs).SingleAsync(o => o.Id == orderId);
            var writeOff = order.WriteOff(amount, reason, null, Clock.UtcNow, "dueña");
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return writeOff;
        }

        public async Task<Result<CustomerAccountDto>> ReverseWriteOffAsync(Guid writeOffId)
        {
            var reversed = await new ReverseCustomerWriteOffCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, new PhoneNormalizationService(), Clock,
                    new CustomerCreditLock(Context), new InvoicingLock(Context))
                .Handle(new ReverseCustomerWriteOffCommand { WriteOffId = writeOffId }, CancellationToken.None);
            Context.ChangeTracker.Clear();
            return reversed;
        }

        /// <summary>
        /// Stamps the sale day's global invoice the next morning (or later), as its preview showed it, and expects it saved.
        /// </summary>
        public async Task<GlobalInvoiceDto> IssueGlobalAsync(int saleCount, decimal total, DateOnly? day = null)
        {
            if (Clock.UtcNow < NextMorning)
                Clock.UtcNow = NextMorning;
            var draft = await DraftAsync(day);
            var issued = await new IssueGlobalInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueGlobalInvoiceCommand(GlobalInvoicePeriodicity.Daily, day ?? SaleDay, saleCount, total, draft.Fingerprint),
                    CancellationToken.None);
            Assert.True(issued.IsSuccess, issued.Error);
            Context.ChangeTracker.Clear();
            return issued.Value;
        }

        /// <summary>The preview of the daily global invoice of that day (the sale day by default).</summary>
        public Task<GlobalInvoiceDraftDto> DraftAsync(DateOnly? day = null)
            => new GetGlobalInvoiceDraftQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new GetGlobalInvoiceDraftQuery(GlobalInvoicePeriodicity.Daily, day ?? SaleDay), CancellationToken.None);

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

        public Task<Result<CreditNoteDto>> IssueCreditNoteAsync(Guid refundId)
            => IssueCreditNoteAsync(CreditNoteAdjustment.Refund, refundId);

        public async Task<Result<CreditNoteDto>> IssueCreditNoteAsync(CreditNoteAdjustment adjustment, Guid adjustmentId)
        {
            var issued = await new IssueCreditNoteCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueCreditNoteCommand(adjustment, adjustmentId), CancellationToken.None);
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
