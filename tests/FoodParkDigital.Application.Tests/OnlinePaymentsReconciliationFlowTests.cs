using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// What happens in Mercado Pago reaches the order (Fase 24K, ADR 0044): a refund the business made from its account, or one
/// Mercado Pago made, becomes an «En línea» refund of the order once; TienditApp's own refunds are never counted twice; one
/// Mercado Pago refused that the business then made in its account is done; a chargeback or claim shows on the order and a
/// lost chargeback gives back what was left. The notification and the review of the account's payments both bring it.
/// </summary>
public partial class OnlinePaymentsFlowTests
{
    [Fact]
    public async Task RefundMadeInMercadoPago_ShouldBecomeAnOnlineRefundOfTheOrder_ByMercadoPago_Once()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop, quantity: 2);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(2);
        var refund = shop.Gateway.GiveBack(payment, 20m, SalesTestFixture.Now.AddDays(1));

        await shop.NotifyAsync(payment);
        await shop.NotifyAsync(payment);

        var order = await shop.Context.Orders.AsNoTracking().Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == orderId);
        var recorded = Assert.Single(order.Refunds);
        // Recorded when TienditApp learned of it, like every refund: a closed day is never rewritten.
        Assert.Equal((OrderPaymentMethod.Online, 20m, ReconcileOnlinePaymentCommandHandler.MadeByBusinessReason, "Mercado Pago", refund.Id, SalesTestFixture.Now.AddDays(2)),
            (recorded.Method, recorded.Amount, recorded.Reason, recorded.RefundedBy, recorded.Reference, recorded.RefundedAt));
        Assert.False(recorded.RestockItems);
        Assert.Equal(90m, order.OnlineRefundable);
        var made = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentRefundReason.InMercadoPago, OnlinePaymentRefundStatus.Completed, refund.Id, recorded.Id, payment),
            (made.Reason, made.Status, made.ProviderRefundId, made.OrderRefundId, made.PaymentId));
        // Already given back: nothing is asked of Mercado Pago, and no drawer moves.
        Assert.Empty(shop.Gateway.Refunds);
        Assert.Empty(await shop.Context.CashTransactions.ToListAsync());

        var shown = Assert.Single((await shop.OrderAsync(orderId)).Refunds);
        Assert.Equal((OnlinePaymentRefundStatus.Completed, OnlinePaymentRefundReason.InMercadoPago), (shown.OnlineStatus, shown.OnlineReason));
    }

    [Theory]
    [InlineData("admin", OnlinePaymentRefundReason.InMercadoPago, ReconcileOnlinePaymentCommandHandler.MadeByMercadoPagoReason)]
    [InlineData("bpp", OnlinePaymentRefundReason.Claim, ReconcileOnlinePaymentCommandHandler.ClaimReason)]
    [InlineData("collector", OnlinePaymentRefundReason.InMercadoPago, ReconcileOnlinePaymentCommandHandler.MadeByBusinessReason)]
    [InlineData(null, OnlinePaymentRefundReason.InMercadoPago, ReconcileOnlinePaymentCommandHandler.MadeByBusinessReason)]
    public async Task RefundMadeInMercadoPago_ShouldSayWhoMadeIt(string? source, OnlinePaymentRefundReason reason, string text)
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddHours(1), source: source!);

        await shop.NotifyAsync(payment);

        Assert.Equal(reason, (await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).Reason);
        Assert.Equal(text, (await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds.Single().Reason);
    }

    [Fact]
    public async Task RefundInMercadoPago_NotApprovedYet_ShouldWait()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddHours(1), status: "in_process");
        shop.Gateway.Payments[payment] = shop.Gateway.Payments[payment] with { RefundedAmount = 55m };

        await shop.NotifyAsync(payment);

        Assert.Empty(await shop.Context.OnlinePaymentRefunds.ToListAsync());
        Assert.Empty((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
    }

    [Fact]
    public async Task PaymentWithoutRefundsOrDisputes_ShouldNotAskForItsRefunds()
    {
        await using var shop = await Shop.OpenAsync();
        var (_, payment) = await PlaceAsync(shop);

        await shop.NotifyAsync(payment);

        Assert.Empty(shop.Gateway.RefundReads);
    }

    [Fact]
    public async Task RefundOfTheBusiness_ThatMercadoPagoListsBeforeAnswering_ShouldNotBeRecordedTwice()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop, quantity: 2);
        var asked = await shop.RefundAsync(orderId, 55m);
        Assert.True(asked.IsSuccess, asked.Error);
        // Mercado Pago already did it and notified, before the queue heard its answer.
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddMinutes(10), refundId: "refund-1");

        await shop.NotifyAsync(payment);
        Assert.Single(await shop.Context.OnlinePaymentRefunds.ToListAsync());

        var own = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        await shop.DispatchRefundAsync(own.Id);
        await shop.NotifyAsync(payment);

        var refunds = await shop.Context.OnlinePaymentRefunds.AsNoTracking().ToListAsync();
        Assert.Equal((OnlinePaymentRefundReason.ByBusiness, OnlinePaymentRefundStatus.Completed, "refund-1"),
            (Assert.Single(refunds).Reason, refunds[0].Status, refunds[0].ProviderRefundId));
        Assert.Single((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
    }

    [Fact]
    public async Task RefundOfTheBusiness_DoneWithoutItsId_ShouldTakeTheIdOfMercadoPagosRefund()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop, quantity: 2);
        shop.Gateway.RefundOutcome = new OnlinePaymentRefundOutcome.Done(null);
        await shop.RefundAsync(orderId, 55m);
        await shop.DispatchRefundAsync((await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).Id);
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddMinutes(10), refundId: "4455");

        await shop.NotifyAsync(payment);

        var own = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentRefundReason.ByBusiness, "4455"), (own.Reason, own.ProviderRefundId));
        Assert.Single((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
    }

    [Fact]
    public async Task RefundMercadoPagoRefused_ThatTheBusinessThenMadeInItsAccount_ShouldBeDone_WithoutASecondRefund()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop, quantity: 2);
        shop.Gateway.RefundOutcome = new OnlinePaymentRefundOutcome.Refused("Mercado Pago no hizo la devolución: saldo insuficiente");
        await shop.RefundAsync(orderId, 55m);
        await shop.DispatchRefundAsync((await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(1, (await shop.SettingsAsync()).RefundsRefused);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddDays(1), refundId: "4455");

        await shop.NotifyAsync(payment);

        var done = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentRefundStatus.DoneInMercadoPago, "4455", SalesTestFixture.Now.AddDays(1)), (done.Status, done.ProviderRefundId, done.SettledAt));
        Assert.Contains("saldo insuficiente", done.FailureMessage);
        Assert.Single((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
        Assert.Equal(0, (await shop.SettingsAsync()).RefundsRefused);
        Assert.Equal(OnlinePaymentRefundStatus.DoneInMercadoPago, Assert.Single((await shop.OrderAsync(orderId)).Refunds).OnlineStatus);
    }

    [Fact]
    public async Task AutomaticRefund_OfASecondPayment_RefusedThenMadeInTheAccount_ShouldBeDone_AndTouchNoOrder()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync()).Value;
        var first = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));
        var second = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(2));
        await shop.NotifyAsync(first.Id);
        await shop.NotifyAsync(second.Id);
        shop.Gateway.RefundOutcome = new OnlinePaymentRefundOutcome.Refused("Mercado Pago no hizo la devolución.");
        await shop.DispatchRefundAsync((await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).Id);
        shop.Gateway.GiveBack(second.Id, 55m, SalesTestFixture.Now.AddHours(1), refundId: "4466");

        await shop.NotifyAsync(second.Id);

        var refund = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentRefundReason.DuplicatePayment, OnlinePaymentRefundStatus.DoneInMercadoPago, "4466"),
            (refund.Reason, refund.Status, refund.ProviderRefundId));
        Assert.Empty((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync()).Refunds);
    }

    [Fact]
    public async Task RefundOfASecondPayment_MadeInMercadoPago_ShouldTouchNoOrder()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync()).Value;
        var first = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));
        var second = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(2));
        await shop.NotifyAsync(first.Id);
        await shop.NotifyAsync(second.Id);
        // The queue's automatic refund is still in course when the business refunds a different amount there.
        shop.Gateway.GiveBack(second.Id, 30m, SalesTestFixture.Now.AddHours(1));

        await shop.NotifyAsync(second.Id);

        Assert.Single(await shop.Context.OnlinePaymentRefunds.ToListAsync());
        Assert.Empty((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync()).Refunds);
    }

    [Fact]
    public async Task RefundMadeInMercadoPago_ForMoreThanWasLeft_ShouldBeRecordedUpToWhatWasLeft()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        await shop.RefundAsync(orderId, 50m);
        await shop.DispatchRefundAsync((await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).Id);
        shop.Gateway.GiveBack(payment, 50m, SalesTestFixture.Now.AddMinutes(10), refundId: "refund-1");
        // Mercado Pago gave back more than was left paid online.
        shop.Gateway.Payments[payment] = shop.Gateway.Payments[payment] with { Amount = 70m };
        shop.Gateway.GiveBack(payment, 20m, SalesTestFixture.Now.AddMinutes(20), refundId: "4477");
        shop.Gateway.Payments[payment] = shop.Gateway.Payments[payment] with { Amount = 55m };

        await shop.NotifyAsync(payment);

        var order = await shop.Context.Orders.AsNoTracking().Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == orderId);
        Assert.Equal([50m, 5m], order.Refunds.OrderBy(r => r.RefundedAt).ThenByDescending(r => r.Amount).Select(r => r.Amount));
        Assert.Equal(0m, order.OnlineRefundable);
        Assert.Equal(5m, (await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync(r => r.ProviderRefundId == "4477")).Amount);
    }

    [Fact]
    public async Task Chargeback_ShouldShowOnTheOrder_AndOnlyALostOneGivesBackWhatWasLeft_Once()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop, quantity: 2);
        shop.Gateway.GiveBack(payment, 20m, SalesTestFixture.Now.AddDays(1));
        await shop.NotifyAsync(payment);

        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(10);
        shop.Gateway.Dispute(payment, OnlinePaymentDetails.ChargedBackStatus, "in_process", SalesTestFixture.Now.AddDays(10));
        await shop.NotifyAsync(payment);

        var opened = await shop.OrderAsync(orderId);
        Assert.Equal((OnlinePaymentDispute.Open, SalesTestFixture.Now.AddDays(10)), (opened.OnlineDispute, opened.OnlineDisputeUpdatedAt));
        // While it is disputed Mercado Pago holds the money: nothing goes back yet.
        Assert.Single(opened.Refunds);
        Assert.Equal(1, (await shop.SettingsAsync()).DisputesOpen);

        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(40);
        shop.Gateway.Dispute(payment, OnlinePaymentDetails.ChargedBackStatus, "settled", SalesTestFixture.Now.AddDays(40));
        await shop.NotifyAsync(payment);
        await shop.NotifyAsync(payment);

        var lost = await shop.OrderAsync(orderId);
        Assert.Equal(OnlinePaymentDispute.Lost, lost.OnlineDispute);
        var chargeback = lost.Refunds.Single(r => r.OnlineReason == OnlinePaymentRefundReason.Chargeback);
        Assert.Equal((90m, ReconcileOnlinePaymentCommandHandler.ChargebackReason, "Mercado Pago", (string?)null),
            (chargeback.Amount, chargeback.Reason, chargeback.RefundedBy, chargeback.Reference));
        Assert.Equal(2, lost.Refunds.Count);
        Assert.Equal(0, (await shop.SettingsAsync()).DisputesOpen);

        // Mercado Pago covering it afterwards shows it, and the refund stays: the money is the business's to check there.
        shop.Gateway.Dispute(payment, OnlinePaymentDetails.ChargedBackStatus, "reimbursed", SalesTestFixture.Now.AddDays(60));
        await shop.NotifyAsync(payment);
        var covered = await shop.OrderAsync(orderId);
        Assert.Equal((OnlinePaymentDispute.Won, 2), (covered.OnlineDispute, covered.Refunds.Count));
    }

    [Fact]
    public async Task Chargeback_WonByTheBusiness_ShouldGiveNothingBack()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        shop.Gateway.Dispute(payment, OnlinePaymentDetails.ChargedBackStatus, "in_process", SalesTestFixture.Now.AddDays(3));
        await shop.NotifyAsync(payment);
        shop.Gateway.Dispute(payment, OnlinePaymentDetails.ChargedBackStatus, "reimbursed", SalesTestFixture.Now.AddDays(30));

        await shop.NotifyAsync(payment);

        var order = await shop.OrderAsync(orderId);
        Assert.Equal(OnlinePaymentDispute.Won, order.OnlineDispute);
        Assert.Empty(order.Refunds);
        Assert.Empty(await shop.Context.OnlinePaymentRefunds.ToListAsync());
    }

    [Fact]
    public async Task Claim_ThatEndsApprovedAgain_ShouldBeWon_AndOneRefundedByMercadoPago_Lost()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop, quantity: 2);
        shop.Gateway.Dispute(payment, OnlinePaymentDetails.InMediationStatus, "pending", SalesTestFixture.Now.AddDays(2));
        await shop.NotifyAsync(payment);
        Assert.Equal(OnlinePaymentDispute.Open, (await shop.OrderAsync(orderId)).OnlineDispute);

        shop.Gateway.Dispute(payment, OnlinePaymentDetails.ApprovedStatus, "accredited", SalesTestFixture.Now.AddDays(5));
        await shop.NotifyAsync(payment);
        Assert.Equal(OnlinePaymentDispute.Won, (await shop.OrderAsync(orderId)).OnlineDispute);

        // Another claim, which Mercado Pago ends giving the money back to the customer.
        shop.Gateway.Dispute(payment, OnlinePaymentDetails.InMediationStatus, "pending", SalesTestFixture.Now.AddDays(8));
        await shop.NotifyAsync(payment);
        shop.Gateway.GiveBack(payment, 110m, SalesTestFixture.Now.AddDays(12), source: "bpp");
        shop.Gateway.Payments[payment] = shop.Gateway.Payments[payment] with { StatusDetail = "bpp_refunded" };
        await shop.NotifyAsync(payment);

        var order = await shop.OrderAsync(orderId);
        Assert.Equal(OnlinePaymentDispute.Lost, order.OnlineDispute);
        var claim = Assert.Single(order.Refunds);
        Assert.Equal((110m, OnlinePaymentRefundReason.Claim), (claim.Amount, claim.OnlineReason));
        // The claim's refund is the money: no chargeback refund on top.
        Assert.DoesNotContain(await shop.Context.OnlinePaymentRefunds.ToListAsync(), r => r.Reason == OnlinePaymentRefundReason.Chargeback);
    }

    [Fact]
    public async Task PaymentOfAnotherAccount_ShouldReflectNothing()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddHours(1));
        shop.Gateway.Payments[payment] = shop.Gateway.Payments[payment] with { CollectorId = FakeOnlinePaymentGateway.AccountId + 7 };

        await shop.NotifyAsync(payment);

        Assert.Empty(await shop.Context.OnlinePaymentRefunds.ToListAsync());
        Assert.Empty((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
    }

    [Fact]
    public async Task RefundThatAnotherPathRecordedFirst_ShouldStayOnce()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        var refund = shop.Gateway.GiveBack(payment, 20m, SalesTestFixture.Now.AddHours(1));
        // The review saves the same refund just before the notification reflects it (the race itself, on SQL Server's
        // unique index, is in OnlinePaymentReconciliationSqlServerTests).
        var saved = false;
        shop.BeforeReconciling = async command =>
        {
            if (saved)
                return;
            saved = true;
            var order = await shop.Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == orderId);
            var orderRefund = order.RegisterRefund(20m, OrderPaymentMethod.Online, "Otra vía", shop.Clock.UtcNow, "Mercado Pago", null, refund.Id, false, []);
            shop.Context.OnlinePaymentRefunds.Add(Domain.OnlinePayments.Entities.OnlinePaymentRefund.MadeInMercadoPago(
                shop.Tenant.Id, orderId, orderRefund.Id, payment, 20m, refund.Id, OnlinePaymentRefundReason.InMercadoPago, shop.Clock.UtcNow));
            await shop.Context.SaveChangesAsync();
            shop.Context.ChangeTracker.Clear();
        };

        await shop.NotifyAsync(payment);

        Assert.Single(await shop.Context.OnlinePaymentRefunds.ToListAsync());
        Assert.Single((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
    }

    [Fact]
    public async Task Review_ShouldReflectWhatChanged_WithoutTheNotification_AndGoOnFromWhereItEnded()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        // A payment of the account that TienditApp did not charge comes too, and is left alone.
        shop.Gateway.Payments["9999"] = shop.Gateway.Payments[payment] with { Id = "9999", ExternalReference = "pedido-de-otra-tienda", LastChangedAt = SalesTestFixture.Now.AddHours(2) };
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddHours(3));
        shop.Clock.UtcNow = SalesTestFixture.Now.AddHours(4);

        var first = (await shop.ReviewAsync()).Value;

        Assert.Equal((2, 1, SalesTestFixture.Now.AddHours(4)), (first.Read, first.Reflected, first.ReviewedUntil));
        Assert.Single((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
        // The first review reads from the connection, with the overlap.
        Assert.Equal(SalesTestFixture.Now - ReviewOnlinePaymentsCommandHandler.Overlap, shop.Gateway.ChangeSearches[0].From);
        Assert.Equal(SalesTestFixture.Now.AddHours(4), (await shop.Context.OnlinePaymentAccounts.AsNoTracking().SingleAsync()).PaymentsReviewedUntil);
        Assert.Equal(SalesTestFixture.Now.AddHours(4), (await shop.SettingsAsync()).PaymentsReviewedUntil);

        // Not again before its time; then from the last review, with the overlap.
        shop.Clock.UtcNow = SalesTestFixture.Now.AddHours(4).AddMinutes(10);
        Assert.Null((await shop.ReviewAsync()).Value.ReviewedUntil);
        Assert.Single(shop.Gateway.ChangeSearches);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddHours(5);
        var second = (await shop.ReviewAsync()).Value;
        Assert.Equal(SalesTestFixture.Now.AddHours(4) - ReviewOnlinePaymentsCommandHandler.Overlap, shop.Gateway.ChangeSearches[1].From);
        Assert.Equal(0, second.Reflected);
        Assert.Single(await shop.Context.OnlinePaymentRefunds.ToListAsync());
    }

    [Fact]
    public async Task Review_ThatReadsAFullPage_ShouldGoOnFromTheLastChangeRead()
    {
        await using var shop = await Shop.OpenAsync();
        await PlaceAsync(shop);
        for (var i = 0; i < ReviewOnlinePaymentsCommandHandler.PageSize * ReviewOnlinePaymentsCommandHandler.MaxPagesPerReview + 3; i++)
            shop.Gateway.Payments[$"8{i:D5}"] = shop.Gateway.Payments.Values.First() with
            {
                Id = $"8{i:D5}",
                ExternalReference = $"otra-{i}",
                LastChangedAt = SalesTestFixture.Now.AddMinutes(i + 1)
            };
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);

        var review = (await shop.ReviewAsync()).Value;

        Assert.Equal(ReviewOnlinePaymentsCommandHandler.PageSize * ReviewOnlinePaymentsCommandHandler.MaxPagesPerReview, review.Read);
        Assert.Equal(ReviewOnlinePaymentsCommandHandler.MaxPagesPerReview, shop.Gateway.ChangeSearches.Count);
        // The order's payment changed first: the last read is the 199th of the others.
        Assert.True(review.ReviewedUntil < SalesTestFixture.Now.AddDays(1));
        Assert.Equal(review.ReviewedUntil, (await shop.Context.OnlinePaymentAccounts.AsNoTracking().SingleAsync()).PaymentsReviewedUntil);
    }

    [Fact]
    public async Task Review_WhenMercadoPagoDoesNotAnswer_OrRejectsTheKey_ShouldNotMove()
    {
        await using var shop = await Shop.OpenAsync();
        await PlaceAsync(shop);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddHours(1);

        shop.Gateway.Unavailable = true;
        Assert.Null((await shop.ReviewAsync()).Value.ReviewedUntil);
        Assert.Null((await shop.Context.OnlinePaymentAccounts.AsNoTracking().SingleAsync()).PaymentsReviewedUntil);

        shop.Gateway.Unavailable = false;
        shop.Gateway.RejectKey = true;
        Assert.Null((await shop.ReviewAsync()).Value.ReviewedUntil);
        var account = await shop.Context.OnlinePaymentAccounts.AsNoTracking().SingleAsync();
        Assert.Null(account.PaymentsReviewedUntil);
        Assert.False(account.IsUsable(shop.Clock.UtcNow));

        // Without a key that works nothing is asked.
        var searches = shop.Gateway.ChangeSearches.Count;
        shop.Gateway.RejectKey = false;
        Assert.Null((await shop.ReviewAsync()).Value.ReviewedUntil);
        Assert.Equal(searches, shop.Gateway.ChangeSearches.Count);
    }

    /// <summary>An order placed by an approved payment of <paramref name="quantity"/> lattes ($55 each).</summary>
    private static async Task<(Guid OrderId, string PaymentId)> PlaceAsync(Shop shop, int quantity = 1)
    {
        var started = (await shop.CheckoutAsync(quantity: quantity)).Value;
        var payment = shop.Gateway.Approve(started.CheckoutId, 55m * quantity, SalesTestFixture.Now.AddMinutes(1));
        Assert.Equal(OnlinePaymentApplication.Placed, (await shop.NotifyAsync(payment.Id)).Value);
        var orderId = await shop.Context.Orders.AsNoTracking().Select(o => o.Id).SingleAsync();
        shop.Context.ChangeTracker.Clear();
        return (orderId, payment.Id);
    }
}
