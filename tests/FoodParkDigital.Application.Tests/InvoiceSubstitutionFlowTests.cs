using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Replacing an invoice with errors (Fase 28G, ADR 0051): the new one carries the old one's UUID with the relation 04 and is
/// stamped first; then the old one is cancelled with motive 01 and the new one's UUID, now or by the worker once the SAT has
/// the new one. A refused replacement gives the order its invoice back, and what comes next (credit notes, complements) goes
/// to the replacement.
/// </summary>
public class InvoiceSubstitutionFlowTests
{
    private const string Rfc = "EKU9003173C9";

    // Karla wrote her postal code wrong the first time.
    private static readonly CfdiReceiverInput Wrong = new()
    {
        Rfc = "XIQB891116QE4", Name = "Karla Fuente Nolasco", PostalCode = "26016", FiscalRegime = "612", CfdiUse = "G03"
    };

    private static readonly CfdiReceiverInput Right = Wrong with { PostalCode = "26015" };

    [Fact]
    public async Task Replacement_ShouldBeStampedRelatedWith04_AndTheOldOneCancelledWith01()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        Assert.Equal(OrderInvoiceStatus.Stamped, original.Status);
        Assert.True((await book.InvoicingAsync(sale.Id)).Invoices.Single().CanSubstitute);

        var replaced = await book.SubstituteAsync(original.Id, Right, email: "karla@example.com");

        Assert.True(replaced.IsSuccess, replaced.Error);
        var replacement = replaced.Value;
        Assert.Equal(("F-2", OrderInvoiceStatus.Stamped, "26015", original.Id, "F-1"),
            (replacement.Number, replacement.Status, replacement.Receiver.PostalCode, replacement.ReplacesInvoiceId, replacement.ReplacesNumber));
        // The new CFDI names the old one with the relation 04, and the old one is cancelled with 01 naming the new one.
        var stamped = book.Gateway.StampRequests[^1].Document;
        Assert.Equal((SatCatalog.SubstitutionRelation, original.Uuid), (stamped.Related!.RelationType, Assert.Single(stamped.Related.Uuids)));
        Assert.Equal((Rfc, original.Uuid!, CfdiCancellationMotive.ErrorsWithReplacement), book.Gateway.Cancellations.Single());
        Assert.Equal(replacement.Uuid, book.Gateway.Replacements[original.Uuid!]);

        var invoicing = await book.InvoicingAsync(sale.Id);
        var (newer, older) = (invoicing.Invoices[0], invoicing.Invoices[1]);
        Assert.Equal(("F-2", "F-1", (string?)null), (newer.Number, newer.ReplacesNumber, newer.ReplacedByNumber));
        Assert.Equal(("F-1", OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.ErrorsWithReplacement, "F-2"),
            (older.Number, older.Status, older.CancellationMotive, older.ReplacedByNumber));
        Assert.False(invoicing.CanInvoice);
        // Only the order's invoice now says whether it can be replaced: the new one waits until the SAT cancels the old one.
        Assert.Equal((false, null), (older.CanSubstitute, older.SubstitutionProblem));
        Assert.Equal((false, OrderInvoiceSubstitutions.ReplacedFirstMessage("F-1")), (newer.CanSubstitute, newer.SubstitutionProblem));
        Assert.Equal("karla@example.com", newer.ReceiverEmail);

        var requested = await book.Context.AuditLogs.SingleAsync(a => a.Action == "OrderInvoiceRequested" && a.EntityId == replacement.Id.ToString());
        Assert.Contains("\"Replaces\":\"F-1\"", requested.Metadata);
        var cancellation = await book.Context.AuditLogs.SingleAsync(a => a.Action == "OrderInvoiceCancellationRequested");
        Assert.Contains("\"Motive\":\"01\"", cancellation.Metadata);
        Assert.Contains("\"ReplacedBy\":\"F-2\"", cancellation.Metadata);

        // The SAT cancels it (the customer accepted, or it needed no acceptance): it stays, replaced.
        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.Cancelled, false);
        Assert.Equal(1, (await book.CheckCancellationsAsync()).Value);
        var cancelled = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Cancelled, replacement.Id), (cancelled.Status, cancelled.ReplacedByInvoiceId));
        Assert.True((await book.InvoiceAsync(replacement.Id)).IsCurrent);
    }

    [Fact]
    public async Task Cancellation_ThatTheSatDoesNotTakeYet_ShouldKeepItsReason_AndBeAskedAgainByTheWorker_ThenByAPerson()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.CancelRefusal = "El SAT todavía no relaciona esta factura con la que la sustituye.";

        var replaced = await book.SubstituteAsync(original.Id, Right);

        // The replacement stands; the old one waits, replaced, with the reason.
        Assert.True(replaced.IsSuccess, replaced.Error);
        var waiting = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Stamped, replaced.Value.Id, book.Gateway.CancelRefusal),
            (waiting.Status, waiting.ReplacedByInvoiceId, waiting.FailureMessage));
        Assert.Empty(book.Gateway.Cancellations);

        // The worker asks again in its cancellations pass, and the SAT takes it.
        book.Gateway.CancelRefusal = null;
        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(30);
        Assert.Equal(1, (await book.CheckCancellationsAsync()).Value);
        var asked = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.ErrorsWithReplacement, null),
            (asked.Status, asked.CancellationMotive, asked.FailureMessage));
        // Asked for by whoever replaced it.
        Assert.Equal(book.Fixture.CurrentUser.UserId, asked.CancellationRequestedByUserId.ToString());

        // Another one that still fails after 72 hours waits for a person, who can ask again.
        var other = await book.PaidOrderAsync();
        var otherOriginal = (await book.IssueAsync(other.Id, Wrong)).Value;
        book.Gateway.CancelRefusal = "El SAT no relaciona la factura que la sustituye.";
        var otherReplacement = (await book.SubstituteAsync(otherOriginal.Id, Right)).Value;
        book.Gateway.CancelRefusal = null;
        book.Clock.UtcNow = otherReplacement.StampedAt!.Value + OrderInvoiceSubstitutions.CancellationRetryWindow + TimeSpan.FromMinutes(1);
        await book.CheckCancellationsAsync();
        Assert.Equal(OrderInvoiceStatus.Stamped, (await book.InvoiceAsync(otherOriginal.Id)).Status);

        var retried = await book.CancelReplacedAsync(otherOriginal.Id);
        Assert.True(retried.IsSuccess, retried.Error);
        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.ErrorsWithReplacement), (retried.Value.Status, retried.Value.CancellationMotive));
        // Asking again changes nothing.
        Assert.True((await book.CancelReplacedAsync(otherOriginal.Id)).IsSuccess);
        Assert.Equal(2, book.Gateway.Cancellations.Count);
    }

    [Fact]
    public async Task ReplacedInvoice_ShouldSayWhileTheWorkerStillAsksTheSat_AndWhyItStopped()
    {
        await using var book = await Book.OpenAsync();
        async Task<OrderInvoiceDto> ReplacedAsync(Guid orderId, Guid invoiceId)
            => (await book.InvoicingAsync(orderId)).Invoices.Single(i => i.Id == invoiceId);

        // While its replacement is stamping, and once the worker stamps it, TienditApp asks the SAT on its own.
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.LoseNextAnswer = true;
        await book.SubstituteAsync(original.Id, Right);
        Assert.True((await ReplacedAsync(sale.Id, original.Id)).AwaitsReplacedCancellation);
        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(2);
        Assert.Equal(1, (await book.StampPendingAsync()).Value);
        var replacement = (await book.InvoicingAsync(sale.Id)).Invoices.Single(i => i.ReplacesInvoiceId == original.Id);
        Assert.True((await ReplacedAsync(sale.Id, original.Id)).AwaitsReplacedCancellation);

        // 72 hours after its replacement was stamped the worker no longer asks, though nothing said why: a person does.
        book.Clock.UtcNow = replacement.StampedAt!.Value + OrderInvoiceSubstitutions.CancellationRetryWindow + TimeSpan.FromMinutes(1);
        var late = await ReplacedAsync(sale.Id, original.Id);
        Assert.Equal((OrderInvoiceStatus.Stamped, (string?)null, false), (late.Status, late.FailureMessage, late.AwaitsReplacedCancellation));
        Assert.Equal(0, (await book.CheckCancellationsAsync()).Value);
        Assert.Empty(book.Gateway.Cancellations);
    }

    [Fact]
    public async Task ReplacedInvoice_WhoseBusinessHasNoCsd_ShouldKeepWhyTheWorkerCannotAsk()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.LoseNextAnswer = true;
        await book.SubstituteAsync(original.Id, Right);
        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(2);
        Assert.Equal(1, (await book.StampPendingAsync()).Value);
        Assert.True((await book.InvoicingAsync(sale.Id)).Invoices.Single(i => i.Id == original.Id).AwaitsReplacedCancellation);

        // The owner removes the CSD before the worker's next pass: it cannot ask the SAT, the invoice keeps why, and TienditApp
        // no longer says it asks.
        (await book.Context.InvoicingProfiles.SingleAsync()).RemoveCertificate();
        await book.Context.SaveChangesAsync();
        book.Context.ChangeTracker.Clear();

        Assert.Equal(0, (await book.CheckCancellationsAsync()).Value);
        var withoutCsd = (await book.InvoicingAsync(sale.Id)).Invoices.Single(i => i.Id == original.Id);
        Assert.Equal((OrderInvoiceStatus.Stamped, CancelOrderInvoiceCommandHandler.NoCertificateToCancelMessage, false),
            (withoutCsd.Status, withoutCsd.FailureMessage, withoutCsd.AwaitsReplacedCancellation));
        Assert.Empty(book.Gateway.Cancellations);
    }

    [Fact]
    public async Task RefusedReplacement_ShouldGiveTheOrderItsInvoiceBack_ToBeReplacedAgain()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.Refusal = "CFDI40147 - El código postal del receptor no corresponde al RFC.";

        var refused = await book.SubstituteAsync(original.Id, Right);

        Assert.True(refused.IsSuccess, refused.Error);
        // It replaces nothing, and the answer says so as the order's invoices do.
        Assert.Equal((OrderInvoiceStatus.Refused, (Guid?)null, (string?)null),
            (refused.Value.Status, refused.Value.ReplacesInvoiceId, refused.Value.ReplacesNumber));
        var kept = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Stamped, (Guid?)null, true), (kept.Status, kept.ReplacedByInvoiceId, kept.IsCurrent));
        Assert.Empty(book.Gateway.Cancellations);

        book.Gateway.Refusal = null;
        var replaced = await book.SubstituteAsync(original.Id, Right);
        Assert.Equal((OrderInvoiceStatus.Stamped, "F-3"), (replaced.Value.Status, replaced.Value.Number));
        Assert.Equal(replaced.Value.Id, (await book.InvoiceAsync(original.Id)).ReplacedByInvoiceId);
    }

    [Fact]
    public async Task ReplacementWithoutAnAnswer_ShouldBeStampedByTheWorker_AndTheOldOneCancelledInItsNextPass()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.LoseNextAnswer = true;

        var stamping = await book.SubstituteAsync(original.Id, Right);

        Assert.Equal(OrderInvoiceStatus.Stamping, stamping.Value.Status);
        Assert.Equal((OrderInvoiceStatus.Stamped, stamping.Value.Id), ((await book.InvoiceAsync(original.Id)).Status, (await book.InvoiceAsync(original.Id)).ReplacedByInvoiceId));
        // While the new one is stamping, the old one is not cancelled: the SAT would not have what replaces it.
        Assert.Equal(OrderInvoiceSubstitutions.ReplacementNotStampedMessage("F-2"), (await book.CancelReplacedAsync(original.Id)).Error);
        Assert.Equal(0, (await book.CheckCancellationsAsync()).Value);
        Assert.Empty(book.Gateway.Cancellations);

        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(2);
        Assert.Equal(1, (await book.StampPendingAsync()).Value);
        Assert.Equal(1, (await book.CheckCancellationsAsync()).Value);
        Assert.Equal(OrderInvoiceStatus.Cancelling, (await book.InvoiceAsync(original.Id)).Status);
    }

    [Fact]
    public async Task ReplacementStampedByAskingAgain_ShouldCancelTheOldOneRightAway()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.LoseNextAnswer = true;
        var stamping = (await book.SubstituteAsync(original.Id, Right)).Value;

        var stamped = await book.RetryAsync(stamping.Id);

        Assert.Equal(OrderInvoiceStatus.Stamped, stamped.Value.Status);
        var old = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.ErrorsWithReplacement), (old.Status, old.CancellationMotive));
        // Asked for by whoever replaced it, as the worker does.
        Assert.Equal(stamping.Id, old.ReplacedByInvoiceId);
        Assert.Equal((await book.InvoiceAsync(stamping.Id)).RequestedByUserId, old.CancellationRequestedByUserId);
        Assert.Equal(book.Gateway.Replacements[original.Uuid!], stamped.Value.Uuid);
    }

    [Fact]
    public async Task StuckReplacement_FoundStampedInTheSatsPortal_ShouldCancelTheOldOneWith01()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        // The PAC stamped the replacement, but its answer never arrived, and the worker stopped asking at 71 hours.
        book.Gateway.LoseNextAnswer = true;
        var stuck = (await book.SubstituteAsync(original.Id, Right)).Value;
        var made = book.Gateway.Stamped.Values.Single(s => s.Uuid != original.Uuid);
        book.Clock.UtcNow += StampPendingInvoicesCommandHandler.RetryWindow + TimeSpan.FromHours(1);

        var confirmed = await book.ConfirmStampedAsync(stuck.Id, made.Uuid);

        Assert.True(confirmed.IsSuccess, confirmed.Error);
        var old = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.ErrorsWithReplacement), (old.Status, old.CancellationMotive));
        Assert.Equal(book.Fixture.CurrentUser.UserId, old.CancellationRequestedByUserId.ToString());
        Assert.Equal(made.Uuid, book.Gateway.Replacements[original.Uuid!]);
        Assert.Contains(book.Gateway.Cancellations, c => c.Uuid == original.Uuid);
    }

    [Fact]
    public async Task AskingAgainForTheStampOfAReplacementAlreadyStamped_ShouldNotAskTheSatToCancelTheOldOne()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        // The SAT did not take the 01 when the replacement was stamped, and 72 hours went by: only an owner or an
        // administrator asks for it now.
        book.Gateway.CancelRefusal = "El SAT todavía no relaciona esta factura con la que la sustituye.";
        var replacement = (await book.SubstituteAsync(original.Id, Right)).Value;
        book.Gateway.CancelRefusal = null;
        book.Clock.UtcNow = replacement.StampedAt!.Value + OrderInvoiceSubstitutions.CancellationRetryWindow + TimeSpan.FromMinutes(1);

        // «Volver a pedir el timbre» (ManageOrders, as a cashier has) stamps nothing here, so it cancels nothing either.
        var retried = await book.RetryAsync(replacement.Id);

        Assert.Equal(OrderInvoiceStatus.Stamped, retried.Value.Status);
        Assert.Empty(book.Gateway.Cancellations);
        var old = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Stamped, replacement.Id), (old.Status, old.ReplacedByInvoiceId));
    }

    [Fact]
    public async Task OldInvoiceCancelledWithout02_WhileItsReplacementWaits_ShouldStayCancelled_IfThePacRefusesTheReplacement()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.LoseNextAnswer = true;
        var stamping = (await book.SubstituteAsync(original.Id, Right)).Value;
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;

        // Without waiting for the new one, the owner cancels the old one without relation.
        Assert.True((await book.CancelAsync(original.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        book.Gateway.Refusal = "CFDI40147 - El receptor no está en la lista de RFC inscritos.";
        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(2);
        Assert.True((await book.StampPendingAsync()).IsSuccess);

        Assert.Equal(OrderInvoiceStatus.Refused, (await book.InvoiceAsync(stamping.Id)).Status);
        var old = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Cancelled, (Guid?)null), (old.Status, old.ReplacedByInvoiceId));
        // Nothing stands for the order now: it is invoiced again from Pedidos.
        Assert.True((await book.InvoicingAsync(sale.Id)).CanInvoice);
    }

    [Fact]
    public async Task Substitution_ShouldOnlyGoForTheOrdersStampedInvoice()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        var replacement = (await book.SubstituteAsync(original.Id, Right)).Value;

        // Not the one already replaced; and the replacement, only once the SAT cancels the one it replaces.
        Assert.Equal(OrderInvoice.AlreadyReplacedMessage, (await book.SubstituteAsync(original.Id, Right)).Error);
        Assert.Equal(OrderInvoiceSubstitutions.ReplacedFirstMessage("F-1"), (await book.SubstituteAsync(replacement.Id, Right)).Error);
        Assert.Equal(OrderInvoiceSubstitutions.ReplacedFirstMessage("F-1"), (await book.InvoicingAsync(sale.Id)).Invoices[0].SubstitutionProblem);
        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.Cancelled, false);
        await book.CheckCancellationsAsync();
        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.InForce, false);
        var again = await book.SubstituteAsync(replacement.Id, Right with { CfdiUse = "S01" });
        Assert.True(again.IsSuccess, again.Error);

        // One still stamping, one cancelled.
        var other = await book.PaidOrderAsync();
        book.Gateway.LoseNextAnswer = true;
        var stamping = (await book.IssueAsync(other.Id, Wrong)).Value;
        Assert.Equal("La factura F-4 todavía se está timbrando.", (await book.SubstituteAsync(stamping.Id, Right)).Error);
        var third = await book.PaidOrderAsync();
        var toCancel = (await book.IssueAsync(third.Id, Wrong)).Value;
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        Assert.True((await book.CancelAsync(toCancel.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        Assert.Equal("Solo se sustituye una factura timbrada.", (await book.SubstituteAsync(toCancel.Id, Right)).Error);

        // Not without the PAC, and not another business's (or one that does not exist): it is not there.
        book.Gateway.IsConfigured = false;
        Assert.Equal(InvoicingSettings.NotAvailableMessage, (await book.SubstituteAsync(replacement.Id, Right)).Error);
        book.Gateway.IsConfigured = true;
        await Assert.ThrowsAsync<Domain.Exceptions.EntityNotFoundException>(() => book.SubstituteAsync(Guid.NewGuid(), Right));
    }

    [Fact]
    public async Task Replacement_ShouldNotBeCancelled_BeforeTheOneItReplaces()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        var replacement = (await book.SubstituteAsync(original.Id, Right)).Value;

        var refused = await book.CancelAsync(replacement.Id, CfdiCancellationMotive.NotCarriedOut);
        Assert.Equal(CancelOrderInvoiceCommandHandler.ReplacedFirstMessage("F-1"), refused.Error);

        // Once the SAT cancels the old one, the new one can be cancelled too (the sale did not happen after all).
        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.Cancelled, false);
        await book.CheckCancellationsAsync();
        Assert.True((await book.CancelAsync(replacement.Id, CfdiCancellationMotive.NotCarriedOut)).IsSuccess);
    }

    [Fact]
    public async Task CancellationTheCustomerRefusedBeforeTheReplacement_ShouldNotKeepTheWorkerFromAskingWith01()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        // First it was cancelled without relation, and the customer refused it.
        Assert.True((await book.CancelAsync(original.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.InForce, true);
        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(30);
        await book.CheckCancellationsAsync();
        Assert.Equal(OrderInvoiceStatus.Stamped, (await book.InvoiceAsync(original.Id)).Status);
        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.InForce, false);

        // Then it is replaced, before the SAT has the new one.
        book.Gateway.CancelRefusal = "El SAT todavía no relaciona esta factura con la que la sustituye.";
        Assert.Equal(OrderInvoiceStatus.Stamped, (await book.SubstituteAsync(original.Id, Right)).Value.Status);
        book.Gateway.CancelRefusal = null;

        // The worker asks again with 01: the refusal it had before it was replaced does not count.
        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(30);
        await book.CheckCancellationsAsync();
        var asked = await book.InvoiceAsync(original.Id);
        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.ErrorsWithReplacement), (asked.Status, asked.CancellationMotive));
    }

    [Fact]
    public async Task ReplacedInvoice_RefusedBy01_ShouldStillBeCancelledWithout02_AsTheSatSays()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.CancelRefusal = "El SAT dice que esta factura no se puede cancelar.";
        await book.SubstituteAsync(original.Id, Right);
        book.Gateway.CancelRefusal = null;

        var cancelled = await book.CancelAsync(original.Id, CfdiCancellationMotive.ErrorsWithoutReplacement);

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(CfdiCancellationMotive.ErrorsWithoutReplacement, cancelled.Value.CancellationMotive);
        // It is no longer asked again with 01.
        await book.CheckCancellationsAsync();
        Assert.Equal([CfdiCancellationMotive.ErrorsWithoutReplacement], book.Gateway.Cancellations.Select(c => c.Motive));
    }

    [Fact]
    public async Task WhatComesNext_ShouldGoToTheReplacement_NotToTheReplacedInvoice()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidOrderAsync();
        var original = (await book.IssueAsync(sale.Id, Wrong)).Value;
        book.Gateway.CancelRefusal = "El SAT todavía no relaciona esta factura con la que la sustituye.";
        var replacement = (await book.SubstituteAsync(original.Id, Right)).Value;

        await book.RefundAsync(sale.Id, 50m);

        // The refund's credit note goes to the replacement, even while the old one waits for its cancellation: a note of the
        // old one would keep the SAT from cancelling it.
        Assert.Equal(1, (await book.IssuePendingCreditNotesAsync()).Value);
        var note = await book.Context.CreditNotes.AsNoTracking().SingleAsync();
        Assert.Equal(replacement.Id, note.OrderInvoiceId);
        Assert.Equal(replacement.Uuid, Assert.Single(book.Gateway.StampRequests[^1].Document.Related!.Uuids));
        Assert.Equal(0, (await book.IssuePendingCreditNotesAsync()).Value);

        // And the old one is still cancelled with motive 01.
        book.Gateway.CancelRefusal = null;
        Assert.Equal(1, (await book.CheckCancellationsAsync()).Value);
        Assert.Equal(OrderInvoiceStatus.Cancelling, (await book.InvoiceAsync(original.Id)).Status);
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
                Rfc,
                "ESCUELA KEMPER URGATE",
                "Comercio",
                "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await book.Context.SaveChangesAsync();
            var (cer, key) = TestCsd.Make();
            var uploaded = await new UploadInvoicingCertificateCommandHandler(
                    book.Context, fixture.TenantService, fixture.CurrentUser, book.Gateway, book.Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                    CancellationToken.None);
            Assert.True(uploaded.IsSuccess, uploaded.Error);
            return book;
        }

        public async Task<OrderDto> PaidOrderAsync()
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Karla",
                CustomerPhone = "5512345678",
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 2 }],
                Payments = [new CreateOrderPaymentDto { Amount = 200m, Method = OrderPaymentMethod.Card }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        public async Task RefundAsync(Guid orderId, decimal amount)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).Include(o => o.Items).SingleAsync(o => o.Id == orderId);
            order.RegisterRefund(amount, OrderPaymentMethod.Card, "Devolución de un café", Clock.UtcNow, "cajero", null, null, false, []);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async Task<OrderInvoice> InvoiceAsync(Guid invoiceId)
        {
            Context.ChangeTracker.Clear();
            return await Context.OrderInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        }

        public Task<OrderInvoicingDto> InvoicingAsync(Guid orderId)
            => new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> IssueAsync(Guid orderId, CfdiReceiverInput receiver)
            => new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(orderId, receiver, false), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> SubstituteAsync(Guid invoiceId, CfdiReceiverInput receiver, string? email = null)
            => new SubstituteOrderInvoiceCommandHandler(
                    this, Context, Fixture.CurrentUser, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new SubstituteOrderInvoiceCommand(invoiceId, receiver, false, email), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> RetryAsync(Guid invoiceId)
            => new RetryOrderInvoiceCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new RetryOrderInvoiceCommand(invoiceId), CancellationToken.None);

        public Task<Result<IssuedCfdiDto>> ConfirmStampedAsync(Guid invoiceId, string uuid)
            => new ConfirmStuckCfdiStampedCommandHandler(
                    Context, Gateway, Storage, Fixture.CurrentUser, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new ConfirmStuckCfdiStampedCommand(IssuedCfdiKind.OrderInvoice, invoiceId, uuid), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> CancelReplacedAsync(Guid invoiceId)
            => new CancelReplacedOrderInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelReplacedOrderInvoiceCommand(invoiceId), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> CancelAsync(Guid invoiceId, CfdiCancellationMotive motive)
            => new CancelOrderInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelOrderInvoiceCommand(invoiceId, motive), CancellationToken.None);

        public Task<Result<int>> StampPendingAsync()
            => new StampPendingInvoicesCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new StampPendingInvoicesCommand(), CancellationToken.None);

        public Task<Result<int>> IssuePendingCreditNotesAsync()
            => new IssuePendingCreditNotesCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingCreditNotesCommand(), CancellationToken.None);

        public Task<Result<int>> CheckCancellationsAsync()
            => new CheckInvoiceCancellationsCommandHandler(Context, Gateway, Storage, Clock, NullLogger<CheckInvoiceCancellationsCommandHandler>.Instance)
                .Handle(new CheckInvoiceCancellationsCommand(), CancellationToken.None);

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The requests of the invoices go to their own handlers, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var lockOf = new InvoicingLock(Context);
            object result = request switch
            {
                RequestOrderInvoiceCommand command => await new RequestOrderInvoiceCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, lockOf, new FolioGenerator(Context), new PhoneNormalizationService(), Clock)
                    .Handle(command, cancellationToken),
                RequestOrderInvoiceSubstitutionCommand command => await new RequestOrderInvoiceSubstitutionCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, lockOf, new FolioGenerator(Context), new PhoneNormalizationService(), Clock)
                    .Handle(command, cancellationToken),
                RequestAutomaticCreditNoteCommand command => await new RequestAutomaticCreditNoteCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, lockOf, new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return (TResponse)result;
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
