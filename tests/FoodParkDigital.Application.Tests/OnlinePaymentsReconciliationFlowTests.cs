using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.OnlinePayments.Entities;
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
    public async Task Refund_RefusedBecauseTheBusinessAlreadyGaveItBackInItsAccount_ShouldBeDone_WithoutWaitingForTheReview()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop, quantity: 2);
        await shop.RefundAsync(orderId, 55m);
        // Meanwhile the business gives the same money back from its account; the notification leaves it to the queue.
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddMinutes(5), refundId: "4488");
        await shop.NotifyAsync(payment);
        Assert.Single(await shop.Context.OnlinePaymentRefunds.ToListAsync());
        shop.Gateway.RefundOutcome = new OnlinePaymentRefundOutcome.Refused("Mercado Pago no hizo la devolución: el pago ya se devolvió.");

        // A refusal changes nothing in the payment: no notification or review would read it again.
        await shop.DispatchRefundAsync((await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).Id);

        var refund = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentRefundStatus.DoneInMercadoPago, "4488"), (refund.Status, refund.ProviderRefundId));
        Assert.Single((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
        Assert.Equal(0, (await shop.SettingsAsync()).RefundsRefused);
    }

    [Fact]
    public async Task Refund_Refused_ShouldNotTakeTheRefundOfAnotherOfTienditApps_DoneWithoutItsId()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop, quantity: 2);
        // Mercado Pago made the first refund but its answer came without the id: TienditApp has it done, without it.
        shop.Gateway.RefundOutcome = new OnlinePaymentRefundOutcome.Done(null);
        await shop.RefundAsync(orderId, 55m);
        await shop.DispatchRefundAsync((await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).Id);
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddMinutes(5), refundId: "4499");
        shop.Gateway.RefundOutcome = new OnlinePaymentRefundOutcome.Refused("Mercado Pago no hizo la devolución: saldo insuficiente");
        await shop.RefundAsync(orderId, 55m);

        await shop.DispatchRefundAsync((await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync(r => r.Status == OnlinePaymentRefundStatus.Requested)).Id);

        // That refund of Mercado Pago may be the first one's: the second stays for the business to give back.
        var refused = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync(r => r.Status != OnlinePaymentRefundStatus.Completed);
        Assert.Equal((OnlinePaymentRefundStatus.Failed, (string?)null), (refused.Status, refused.ProviderRefundId));
        Assert.Equal(1, (await shop.SettingsAsync()).RefundsRefused);

        // The business then gives it back from its account: that one is new, and settles it.
        shop.Gateway.GiveBack(payment, 55m, SalesTestFixture.Now.AddMinutes(30), refundId: "4500");
        await shop.NotifyAsync(payment);
        var refunds = await shop.Context.OnlinePaymentRefunds.AsNoTracking().ToListAsync();
        Assert.Equal(2, refunds.Count);
        Assert.Contains(refunds, r => r.Status == OnlinePaymentRefundStatus.DoneInMercadoPago);
        Assert.Equal(0, (await shop.SettingsAsync()).RefundsRefused);
        Assert.Equal(2, (await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds.Count);
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
    public async Task Dispute_ShouldNotGoBack_WithWhatMercadoPagoSaidBeforeItLastChanged()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        var opened = shop.Gateway.Dispute(payment, OnlinePaymentDetails.ChargedBackStatus, "in_process", SalesTestFixture.Now.AddDays(10));
        shop.Gateway.Dispute(payment, OnlinePaymentDetails.ChargedBackStatus, "settled", SalesTestFixture.Now.AddDays(40));
        await shop.NotifyAsync(payment);
        var checkoutId = await shop.Context.OnlineCheckouts.AsNoTracking().Select(c => c.Id).SingleAsync();

        // A review that read the payment while it was still open reflects it after the notification of how it ended.
        var stale = (await shop.Send(new ReconcileOnlinePaymentCommand(checkoutId, opened, []))).Value;

        Assert.False(stale.ChangedAnything);
        var order = await shop.OrderAsync(orderId);
        Assert.Equal((OnlinePaymentDispute.Lost, SalesTestFixture.Now.AddDays(40)), (order.OnlineDispute, order.OnlineDisputeUpdatedAt));
        Assert.Equal(0, (await shop.SettingsAsync()).DisputesOpen);
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
    public async Task RefundThatAnotherPathRecordedFirst_ShouldStayOnce_WhenTheDatabaseRefusesTheSecond()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        var refund = shop.Gateway.GiveBack(payment, 20m, SalesTestFixture.Now.AddHours(1));
        // The review records the same refund while the notification reflects it: the notification had already tracked its own,
        // and the database refuses it (the race on SQL Server's unique index is in OnlinePaymentReconciliationSqlServerTests).
        shop.RefuseNextReconcile = async _ =>
        {
            var order = await shop.Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == orderId);
            var orderRefund = order.RegisterRefund(20m, OrderPaymentMethod.Online, "Otra vía", shop.Clock.UtcNow, "Mercado Pago", null, refund.Id, false, []);
            shop.Context.OnlinePaymentRefunds.Add(OnlinePaymentRefund.MadeInMercadoPago(
                shop.Tenant.Id, orderId, orderRefund.Id, payment, 20m, refund.Id, OnlinePaymentRefundReason.InMercadoPago, shop.Clock.UtcNow));
            await shop.Context.SaveChangesAsync();
            shop.Context.ChangeTracker.Clear();
            // What this path had loaded and was about to add when the database refused it.
            await shop.Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == orderId);
            shop.Context.OnlinePaymentRefunds.Add(OnlinePaymentRefund.MadeInMercadoPago(
                shop.Tenant.Id, orderId, Guid.NewGuid(), payment, 20m, refund.Id, OnlinePaymentRefundReason.InMercadoPago, shop.Clock.UtcNow));
        };

        var notified = await shop.NotifyAsync(payment);

        // The one that lost reads what the other recorded, without what it had tracked.
        Assert.True(notified.IsSuccess);
        Assert.DoesNotContain(shop.Context.ChangeTracker.Entries<OnlinePaymentRefund>(), e => e.State == EntityState.Added);
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

        // Read all of it: reviewed until the overlap before now (Mercado Pago's clock may run ahead of ours).
        var reviewed = SalesTestFixture.Now.AddHours(4) - ReviewOnlinePaymentsCommandHandler.Overlap;
        Assert.Equal((2, 1, reviewed), (first.Read, first.Reflected, first.ReviewedUntil));
        Assert.Single((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
        // The first review reads from the checkout paid with the account (connected that same moment).
        Assert.Equal(SalesTestFixture.Now, shop.Gateway.ChangeSearches[0].From);
        Assert.Equal(reviewed, (await shop.Context.OnlinePaymentAccounts.AsNoTracking().SingleAsync()).PaymentsReviewedUntil);
        Assert.Equal(reviewed, (await shop.SettingsAsync()).PaymentsReviewedUntil);

        // Not again before its time (15 minutes after it ran); then from where it ended.
        shop.Clock.UtcNow = SalesTestFixture.Now.AddHours(4).AddMinutes(14);
        Assert.Null((await shop.ReviewAsync()).Value.ReviewedUntil);
        Assert.Single(shop.Gateway.ChangeSearches);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddHours(4).AddMinutes(15);
        var second = (await shop.ReviewAsync()).Value;
        Assert.Equal(reviewed, shop.Gateway.ChangeSearches[1].From);
        Assert.Equal((0, SalesTestFixture.Now.AddHours(4).AddMinutes(5)), (second.Reflected, second.ReviewedUntil));
        Assert.Single(await shop.Context.OnlinePaymentRefunds.ToListAsync());
    }

    [Fact]
    public async Task Review_ThatReadsAFullPage_ShouldGoOnFromTheLastChangeRead_UntilEveryPaymentWasRead()
    {
        await using var shop = await Shop.OpenAsync();
        await PlaceAsync(shop);
        var others = AddOtherPayments(shop, ReviewOnlinePaymentsCommandHandler.PageSize * ReviewOnlinePaymentsCommandHandler.MaxPagesPerReview + 3,
            i => SalesTestFixture.Now.AddMinutes(i + 1));
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);

        var first = (await shop.ReviewAsync()).Value;

        Assert.Equal(ReviewOnlinePaymentsCommandHandler.MaxPagesPerReview, shop.Gateway.ChangeSearches.Count);
        // Each page starts at the last change of the one before, which gives that payment back once more.
        Assert.Equal(ReviewOnlinePaymentsCommandHandler.PageSize * ReviewOnlinePaymentsCommandHandler.MaxPagesPerReview - 3, first.Read);
        Assert.All(shop.Gateway.ChangeSearches, search => Assert.Equal(0, search.Offset));
        // Reviewed until the last change it read, by Mercado Pago's clock; due again at once.
        var lastRead = shop.Gateway.Payments[shop.Gateway.ChangesGivenBack[^1]].LastChangedAt;
        Assert.Equal(lastRead, first.ReviewedUntil);

        var second = (await shop.ReviewAsync()).Value;

        Assert.Equal(lastRead, shop.Gateway.ChangeSearches[ReviewOnlinePaymentsCommandHandler.MaxPagesPerReview].From);
        Assert.Equal(SalesTestFixture.Now.AddDays(1) - ReviewOnlinePaymentsCommandHandler.Overlap, second.ReviewedUntil);
        Assert.Superset(others.Append(shop.Gateway.Payments.Keys.Except(others).Single()).ToHashSet(), shop.Gateway.ChangesGivenBack.ToHashSet());
        Assert.Subset(shop.Gateway.ChangesGivenBack.ToHashSet(), shop.Gateway.Payments.Keys.ToHashSet());
    }

    [Fact]
    public async Task Review_OfAnAccountWithMoreChangesInTenMinutesThanAReviewReads_ShouldAlwaysMoveForward()
    {
        await using var shop = await Shop.OpenAsync();
        await PlaceAsync(shop);
        // 450 sales outside TienditApp at a lunch peak, one every second: more than a review reads within the overlap.
        AddOtherPayments(shop, 450, i => SalesTestFixture.Now.AddHours(2).AddSeconds(i));
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);

        var cursors = new List<DateTimeOffset?>();
        for (var review = 0; review < 5 && cursors.LastOrDefault() != shop.Clock.UtcNow - ReviewOnlinePaymentsCommandHandler.Overlap; review++)
            cursors.Add((await shop.ReviewAsync()).Value.ReviewedUntil);

        // It never stays where it was, and three reviews read all of it.
        Assert.Equal(3, cursors.Count);
        Assert.Equal(cursors.OrderBy(c => c).ToList(), cursors);
        Assert.Equal(cursors.Count, cursors.Distinct().Count());
        Assert.Equal(shop.Gateway.Payments.Keys.ToHashSet(), shop.Gateway.ChangesGivenBack.ToHashSet());
    }

    [Fact]
    public async Task Review_OfAWholeReviewOfChangesAtOneInstant_ShouldNotStayThereForever()
    {
        await using var shop = await Shop.OpenAsync();
        await PlaceAsync(shop);
        var instant = SalesTestFixture.Now.AddHours(2);
        AddOtherPayments(shop, 260, _ => instant);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);

        var first = (await shop.ReviewAsync()).Value;
        var second = (await shop.ReviewAsync()).Value;
        var third = (await shop.ReviewAsync()).Value;

        // A page that changed at one instant goes on by position; a next review that again reads only that instant goes on
        // after it (the rest of that instant is left out and logged) instead of reading it again on every pass.
        Assert.Equal(instant, first.ReviewedUntil);
        Assert.True(second.ReviewedUntil > instant);
        Assert.Equal(shop.Clock.UtcNow - ReviewOnlinePaymentsCommandHandler.Overlap, third.ReviewedUntil);
    }

    [Fact]
    public async Task Review_ShouldReadEveryPayment_EvenWhenOneChangesAgainWhileItRuns()
    {
        await using var shop = await Shop.OpenAsync();
        await PlaceAsync(shop);
        var others = AddOtherPayments(shop, 120, i => SalesTestFixture.Now.AddMinutes(i + 1));
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        // While the second page is asked for, a payment of the first one changes again (it moves past now): by position, the
        // payment at the start of the second page would never come.
        shop.Gateway.WhileReading = () =>
        {
            shop.Gateway.WhileReading = () =>
            {
                shop.Gateway.Payments[others[10]] = shop.Gateway.Payments[others[10]] with { LastChangedAt = SalesTestFixture.Now.AddDays(2) };
                return Task.CompletedTask;
            };
            return Task.CompletedTask;
        };

        var review = (await shop.ReviewAsync()).Value;

        Assert.Equal(shop.Clock.UtcNow - ReviewOnlinePaymentsCommandHandler.Overlap, review.ReviewedUntil);
        Assert.Equal(shop.Gateway.Payments.Keys.ToHashSet(), shop.Gateway.ChangesGivenBack.ToHashSet());
    }

    [Fact]
    public async Task Review_ShouldNotTakeAFullPageForTheLast_WhenSomeOfItCouldNotBeRead()
    {
        await using var shop = await Shop.OpenAsync();
        await PlaceAsync(shop);
        AddOtherPayments(shop, ReviewOnlinePaymentsCommandHandler.PageSize - 2, i => SalesTestFixture.Now.AddMinutes(i + 1));
        // The first page also came with two results that could not be read.
        shop.Gateway.UnreadableInNextPage[1] = 2;
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);

        var review = (await shop.ReviewAsync()).Value;

        Assert.Equal(2, shop.Gateway.ChangeSearches.Count);
        Assert.Equal(shop.Clock.UtcNow - ReviewOnlinePaymentsCommandHandler.Overlap, review.ReviewedUntil);
    }

    [Fact]
    public async Task FirstReview_OfAnAccountConnectedAgain_ShouldReadFromItsOldestPaidCheckout()
    {
        await using var shop = await Shop.OpenAsync();
        var (orderId, payment) = await PlaceAsync(shop);
        // The business disconnected the account and connected it again a month later; meanwhile it gave money back from it.
        shop.Context.OnlinePaymentAccounts.RemoveRange(await shop.Context.OnlinePaymentAccounts.ToListAsync());
        await shop.Context.SaveChangesAsync();
        shop.Context.OnlinePaymentAccounts.Add(OnlinePaymentAccount.Connect(
            shop.Tenant.Id, FakeOnlinePaymentGateway.AccountId,
            OnlinePaymentKeys.ProtectAccess(shop.Protector, shop.Tenant.Id, FakeOnlinePaymentGateway.AccessToken),
            OnlinePaymentKeys.ProtectRefresh(shop.Protector, shop.Tenant.Id, FakeOnlinePaymentGateway.RefreshToken),
            SalesTestFixture.Now.AddDays(200), liveMode: true, Guid.NewGuid(), SalesTestFixture.Now.AddDays(30)));
        await shop.Context.SaveChangesAsync();
        shop.Gateway.GiveBack(payment, 20m, SalesTestFixture.Now.AddDays(10));
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(31);

        var review = (await shop.ReviewAsync()).Value;

        Assert.Equal(SalesTestFixture.Now, shop.Gateway.ChangeSearches[0].From);
        Assert.Equal(1, review.Reflected);
        Assert.Single((await shop.Context.Orders.AsNoTracking().Include(o => o.Refunds).SingleAsync(o => o.Id == orderId)).Refunds);
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

    /// <summary>Payments of the account TienditApp did not charge, each changed when <paramref name="changedAt"/> says.</summary>
    /// <returns>Their ids, in the order they were added.</returns>
    private static List<string> AddOtherPayments(Shop shop, int count, Func<int, DateTimeOffset> changedAt)
    {
        var template = shop.Gateway.Payments.Values.First();
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var id = $"8{i:D5}";
            shop.Gateway.Payments[id] = template with { Id = id, ExternalReference = $"otra-{i}", LastChangedAt = changedAt(i) };
            ids.Add(id);
        }
        return ids;
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
