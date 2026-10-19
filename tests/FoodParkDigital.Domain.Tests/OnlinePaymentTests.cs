using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.OnlinePayments.Events;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Paying online with the business's own Mercado Pago account (Fase 24I, ADR 0031): the account and its keys, the checkout
/// that only becomes an order once paid, the «En línea» payment outside every drawer, and the money that goes back.
/// </summary>
public class OnlinePaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 13, 0, 0, TimeSpan.FromHours(-6));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private const long ProviderUserId = 1234567890;
    private const string Request = "{\"items\":[]}";

    [Fact]
    public void Account_ShouldKeepTheEncryptedKeys_AndStartWithoutTakingPayments()
    {
        var account = Account();

        Assert.Equal((ProviderUserId, "access-cipher", "refresh-cipher", true, OwnerId, Now),
            (account.ProviderUserId, account.ProtectedAccessToken, account.ProtectedRefreshToken, account.LiveMode,
                account.ConnectedByUserId, account.ConnectedAt));
        // Connecting is not offering it: the business turns it on.
        Assert.False(account.AcceptsPayments);
        Assert.True(account.IsUsable(Now));
        Assert.False(account.NeedsRenewal(Now));
    }

    [Fact]
    public void Account_ShouldRenewItsKeys_ThirtyDaysBeforeTheyExpire_AndStopWorkingOnceExpired()
    {
        var account = Account();

        Assert.False(account.NeedsRenewal(Now.AddDays(149)));
        Assert.True(account.NeedsRenewal(Now.AddDays(150)));
        Assert.False(account.IsUsable(Now.AddDays(180)));

        account.RenewTokens("access-2", "refresh-2", Now.AddDays(330), Now.AddDays(150));
        Assert.Equal(("access-2", "refresh-2"), (account.ProtectedAccessToken, account.ProtectedRefreshToken));
        Assert.False(account.NeedsRenewal(Now.AddDays(150)));
    }

    [Fact]
    public void Account_WhoseKeyMercadoPagoRejects_ShouldCountAsExpired_UntilItIsRenewed()
    {
        var account = Account();

        account.RejectKey(Now.AddDays(10));

        Assert.False(account.IsUsable(Now.AddDays(10)));
        Assert.True(account.NeedsRenewal(Now.AddDays(10)));
        // Rejecting it again later never brings an expiry back.
        account.RejectKey(Now.AddDays(20));
        Assert.Equal(Now.AddDays(10), account.AccessTokenExpiresAt);
        account.RenewTokens("access-2", "refresh-2", Now.AddDays(200), Now.AddDays(20));
        Assert.True(account.IsUsable(Now.AddDays(20)));
    }

    [Fact]
    public void Account_ShouldRefuse_KeysMissingOrAlreadyExpired_AndAnUnknownAccount()
    {
        Assert.Throws<DomainException>(() => Account(access: " "));
        Assert.Throws<DomainException>(() => Account(expiresAt: Now));
        Assert.Throws<DomainException>(() => Account(providerUserId: 0));
        Assert.Throws<DomainException>(() => Account(access: new string('a', OnlinePaymentAccount.MaxProtectedTokenLength + 1)));
    }

    [Fact]
    public void Account_ConnectedAgain_ShouldKeepWhetherItTakesPayments()
    {
        var account = Account();
        account.SetAcceptsPayments(true);

        account.Reconnect(987654321, "access-3", "refresh-3", Now.AddDays(200), liveMode: true, Guid.NewGuid(), Now.AddDays(10));

        Assert.True(account.AcceptsPayments);
        Assert.Equal(987654321, account.ProviderUserId);
        Assert.Equal(Now.AddDays(10), account.ConnectedAt);
    }

    [Fact]
    public void Authorization_ShouldLastTenMinutes_WithAPkceVerifier()
    {
        var verifier = new string('v', 64);
        var authorization = OnlinePaymentAuthorization.Start(TenantId, OwnerId, verifier, Now);

        Assert.Equal(Now.AddMinutes(10), authorization.ExpiresAt);
        Assert.False(authorization.IsExpired(Now.AddMinutes(9)));
        Assert.True(authorization.IsExpired(Now.AddMinutes(10)));
        // RFC 7636: from 43 to 128 characters.
        Assert.Throws<DomainException>(() => OnlinePaymentAuthorization.Start(TenantId, OwnerId, new string('v', 42), Now));
        Assert.Throws<DomainException>(() => OnlinePaymentAuthorization.Start(TenantId, OwnerId, new string('v', 129), Now));
        Assert.Throws<DomainException>(() => OnlinePaymentAuthorization.Start(TenantId, Guid.Empty, verifier, Now));
    }

    [Fact]
    public void Checkout_ShouldAwaitPayment_ForThirtyMinutes_WithTheServersTotal()
    {
        var customer = Guid.NewGuid();
        var checkout = OnlineCheckout.Start(TenantId, ProviderUserId, 235.50m, Request, customer, " +525522107765 ", Now);

        Assert.Equal((OnlineCheckoutStatus.AwaitingPayment, 235.50m, Request, customer, "+525522107765", Now.AddMinutes(30)),
            (checkout.Status, checkout.Amount, checkout.Request, checkout.CustomerUserId, checkout.CustomerPhone, checkout.ExpiresAt));
        Assert.False(checkout.IsExpired(Now.AddMinutes(29)));
        Assert.True(checkout.IsExpired(Now.AddMinutes(30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10.555)]
    public void Checkout_ShouldRefuse_AnAmountThatIsNotMoney(decimal amount)
    {
        Assert.Throws<DomainException>(() => OnlineCheckout.Start(TenantId, ProviderUserId, amount, Request, null, null, Now));
    }

    [Fact]
    public void Checkout_ShouldRefuse_ABusinessWithoutAccount_OrWithoutTheOrder()
    {
        Assert.Throws<DomainException>(() => OnlineCheckout.Start(TenantId, 0, 100m, Request, null, null, Now));
        Assert.Throws<DomainException>(() => OnlineCheckout.Start(TenantId, ProviderUserId, 100m, " ", null, null, Now));
    }

    [Fact]
    public void Checkout_ShouldTakeOneMercadoPagoPage_OverHttps()
    {
        var checkout = Checkout();

        Assert.Throws<DomainException>(() => checkout.AttachPreference("pref-1", "http://www.mercadopago.com.mx/checkout"));
        Assert.Throws<DomainException>(() => checkout.AttachPreference("pref-1", "javascript:alert(1)"));
        checkout.AttachPreference("pref-1", "https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=pref-1");

        Assert.Equal("pref-1", checkout.PreferenceId);
        Assert.Throws<DomainException>(() => checkout.AttachPreference("pref-2", "https://www.mercadopago.com.mx/checkout"));
    }

    [Fact]
    public void Checkout_PaidInTime_ShouldPlaceTheOrder_AndForgetTheCustomer()
    {
        var checkout = Checkout();
        var orderId = Guid.NewGuid();

        checkout.Place("98765", Now.AddMinutes(30), orderId);

        Assert.Equal((OnlineCheckoutStatus.Placed, "98765", Now.AddMinutes(30), orderId),
            (checkout.Status, checkout.PaymentId, checkout.PaidAt, checkout.OrderId));
        Assert.Null(checkout.Request);
        Assert.Null(checkout.CustomerPhone);
        // Placed once: a second payment is the business's to give back, not a second order.
        Assert.Throws<DomainException>(() => checkout.Place("98766", Now.AddMinutes(5), Guid.NewGuid()));
    }

    [Fact]
    public void Checkout_PaidAfterItsTime_ShouldNotPlaceAnOrder()
    {
        var checkout = Checkout();

        Assert.Throws<DomainException>(() => checkout.Place("98765", Now.AddMinutes(30).AddSeconds(1), Guid.NewGuid()));
        Assert.Equal(OnlineCheckoutStatus.AwaitingPayment, checkout.Status);
    }

    [Fact]
    public void Checkout_ThatCouldNotBePlaced_ShouldSayWhy_AndForgetTheCustomer_AlsoOnceExpired()
    {
        var awaiting = Checkout();
        awaiting.FailToPlace("98765", Now.AddMinutes(3), OnlineCheckoutFailure.OrderRefused, "  El Latte se agotó.  ");
        Assert.Equal((OnlineCheckoutStatus.NotPlaced, OnlineCheckoutFailure.OrderRefused, "El Latte se agotó.", "98765"),
            (awaiting.Status, awaiting.Failure, awaiting.FailureMessage, awaiting.PaymentId));
        Assert.Null(awaiting.Request);

        var expired = Checkout();
        expired.Expire(Now.AddMinutes(40));
        expired.FailToPlace("98766", Now.AddMinutes(41), OnlineCheckoutFailure.PaidAfterExpiry, "Pagaste después de tiempo.");
        Assert.Equal(OnlineCheckoutStatus.NotPlaced, expired.Status);

        var placed = Checkout();
        placed.Place("1", Now, Guid.NewGuid());
        Assert.Throws<DomainException>(() => placed.FailToPlace("2", Now, OnlineCheckoutFailure.OrderRefused, "No."));
    }

    [Fact]
    public void Checkout_ShouldExpire_OnlyOnceItsTimeIsOver_AndForgetTheCustomer()
    {
        var checkout = Checkout();

        Assert.Throws<DomainException>(() => checkout.Expire(Now.AddMinutes(29)));
        checkout.Expire(Now.AddMinutes(30));

        Assert.Equal(OnlineCheckoutStatus.Expired, checkout.Status);
        Assert.Null(checkout.Request);
        Assert.Null(checkout.CustomerPhone);
        Assert.Throws<DomainException>(() => checkout.Place("98765", Now.AddMinutes(1), Guid.NewGuid()));
    }

    [Fact]
    public void Refund_ShouldBeAskedOfMercadoPago_ThroughTheQueue()
    {
        var checkoutId = Guid.NewGuid();
        var refund = OnlinePaymentRefund.ForCheckout(TenantId, checkoutId, " 98765 ", 235.50m, OnlinePaymentRefundReason.NotPlaced, Now);

        Assert.Equal((OnlinePaymentRefundStatus.Requested, "98765", 235.50m, checkoutId, Now),
            (refund.Status, refund.PaymentId, refund.Amount, refund.CheckoutId, refund.RequestedAt));
        var requested = Assert.IsType<OnlinePaymentRefundRequestedEvent>(Assert.Single(refund.DomainEvents));
        Assert.Equal((refund.Id, TenantId), (requested.RefundId, requested.TenantId));
    }

    [Fact]
    public void Refund_ByTheBusiness_ShouldNameItsOrderAndItsRefund()
    {
        var orderId = Guid.NewGuid();
        var orderRefundId = Guid.NewGuid();
        var refund = OnlinePaymentRefund.ForOrder(TenantId, orderId, orderRefundId, "98765", 50m, Now);

        Assert.Equal((OnlinePaymentRefundReason.ByBusiness, orderId, orderRefundId), (refund.Reason, refund.OrderId, refund.OrderRefundId));
        Assert.Throws<DomainException>(() => OnlinePaymentRefund.ForOrder(TenantId, orderId, Guid.Empty, "98765", 50m, Now));
        Assert.Throws<DomainException>(() =>
            OnlinePaymentRefund.ForCheckout(TenantId, Guid.NewGuid(), "98765", 50m, OnlinePaymentRefundReason.ByBusiness, Now));
        Assert.Throws<DomainException>(() =>
            OnlinePaymentRefund.ForCheckout(TenantId, Guid.NewGuid(), "98765", 0m, OnlinePaymentRefundReason.NotPlaced, Now));
    }

    [Fact]
    public void Refund_ShouldBeDoneOnce_AndAnsweringAgainShouldChangeNothing()
    {
        var refund = OnlinePaymentRefund.ForCheckout(TenantId, Guid.NewGuid(), "98765", 100m, OnlinePaymentRefundReason.DuplicatePayment, Now);

        refund.Complete("555", Now.AddMinutes(1));
        refund.Complete("556", Now.AddMinutes(2));

        Assert.Equal((OnlinePaymentRefundStatus.Completed, "555", Now.AddMinutes(1)), (refund.Status, refund.ProviderRefundId, refund.SettledAt));
        Assert.Throws<DomainException>(() => refund.Fail("Sin saldo", Now));
    }

    [Fact]
    public void Refund_RefusedByMercadoPago_ShouldSayWhy_AndNotBeDoneAfterwards()
    {
        var refund = OnlinePaymentRefund.ForCheckout(TenantId, Guid.NewGuid(), "98765", 100m, OnlinePaymentRefundReason.NotPlaced, Now);

        refund.Fail("  La cuenta no tiene saldo suficiente.  ", Now.AddMinutes(1));

        Assert.Equal((OnlinePaymentRefundStatus.Failed, "La cuenta no tiene saldo suficiente."), (refund.Status, refund.FailureMessage));
        Assert.Throws<DomainException>(() => refund.Complete("555", Now));
    }

    [Fact]
    public void Order_PaidOnline_ShouldRecordTheTotal_OutsideEveryDrawer()
    {
        var order = OnlineOrder();

        var payment = order.RegisterOnlinePayment(" 98765 ", Now, inventoryLocationId: null);

        Assert.Equal((OrderPaymentMethod.Online, 235.50m, "98765"), (payment.Method, payment.Amount, payment.Reference));
        Assert.Equal((235.50m, 0m), (order.PaidAmount, order.Balance));
        Assert.False(OrderPaymentMethodRules.EntersADrawer(payment.Method));
        Assert.Equal(235.50m, order.OnlineRefundable);
    }

    [Fact]
    public void Order_PaidOnline_ShouldOnlyBeAnOrderOfTheOnlineMenu_BeforeAnyOtherPayment()
    {
        var counter = OnlineOrder(OrderChannel.Counter);
        Assert.Equal(Order.OnlinePaymentOnlyOnlineMessage,
            Assert.Throws<DomainException>(() => counter.RegisterOnlinePayment("98765", Now, null)).Message);

        var paid = OnlineOrder();
        paid.RegisterOnlinePayment("98765", Now, null);
        Assert.Equal(Order.OnlinePaymentBeforeAnyPaymentMessage,
            Assert.Throws<DomainException>(() => paid.RegisterOnlinePayment("98766", Now, null)).Message);
    }

    [Fact]
    public void Order_ShouldNeverTakeAnOnlinePaymentByHand()
    {
        var order = OnlineOrder();

        var error = Assert.Throws<DomainException>(() =>
            order.RegisterPayment(100m, OrderPaymentMethod.Online, Now, null, null, "98765", null));

        Assert.Equal(OrderPaymentMethodRules.OnlineOnlyOnCreationMessage, error.Message);
        Assert.Empty(order.Payments);
    }

    [Fact]
    public void Order_PaidOnline_ShouldGiveBackOnline_UpToWhatWasPaidOnline()
    {
        var order = OnlineOrder();
        order.RegisterOnlinePayment("98765", Now, null);

        order.RegisterRefund(200m, OrderPaymentMethod.Online, "Se agotó el pan", Now, "dueño", null, null, false, []);
        Assert.Equal(35.50m, order.OnlineRefundable);

        Assert.Throws<DomainException>(() =>
            order.RegisterRefund(35.51m, OrderPaymentMethod.Online, "Otro", Now, "dueño", null, null, false, []));
        order.RegisterRefund(35.50m, OrderPaymentMethod.Online, "Se agotó el refresco", Now, "dueño", null, null, false, []);
        Assert.Equal(0m, order.OnlineRefundable);

        // An order paid at the counter has nothing to give back online.
        var counter = OnlineOrder();
        counter.RegisterPayment(235.50m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(Order.OnlineRefundOnlyOfOnlineMessage, Assert.Throws<DomainException>(() =>
            counter.RegisterRefund(10m, OrderPaymentMethod.Online, "Otro", Now, "dueño", null, null, false, [])).Message);
    }

    private static OnlinePaymentAccount Account(
        string access = "access-cipher", DateTimeOffset? expiresAt = null, long providerUserId = ProviderUserId) =>
        OnlinePaymentAccount.Connect(
            TenantId, providerUserId, access, "refresh-cipher", expiresAt ?? Now.AddDays(180), liveMode: true, OwnerId, Now);

    private static OnlineCheckout Checkout() =>
        OnlineCheckout.Start(TenantId, ProviderUserId, 235.50m, Request, null, "+525522107765", Now);

    private static Order OnlineOrder(OrderChannel channel = OrderChannel.Online)
    {
        var order = new Order(
            tenantId: TenantId,
            customerUserId: null,
            temporaryCustomerId: Guid.NewGuid(),
            createdByBusiness: false,
            orderNumber: "ORD-000412",
            businessType: BusinessType.Food,
            orderType: OrderType.Retail,
            customerName: "Valeria Morales",
            customerPhone: "+525522107765",
            customerNotes: null,
            tableNumber: null,
            // Menu prices include IVA, so the total is what the customer pays.
            pricesIncludeTax: true,
            channel: channel);
        order.AddItem(Guid.NewGuid(), "Torta de jamón", 2, 117.75m);
        return order;
    }
}
