using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The deposit to accept a quote from its link (Fase 31N, ADR 0094): the business asks a share of what is left to pay, fixed
/// on each version when it is sent; the customer pays it in Mercado Pago, into the business's account, and the payment accepts
/// the version and makes it its service order with the deposit as its «En línea» payment. A payment that can no longer accept
/// it goes back.
/// </summary>
public class QuoteDepositFlowTests
{
    private static readonly QuoteLineRequest Job = new() { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1000m };

    [Fact]
    public async Task Settings_ShouldAskADeposit_OnlyWithAnAccountThatCollects_AndTheAnswersOnTheLink()
    {
        await using var desk = await Desk.OpenAsync(percent: null, connect: false);

        Assert.False((await desk.SettingsAsync()).DepositAvailable);
        Assert.Equal(QuoteDeposits.UnavailableMessage, (await desk.UpdateSettingsAsync(depositPercent: 50)).Error);

        // A test account takes no real money: not with the platform in its real mode.
        await desk.ConnectAsync(liveMode: false);
        Assert.False((await desk.SettingsAsync()).DepositAvailable);
        Assert.Equal(QuoteDeposits.UnavailableMessage, (await desk.UpdateSettingsAsync(depositPercent: 50)).Error);
        desk.Context.OnlinePaymentAccounts.RemoveRange(desk.Context.OnlinePaymentAccounts);
        await desk.Context.SaveChangesAsync();

        // Paying online in the menu is its own switch: the account connected is enough.
        await desk.ConnectAsync();
        Assert.True((await desk.SettingsAsync()).DepositAvailable);
        var asked = await desk.UpdateSettingsAsync(depositPercent: 50);
        Assert.True(asked.IsSuccess, asked.Error);
        Assert.Equal(50, asked.Value!.DepositPercent);
        Assert.Contains(await desk.Context.AuditLogs.Select(a => a.Metadata).ToListAsync(), m => m!.Contains("\"DepositPercent\":50"));

        // While the account cannot collect for a while, the share already asked stays and the rest still saves; a new one does not.
        desk.Gateway.IsConfigured = false;
        var saved = await desk.Shop.Handler.Handle(new UpdateQuoteSettingsCommand(20, null, null), CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal((20, 50, false), (saved.Value!.ValidityDays, saved.Value.DepositPercent, saved.Value.DepositAvailable));
        Assert.Equal(QuoteDeposits.UnavailableMessage, (await desk.UpdateSettingsAsync(depositPercent: 30)).Error);
        desk.Gateway.IsConfigured = true;

        // Paid from the link: the answers there stay on while it asks one, and both go off together.
        Assert.Equal(QuoteDeposits.NeedsOnlineRepliesMessage, (await desk.UpdateSettingsAsync(onlineReplies: false)).Error);
        var off = await desk.UpdateSettingsAsync(onlineReplies: false, depositPercent: 0);
        Assert.True(off.IsSuccess, off.Error);
        Assert.Null(off.Value!.DepositPercent);
        Assert.False(off.Value.OnlineReplies);

        // A share outside 10 to 100 is refused.
        var validator = new UpdateQuoteSettingsCommandValidator();
        Assert.False(validator.Validate(new UpdateQuoteSettingsCommand(15, true, 5)).IsValid);
        Assert.False(validator.Validate(new UpdateQuoteSettingsCommand(15, true, 101)).IsValid);
        Assert.True(validator.Validate(new UpdateQuoteSettingsCommand(15, true, 0)).IsValid);
        Assert.True(validator.Validate(new UpdateQuoteSettingsCommand(15, true, null)).IsValid);
    }

    [Fact]
    public async Task Settings_ShouldNotOfferTheDeposit_WithoutServiceOrders_NorLetMercadoPagoGoWhileItIsAsked()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);

        // The account goes, or another one comes, only once no quote is sent asking one.
        Assert.Equal(OnlinePaymentSettings.QuoteDepositFirstMessage, (await desk.DisconnectAsync()).Error);
        Assert.Equal(OnlinePaymentSettings.QuoteDepositBeforeChangeMessage, (await desk.ConnectAnotherAccountAsync()).Error);
        Assert.Equal(FakeOnlinePaymentGateway.AccountId, (await desk.Context.OnlinePaymentAccounts.AsNoTracking().SingleAsync()).ProviderUserId);
        Assert.True((await desk.UpdateSettingsAsync(depositPercent: 0)).IsSuccess);
        Assert.True((await desk.DisconnectAsync()).IsSuccess);

        // A business with the agenda but no service orders quotes, but its paid quote would have no order to become.
        await using var dentist = await Desk.OpenAsync(percent: null, capabilities: TenantCapability.Retail | TenantCapability.Appointments);
        Assert.False((await dentist.SettingsAsync()).DepositAvailable);
        Assert.Equal(QuoteDeposits.UnavailableMessage, (await dentist.UpdateSettingsAsync(depositPercent: 30)).Error);
    }

    [Fact]
    public async Task Send_ShouldFixTheDepositOnTheVersion_AsTheShareOfWhatIsLeftToPay()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);

        var sent = await desk.Shop.SentAsync(Job);
        Assert.Equal(300m, sent.DepositAmount);
        Assert.False(sent.DepositPaid);
        var pdf = QuotePdfs.Model(await desk.Context.Quotes.AsNoTracking().Include(q => q.Lines).SingleAsync(q => q.Id == sent.Id),
            "Plomería", null, null, DateOnly.FromDateTime(SalesTestFixture.Now.UtcDateTime), null);
        Assert.Equal(((decimal?)300m, false), (pdf.DepositAmount, pdf.DepositPaid));

        // A share changed later leaves the versions already sent as the customer has them.
        Assert.True((await desk.UpdateSettingsAsync(depositPercent: 50)).IsSuccess);
        Assert.Equal(300m, (await desk.Shop.GetAsync(sent.Id)).DepositAmount);
        // Under $10 it asks none.
        Assert.Null((await desk.Shop.SentAsync(new QuoteLineRequest { Description = "Ajuste de llave", Quantity = 1m, UnitPrice = 18m })).DepositAmount);
    }

    [Fact]
    public async Task Send_ShouldTakeTheShareOfWhatIsLeft_AfterThePaidVisit()
    {
        await using var desk = await Desk.OpenAsync(percent: 50, creditedVisits: true);

        // 1,000 plus IVA is 1,160; the 440.80 paid for the visit comes off: 719.20 left, half of it 359.60.
        var (_, quote) = await desk.Shop.CreditedVisitQuoteAsync(accept: false);

        Assert.Equal(719.20m, quote.ToPay);
        Assert.Equal(359.60m, quote.DepositAmount);
    }

    [Fact]
    public async Task Send_ShouldSayWhenTheDepositCannotBeCollected_InsteadOfSendingWithoutIt()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        desk.Gateway.IsConfigured = false;

        var created = await desk.Shop.SaveDraftAsync(Job);
        var refused = await desk.Shop.Handler.Handle(new SendQuoteCommand(created.Value!.Id), CancellationToken.None);

        Assert.Equal(QuoteDeposits.UnavailableAtSendMessage, refused.Error);
        Assert.Equal(QuoteStatus.Draft, (await desk.Shop.GetAsync(created.Value.Id)).Status);

        // Nor without the answers on the link, where it is paid (a version before the Fase 31N turned them off and kept it).
        desk.Gateway.IsConfigured = true;
        var settings = await desk.Context.TenantSettings.SingleAsync(s => s.TenantId == desk.Shop.Tenant.Id);
        settings.SetQuoteOnlineReplies(false);
        await desk.Context.SaveChangesAsync();
        desk.Context.ChangeTracker.Clear();
        refused = await desk.Shop.Handler.Handle(new SendQuoteCommand(created.Value.Id), CancellationToken.None);
        Assert.Equal(QuoteDeposits.UnavailableAtSendMessage, refused.Error);
        Assert.Equal(QuoteStatus.Draft, (await desk.Shop.GetAsync(created.Value.Id)).Status);
    }

    [Fact]
    public async Task TheLink_ShouldAskToPayTheDeposit_AndRefuseAcceptingWithoutPayingIt()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var sent = await desk.Shop.SentAsync(Job);
        var token = sent.TrackingToken!;

        var view = await desk.PublicAsync(token);
        Assert.Equal((300m, true, true, false), (view!.DepositAmount, view.CanPayDeposit, view.CanReply, view.DepositPaid));

        Assert.Equal(QuoteRules.DepositRequiredMessage, (await desk.ReplyAsync(token, accepted: true))!.Error);
        // When the business cannot collect it now, the customer still sees it and can reject it.
        desk.Gateway.IsConfigured = false;
        Assert.False((await desk.PublicAsync(token))!.CanPayDeposit);
        var rejected = await desk.ReplyAsync(token, accepted: false);
        Assert.Equal(QuoteStatus.Rejected, rejected!.Value!.Status);
    }

    [Fact]
    public async Task StartingTheDeposit_ShouldSendTheCustomerToMercadoPago_ForTheVersionsDeposit_AndReuseThePaymentInCourse()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var sent = await desk.Shop.SentAsync(Job);

        var started = await desk.StartAsync(sent.TrackingToken!);

        Assert.True(started!.IsSuccess, started.Error);
        Assert.Equal((300m, SalesTestFixture.Now + OnlineCheckout.TimeToPay), (started.Value!.Amount, started.Value.ExpiresAt));
        var page = Assert.Single(desk.Gateway.Checkouts);
        Assert.Equal((started.Value.CheckoutId, 300m, "Anticipo de la cotización COT-000001"), (page.CheckoutId, page.Amount, page.Title));
        Assert.Equal($"https://tienditapp.test/cotizacion/pago/{started.Value.CheckoutId}", page.ReturnUrl);
        var checkout = await desk.Context.OnlineCheckouts.SingleAsync();
        Assert.Equal((sent.Id, (string?)null, (string?)null), (checkout.QuoteId, checkout.Request, checkout.CustomerPhone));

        // A double tap or coming back without paying goes to the same payment, while it has time.
        Assert.Equal(started.Value.CheckoutId, (await desk.StartAsync(sent.TrackingToken!))!.Value!.CheckoutId);
        Assert.Single(desk.Gateway.Checkouts);
        desk.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(26);
        Assert.NotEqual(started.Value.CheckoutId, (await desk.StartAsync(sent.TrackingToken!))!.Value!.CheckoutId);
        Assert.Equal(2, desk.Gateway.Checkouts.Count);
    }

    [Fact]
    public async Task StartingTheDeposit_ShouldBeRefused_ForAVersionWithoutOne_NoLongerAnswerable_OrThatTheBusinessCannotCollect()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var asks = await desk.Shop.SentAsync(Job);

        Assert.Null(await desk.StartAsync(new string('x', 43)));
        desk.Gateway.IsConfigured = false;
        Assert.Equal(QuoteDeposits.NotOfferedMessage, (await desk.StartAsync(asks.TrackingToken!))!.Error);
        desk.Gateway.IsConfigured = true;

        // The business turned the answers on the link off since it was sent.
        Assert.True((await desk.UpdateSettingsAsync(onlineReplies: false, depositPercent: 0)).IsSuccess);
        Assert.Equal(QuoteRules.OnlineRepliesOffMessage, (await desk.StartAsync(asks.TrackingToken!))!.Error);
        Assert.True((await desk.UpdateSettingsAsync(onlineReplies: true)).IsSuccess);
        var none = await desk.Shop.SentAsync(Job);
        Assert.Equal(QuoteRules.NoDepositMessage, (await desk.StartAsync(none.TrackingToken!))!.Error);

        // Answered by the business: not anymore.
        var answered = await desk.Shop.Handler.Handle(new RecordQuoteReplyCommand(asks.Id, false, QuoteReplyChannel.Phone, null), CancellationToken.None);
        Assert.True(answered.IsSuccess, answered.Error);
        Assert.Equal(QuoteDeposits.NotAnswerableMessage, (await desk.StartAsync(asks.TrackingToken!))!.Error);
        Assert.Empty(desk.Gateway.Checkouts);
    }

    [Fact]
    public async Task AnApprovedDeposit_ShouldAcceptTheQuote_AndMakeItsServiceOrder_WithTheDepositAsItsOnlinePayment()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var sent = await desk.Shop.SentAsync(Job);
        var started = (await desk.StartAsync(sent.TrackingToken!))!.Value!;
        var payment = desk.Gateway.Approve(started.CheckoutId, 300m, SalesTestFixture.Now.AddMinutes(5));
        desk.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(6);

        var status = await desk.ConfirmAsync(sent.TrackingToken!, started.CheckoutId, payment.Id);

        Assert.Equal(OnlineCheckoutStatus.Placed, status!.Status);
        var quote = await desk.Shop.GetAsync(sent.Id);
        Assert.Equal((QuoteStatus.Accepted, (QuoteReplyChannel?)QuoteReplyChannel.Link, true), (quote.Status, quote.ReplyChannel, quote.DepositPaid));
        Assert.Equal(quote.Order!.OrderNumber, status.OrderNumber);
        var order = await desk.Context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == quote.Order.Id);
        Assert.Equal((OrderType.Service, 1000m, 300m), (order.OrderType, order.Total, order.PaidAmount));
        var paid = Assert.Single(order.Payments);
        Assert.Equal((OrderPaymentMethod.Online, 300m, payment.Id), (paid.Method, paid.Amount, paid.Reference));
        var actions = await desk.Context.AuditLogs.Where(a => a.EntityId == sent.Id.ToString()).Select(a => new { a.Action, a.PerformedBy }).ToListAsync();
        Assert.Contains(actions, a => a.Action == QuoteAudit.Accepted && a.PerformedBy == QuoteAudit.CustomerFromLink);
        Assert.Contains(actions, a => a.Action == QuoteAudit.Converted && a.PerformedBy == QuoteAudit.CustomerFromLink);

        // Its PDF says it was paid.
        var pdf = await QuotePdfs.ModelAsync(desk.Context, await desk.Context.Quotes.AsNoTracking().Include(q => q.Lines).SingleAsync(q => q.Id == sent.Id),
            desk.Clock.UtcNow, CancellationToken.None);
        Assert.Equal(((decimal?)300m, true), (pdf.DepositAmount, pdf.DepositPaid));
        // The link says it was paid, and applying the same payment again changes nothing.
        var view = await desk.PublicAsync(sent.TrackingToken!);
        Assert.Equal((QuoteStatus.Accepted, true, false, false), (view!.Status, view.DepositPaid, view.CanReply, view.CanPayDeposit));
        Assert.Equal(OnlinePaymentApplication.AlreadyApplied, await desk.ApplyAsync(started.CheckoutId, payment));
        Assert.Single(await desk.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task ADeposit_ShouldBeAppliedByMercadoPagosNotification_OrTheSweep_WithoutTheCustomerComingBack()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var notified = await desk.Shop.SentAsync(Job);
        var swept = await desk.Shop.SentAsync(Job);
        var first = (await desk.StartAsync(notified.TrackingToken!))!.Value!;
        var second = (await desk.StartAsync(swept.TrackingToken!))!.Value!;
        var firstPayment = desk.Gateway.Approve(first.CheckoutId, 300m, SalesTestFixture.Now.AddMinutes(1));
        desk.Gateway.Approve(second.CheckoutId, 300m, SalesTestFixture.Now.AddMinutes(1));

        Assert.Equal(OnlinePaymentApplication.Placed, (await desk.NotifyAsync(firstPayment.Id)).Value);
        desk.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(3);
        Assert.Equal(1, (await desk.SweepAsync()).Value!.Checked);

        Assert.All(new[] { notified.Id, swept.Id }.Select(id => desk.Context.Quotes.AsNoTracking().Single(q => q.Id == id)),
            quote => Assert.Equal((QuoteStatus.Accepted, true), (quote.Status, quote.OrderId is not null)));
        Assert.Equal(2, await desk.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task ADepositWithThePaidVisit_ShouldPayTheOrderWithBoth()
    {
        await using var desk = await Desk.OpenAsync(percent: 50, creditedVisits: true);
        var (_, sent) = await desk.Shop.CreditedVisitQuoteAsync(accept: false);
        var started = (await desk.StartAsync(sent.TrackingToken!))!.Value!;
        var payment = desk.Gateway.Approve(started.CheckoutId, 359.60m, SalesTestFixture.Now.AddMinutes(5));

        Assert.Equal(OnlineCheckoutStatus.Placed, (await desk.ConfirmAsync(sent.TrackingToken!, started.CheckoutId, payment.Id))!.Status);

        var quote = await desk.Shop.GetAsync(sent.Id);
        var order = await desk.Context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == quote.Order!.Id);
        Assert.Equal((1160m, 800.40m), (order.Total, order.PaidAmount));
        Assert.Equal(
            [(OrderPaymentMethod.CustomerBalance, 440.80m), (OrderPaymentMethod.Online, 359.60m)],
            order.Payments.OrderBy(p => p.Method == OrderPaymentMethod.Online).Select(p => (p.Method, p.Amount)));
    }

    [Fact]
    public async Task ADepositStartedOnTheLastValidDay_ShouldCount_EvenIfTheValidityEndedWhileTheCustomerPaid()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var sent = await desk.Shop.SentAsync(Job);
        // Valid until October 19: at 23:50 of that day in Mexico City it is still valid.
        desk.Clock.UtcNow = new DateTimeOffset(2026, 10, 20, 5, 50, 0, TimeSpan.Zero);
        var started = (await desk.StartAsync(sent.TrackingToken!))!.Value!;
        var payment = desk.Gateway.Approve(started.CheckoutId, 300m, desk.Clock.UtcNow.AddMinutes(20));
        desk.Clock.UtcNow = desk.Clock.UtcNow.AddMinutes(21);

        Assert.Equal(OnlineCheckoutStatus.Placed, (await desk.ConfirmAsync(sent.TrackingToken!, started.CheckoutId, payment.Id))!.Status);
        Assert.Equal(QuoteStatus.Accepted, (await desk.Shop.GetAsync(sent.Id)).Status);
    }

    [Fact]
    public async Task ADepositPaidLate_ForAnotherAmount_OrForAVersionTheBusinessAnswered_ShouldGoBack_LeavingTheQuoteAsItWas()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var late = await desk.Shop.SentAsync(Job);
        var lateCheckout = (await desk.StartAsync(late.TrackingToken!))!.Value!;
        var latePayment = desk.Gateway.Approve(lateCheckout.CheckoutId, 300m, lateCheckout.ExpiresAt.AddMinutes(1));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, await desk.ApplyAsync(lateCheckout.CheckoutId, latePayment));

        var other = await desk.Shop.SentAsync(Job);
        var otherCheckout = (await desk.StartAsync(other.TrackingToken!))!.Value!;
        var otherPayment = desk.Gateway.Approve(otherCheckout.CheckoutId, 299m, SalesTestFixture.Now.AddMinutes(2));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, await desk.ApplyAsync(otherCheckout.CheckoutId, otherPayment));

        // The business recorded an answer while the customer paid.
        var answered = await desk.Shop.SentAsync(Job);
        var answeredCheckout = (await desk.StartAsync(answered.TrackingToken!))!.Value!;
        Assert.True((await desk.Shop.Handler.Handle(new RecordQuoteReplyCommand(answered.Id, true, QuoteReplyChannel.WhatsApp, null), CancellationToken.None)).IsSuccess);
        desk.Context.ChangeTracker.Clear();
        var answeredPayment = desk.Gateway.Approve(answeredCheckout.CheckoutId, 300m, SalesTestFixture.Now.AddMinutes(3));
        var status = await desk.ConfirmAsync(answered.TrackingToken!, answeredCheckout.CheckoutId, answeredPayment.Id);

        Assert.Equal((OnlineCheckoutStatus.NotPlaced, QuoteRules.AnsweredAlreadyMessage), (status!.Status, status.FailureMessage));
        Assert.Equal(OnlinePaymentRefundStatus.Requested, status.RefundStatus);
        // Each goes back whole, for why it was not taken.
        var refunds = await desk.Context.OnlinePaymentRefunds.ToListAsync();
        Assert.Equal(
            new[] { latePayment.Id, otherPayment.Id, answeredPayment.Id }.Order(),
            refunds.Select(r => r.PaymentId).Order());
        Assert.All(refunds, r => Assert.Equal(OnlinePaymentRefundReason.NotPlaced, r.Reason));
        Assert.Equal([300m, 299m, 300m], new[] { latePayment.Id, otherPayment.Id, answeredPayment.Id }
            .Select(id => refunds.Single(r => r.PaymentId == id).Amount));
        Assert.Equal(
            [OnlineCheckoutFailure.PaidAfterExpiry, OnlineCheckoutFailure.PaymentMismatch, OnlineCheckoutFailure.OrderRefused],
            new[] { lateCheckout.CheckoutId, otherCheckout.CheckoutId, answeredCheckout.CheckoutId }
                .Select(id => desk.Context.OnlineCheckouts.AsNoTracking().Single(c => c.Id == id).Failure));
        Assert.Equal(
            [(QuoteStatus.Sent, (QuoteReplyChannel?)null), (QuoteStatus.Sent, null), (QuoteStatus.Accepted, QuoteReplyChannel.WhatsApp)],
            new[] { late.Id, other.Id, answered.Id }.Select(id => desk.Context.Quotes.AsNoTracking().Single(q => q.Id == id))
                .Select(q => (q.Status, q.ReplyChannel)));
        Assert.Empty(await desk.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task ADepositOfAVersionReplacedWhilePaying_ShouldGoBack_AndOneBeingCorrected_ShouldDropTheCorrection()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);

        // The business sent a corrected version while the customer paid the first one: that payment goes back.
        var replaced = await desk.Shop.SentAsync(Job);
        var replacedCheckout = (await desk.StartAsync(replaced.TrackingToken!))!.Value!;
        var correction = (await desk.Shop.Handler.Handle(new ReviseQuoteCommand(replaced.Id), CancellationToken.None)).Value!;
        Assert.True((await desk.Shop.Handler.Handle(new SendQuoteCommand(correction.Id), CancellationToken.None)).IsSuccess);
        desk.Context.ChangeTracker.Clear();
        var replacedPayment = desk.Gateway.Approve(replacedCheckout.CheckoutId, 300m, SalesTestFixture.Now.AddMinutes(2));
        var status = await desk.ConfirmAsync(replaced.TrackingToken!, replacedCheckout.CheckoutId, replacedPayment.Id);
        Assert.Equal((OnlineCheckoutStatus.NotPlaced, QuoteRules.ReplacedMessage), (status!.Status, status.FailureMessage));
        Assert.Equal(QuoteStatus.Superseded, (await desk.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == replaced.Id)).Status);
        Assert.Equal(QuoteStatus.Sent, (await desk.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == correction.Id)).Status);
        Assert.Single(await desk.Context.OnlinePaymentRefunds.Where(r => r.PaymentId == replacedPayment.Id).ToListAsync());

        // A correction the business left as a draft goes: the customer accepted, and paid, the version they had.
        var corrected = await desk.Shop.SentAsync(Job);
        var checkout = (await desk.StartAsync(corrected.TrackingToken!))!.Value!;
        var draft = (await desk.Shop.Handler.Handle(new ReviseQuoteCommand(corrected.Id), CancellationToken.None)).Value!;
        desk.Context.ChangeTracker.Clear();
        var payment = desk.Gateway.Approve(checkout.CheckoutId, 300m, SalesTestFixture.Now.AddMinutes(3));
        Assert.Equal(OnlineCheckoutStatus.Placed, (await desk.ConfirmAsync(corrected.TrackingToken!, checkout.CheckoutId, payment.Id))!.Status);
        Assert.Equal(QuoteStatus.Accepted, (await desk.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == corrected.Id)).Status);
        Assert.Equal(QuoteStatus.Discarded, (await desk.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == draft.Id)).Status);
        Assert.Single(await desk.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task TwoPaymentsOfTheSameVersion_ShouldMakeOneOrder_AndTheOtherGoBack()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var sent = await desk.Shop.SentAsync(Job);
        var first = (await desk.StartAsync(sent.TrackingToken!))!.Value!;
        // With less than five minutes left to pay the first, another tab starts a second one.
        desk.Clock.UtcNow = first.ExpiresAt.AddMinutes(-4);
        var second = (await desk.StartAsync(sent.TrackingToken!))!.Value!;
        Assert.NotEqual(first.CheckoutId, second.CheckoutId);

        var firstPayment = desk.Gateway.Approve(first.CheckoutId, 300m, desk.Clock.UtcNow);
        var secondPayment = desk.Gateway.Approve(second.CheckoutId, 300m, desk.Clock.UtcNow.AddMinutes(1));
        Assert.Equal(OnlinePaymentApplication.Placed, await desk.ApplyAsync(first.CheckoutId, firstPayment));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, await desk.ApplyAsync(second.CheckoutId, secondPayment));

        var order = Assert.Single(await desk.Context.Orders.Include(o => o.Payments).ToListAsync());
        Assert.Equal(firstPayment.Id, Assert.Single(order.Payments).Reference);
        var refused = await desk.Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == second.CheckoutId);
        Assert.Equal((OnlineCheckoutFailure.OrderRefused, QuoteRules.AnsweredAlreadyMessage), (refused.Failure, refused.FailureMessage));
        var refund = await desk.Context.OnlinePaymentRefunds.SingleAsync();
        Assert.Equal((secondPayment.Id, 300m), (refund.PaymentId, refund.Amount));
    }

    [Fact]
    public async Task ADepositOfABusinessNoLongerActive_ShouldGoBack_LeavingTheQuoteAsItWas()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var sent = await desk.Shop.SentAsync(Job);
        var checkout = (await desk.StartAsync(sent.TrackingToken!))!.Value!;
        var tenant = await desk.Context.Tenants.SingleAsync(t => t.Id == desk.Shop.Tenant.Id);
        tenant.SetStatus(TenantStatus.Suspended);
        await desk.Context.SaveChangesAsync();
        desk.Context.ChangeTracker.Clear();

        var payment = desk.Gateway.Approve(checkout.CheckoutId, 300m, SalesTestFixture.Now.AddMinutes(2));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, await desk.ApplyAsync(checkout.CheckoutId, payment));

        var refused = await desk.Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == checkout.CheckoutId);
        Assert.Equal((OnlineCheckoutFailure.StoppedTakingOrders, QuoteDepositPlacement.BusinessInactiveMessage), (refused.Failure, refused.FailureMessage));
        Assert.Equal(payment.Id, (await desk.Context.OnlinePaymentRefunds.SingleAsync()).PaymentId);
        Assert.Equal(QuoteStatus.Sent, (await desk.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == sent.Id)).Status);
        Assert.Empty(await desk.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task ADepositWhoseQuoteCannotBecomeItsOrder_ShouldGoBack_WithoutLeavingTheQuoteAccepted()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var sent = await desk.Shop.SentAsync(Job);
        var started = (await desk.StartAsync(sent.TrackingToken!))!.Value!;
        // The business stopped having service orders while the customer paid.
        (await desk.Context.TenantSettings.SingleAsync(s => s.TenantId == desk.Shop.Tenant.Id)).UpdateCapabilities(TenantCapability.Retail | TenantCapability.Appointments);
        await desk.Context.SaveChangesAsync();
        var payment = desk.Gateway.Approve(started.CheckoutId, 300m, SalesTestFixture.Now.AddMinutes(2));

        var status = await desk.ConfirmAsync(sent.TrackingToken!, started.CheckoutId, payment.Id);

        Assert.Equal((OnlineCheckoutStatus.NotPlaced, QuoteRules.NoServiceOrdersMessage), (status!.Status, status.FailureMessage));
        var quote = await desk.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == sent.Id);
        Assert.Equal((QuoteStatus.Sent, (Guid?)null), (quote.Status, quote.OrderId));
        Assert.Empty(await desk.Context.Orders.ToListAsync());
        Assert.DoesNotContain(await desk.Context.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == QuoteAudit.Accepted);
        Assert.Equal(payment.Id, (await desk.Context.OnlinePaymentRefunds.SingleAsync()).PaymentId);
    }

    [Fact]
    public async Task AVersionAcceptedFromItsLinkWithoutAPaymentOfItsDeposit_ShouldNotSayItWasPaid()
    {
        // As a version asking one that the code before the Fase 31N accepted from its link (it knew no deposits), back on it.
        await using var desk = await Desk.OpenAsync(percent: null);
        var sent = await desk.Shop.SentAsync(Job);
        Assert.True((await desk.ReplyAsync(sent.TrackingToken!, accepted: true))!.IsSuccess);
        var quote = await desk.Context.Quotes.SingleAsync(q => q.Id == sent.Id);
        desk.Context.Entry(quote).Property(q => q.DepositAmount).CurrentValue = 300m;
        await desk.Context.SaveChangesAsync();
        desk.Context.ChangeTracker.Clear();

        var detail = await desk.Shop.GetAsync(sent.Id);
        Assert.Equal((QuoteStatus.Accepted, (decimal?)300m, false), (detail.Status, detail.DepositAmount, detail.DepositPaid));
        Assert.False((await desk.PublicAsync(sent.TrackingToken!))!.DepositPaid);
        var pdf = await QuotePdfs.ModelAsync(desk.Context, await desk.Context.Quotes.AsNoTracking().Include(q => q.Lines).SingleAsync(q => q.Id == sent.Id),
            desk.Clock.UtcNow, CancellationToken.None);
        Assert.False(pdf.DepositPaid);
    }

    [Fact]
    public async Task TheReturnPage_ShouldOnlyAnswer_WithTheLinkOfTheVersionItPaid()
    {
        await using var desk = await Desk.OpenAsync(percent: 30);
        var mine = await desk.Shop.SentAsync(Job);
        var another = await desk.Shop.SentAsync(Job);
        var started = (await desk.StartAsync(mine.TrackingToken!))!.Value!;

        Assert.Null(await desk.ConfirmAsync(another.TrackingToken!, started.CheckoutId, null));
        Assert.Null(await desk.ConfirmAsync(new string('x', 43), started.CheckoutId, null));
        Assert.Null(await desk.ConfirmAsync(mine.TrackingToken!, Guid.NewGuid(), null));
        var waiting = await desk.ConfirmAsync(mine.TrackingToken!, started.CheckoutId, null);
        Assert.Equal((OnlineCheckoutStatus.AwaitingPayment, started.CheckoutUrl), (waiting!.Status, waiting.CheckoutUrl));

        // The menu's return, with the business's address and the payment's id (in the return address), says nothing of it,
        // nor asks Mercado Pago about it.
        var asked = desk.Gateway.AccessTokensUsed.Count;
        Assert.Null(await desk.MenuConfirmAsync(started.CheckoutId));
        Assert.Equal(asked, desk.Gateway.AccessTokensUsed.Count);

        // Once its time to pay ended, by the server's clock, it no longer offers Mercado Pago's page.
        desk.Clock.UtcNow = started.ExpiresAt.AddSeconds(1);
        var late = await desk.ConfirmAsync(mine.TrackingToken!, started.CheckoutId, null);
        Assert.Null(late!.CheckoutUrl);
    }

    /// <summary>A trade with service orders, its Mercado Pago account connected (the menu's switch off) and its share asked.</summary>
    private sealed class Desk : IAsyncDisposable, ISender
    {
        private Desk(QuoteConversionFlowTests.Shop shop) => Shop = shop;

        public QuoteConversionFlowTests.Shop Shop { get; }
        public FakeOnlinePaymentGateway Gateway { get; } = new();
        public FakeSecretProtector Protector { get; } = new();
        public SalesDbContext Context => Shop.Context;
        public SalesClock Clock => Shop.Fixture.Clock;

        public static async Task<Desk> OpenAsync(
            int? percent, bool connect = true, TenantCapability capabilities = TenantCapability.Service | TenantCapability.Appointments,
            bool creditedVisits = false)
        {
            // A visit «Se descuenta si contratas» is priced plus IVA, as CreditedVisitQuoteAsync charges it.
            var desk = new Desk(creditedVisits
                ? await QuoteConversionFlowTests.Shop.OpenAsync(capabilities, pricesIncludeTax: false, HomeVisitChargePolicy.CreditedIfHired)
                : await QuoteConversionFlowTests.Shop.OpenAsync(capabilities));
            desk.Shop.Gateway = desk.Gateway;
            if (connect)
                await desk.ConnectAsync();
            if (percent is not null)
                Assert.True((await desk.UpdateSettingsAsync(depositPercent: percent)).IsSuccess);
            return desk;
        }

        public async Task ConnectAsync(bool liveMode = true)
        {
            var tenantId = Shop.Tenant.Id;
            var account = OnlinePaymentAccount.Connect(
                tenantId,
                FakeOnlinePaymentGateway.AccountId,
                OnlinePaymentKeys.ProtectAccess(Protector, tenantId, FakeOnlinePaymentGateway.AccessToken),
                OnlinePaymentKeys.ProtectRefresh(Protector, tenantId, FakeOnlinePaymentGateway.RefreshToken),
                SalesTestFixture.Now.AddDays(180),
                liveMode,
                Guid.NewGuid(),
                SalesTestFixture.Now);
            Context.OnlinePaymentAccounts.Add(account);
            await Context.SaveChangesAsync();
        }

        public Task<QuoteSettingsDto> SettingsAsync() => Shop.Handler.Handle(new GetQuoteSettingsQuery(), CancellationToken.None);

        public Task<Result<QuoteSettingsDto>> UpdateSettingsAsync(bool? onlineReplies = null, int? depositPercent = null)
            => Shop.Handler.Handle(new UpdateQuoteSettingsCommand(15, onlineReplies, depositPercent), CancellationToken.None);

        public Task<Result<OnlinePaymentSettingsDto>> DisconnectAsync()
            => new DisconnectOnlinePaymentAccountCommandHandler(Context, Shop.Fixture.TenantService, Shop.Fixture.CurrentUser, Gateway, Protector, Clock)
                .Handle(new DisconnectOnlinePaymentAccountCommand(), CancellationToken.None);

        public Task<PublicQuoteDto?> PublicAsync(string token)
            => new GetPublicQuoteQueryHandler(Context, Shop.Fixture.TenantService, Clock, Gateway)
                .Handle(new GetPublicQuoteQuery(token), CancellationToken.None);

        public Task<Result<PublicQuoteDto>?> ReplyAsync(string token, bool accepted)
            => new ReplyToQuoteFromLinkCommandHandler(Context, Shop.Fixture.TenantService, Clock, Gateway)
                .Handle(new ReplyToQuoteFromLinkCommand(token, accepted, null), CancellationToken.None);

        public Task<Result<QuoteDepositStartedDto>?> StartAsync(string token)
            => new StartQuoteDepositCommandHandler(
                    Context, Shop.Fixture.TenantService, Clock, Gateway, Protector, new Links(), NullLogger<StartQuoteDepositCommandHandler>.Instance)
                .Handle(new StartQuoteDepositCommand(token), CancellationToken.None);

        public Task<QuoteDepositStatusDto?> ConfirmAsync(string token, Guid checkoutId, string? paymentId)
            => new ConfirmQuoteDepositCommandHandler(
                    Context, Shop.Fixture.TenantService, Gateway, Protector, Clock, this, NullLogger<ConfirmQuoteDepositCommandHandler>.Instance)
                .Handle(new ConfirmQuoteDepositCommand(token, checkoutId, paymentId), CancellationToken.None);

        /// <summary>The owner authorizes another Mercado Pago account than the one connected.</summary>
        public async Task<Result<OnlinePaymentSettingsDto>> ConnectAnotherAccountAsync()
        {
            Gateway.Credentials = new OnlinePaymentCredentials(
                FakeOnlinePaymentGateway.AccountId + 1, "APP_USR-other", "TG-other", Clock.UtcNow.AddDays(180), LiveMode: true);
            var started = await new StartOnlinePaymentConnectionCommandHandler(Context, Shop.Fixture.TenantService, Shop.Fixture.CurrentUser, Gateway, Clock)
                .Handle(new StartOnlinePaymentConnectionCommand(), CancellationToken.None);
            var state = Guid.ParseExact(System.Web.HttpUtility.ParseQueryString(new Uri(started.Value!.AuthorizationUrl).Query)["state"]!, "N");
            return await new CompleteOnlinePaymentConnectionCommandHandler(Context, Shop.Fixture.TenantService, Shop.Fixture.CurrentUser, Gateway, Protector, Clock)
                .Handle(new CompleteOnlinePaymentConnectionCommand(state, "TG-other-code"), CancellationToken.None);
        }

        public Task<OnlineCheckoutStatusDto?> MenuConfirmAsync(Guid checkoutId)
            => new ConfirmOnlineCheckoutCommandHandler(
                    Context, Shop.Fixture.TenantService, Shop.Fixture.CurrentUser, Gateway, Protector, Clock, this,
                    NullLogger<ConfirmOnlineCheckoutCommandHandler>.Instance)
                .Handle(new ConfirmOnlineCheckoutCommand(Shop.Tenant.Slug, checkoutId), CancellationToken.None);

        public Task<Result<OnlinePaymentApplication>> NotifyAsync(string paymentId)
            => new ApplyOnlinePaymentNotificationCommandHandler(
                    Context, Gateway, Protector, Clock, this, NullLogger<ApplyOnlinePaymentNotificationCommandHandler>.Instance)
                .Handle(new ApplyOnlinePaymentNotificationCommand(paymentId), CancellationToken.None);

        public Task<Result<OnlinePaymentSweep>> SweepAsync()
            => new SweepOnlinePaymentsCommandHandler(Context, Gateway, Protector, Clock, this, NullLogger<SweepOnlinePaymentsCommandHandler>.Instance)
                .Handle(new SweepOnlinePaymentsCommand(), CancellationToken.None);

        /// <summary>The payment, as the notification or the sweep would apply it.</summary>
        public Task<OnlinePaymentApplication> ApplyAsync(Guid checkoutId, OnlinePaymentDetails payment)
            => new OnlinePaymentChecker(Context, Gateway, Protector, Clock, this, NullLogger.Instance).ApplyAsync(checkoutId, payment, CancellationToken.None);

        // The applications of payments go to their own handler, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ReconcileOnlinePaymentCommand reconcile)
                return (TResponse)(object)await new ReconcileOnlinePaymentCommandHandler(
                        Context, Gateway, Clock, NullLogger<ReconcileOnlinePaymentCommandHandler>.Instance)
                    .Handle(reconcile, cancellationToken);
            if (request is not ApplyOnlinePaymentCommand apply)
                throw new NotSupportedException(request.GetType().Name);
            var phones = new PhoneNormalizationService();
            var folios = new FolioGenerator(Context);
            var handler = new ApplyOnlinePaymentCommandHandler(
                Context, Gateway, Clock,
                new CustomerLookupService(Context, phones, NullLogger<CustomerLookupService>.Instance),
                new OrderInventoryService(Context, Clock, folios, NullLogger<OrderInventoryService>.Instance),
                folios, phones, new OrderCashService(Context, Shop.Fixture.CurrentUser),
                NullLogger<ApplyOnlinePaymentCommandHandler>.Instance);
            return (TResponse)(object)await handler.Handle(apply, cancellationToken);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync() => Shop.DisposeAsync();

        private sealed class Links : IAppLinks
        {
            public string? BaseUrl => "https://tienditapp.test";
        }
    }
}
