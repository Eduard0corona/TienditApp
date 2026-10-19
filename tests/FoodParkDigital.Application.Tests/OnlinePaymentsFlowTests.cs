using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.OnlinePayments.Events;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Paying online (Fase 24I, ADR 0031): the menu prices the order and sends the customer to Mercado Pago; the order exists
/// only once an approved payment of the business's account, for exactly that amount, comes back (by the notification, the
/// customer's return or the sweep), and otherwise the money goes back. The business connects its own account.
/// </summary>
public class OnlinePaymentsFlowTests
{
    private const string Slug = "cafe-luna";

    [Fact]
    public async Task Checkout_ShouldPriceTheOrderAndSendTheCustomerToPay_WithoutCreatingAnything()
    {
        await using var shop = await Shop.OpenAsync();

        var started = await shop.CheckoutAsync(quantity: 2);

        Assert.True(started.IsSuccess, started.Error);
        Assert.Equal(110m, started.Value.Amount);
        Assert.Equal(SalesTestFixture.Now + OnlineCheckout.TimeToPay, started.Value.ExpiresAt);
        var sent = Assert.Single(shop.Gateway.Checkouts);
        Assert.Equal((started.Value.CheckoutId, 110m, "Pedido en Negocio cafe-luna"), (sent.CheckoutId, sent.Amount, sent.Title));
        Assert.Equal($"https://tienditapp.test/m/{Slug}/pago/{started.Value.CheckoutId}", sent.ReturnUrl);
        Assert.Equal([FakeOnlinePaymentGateway.AccessToken], shop.Gateway.AccessTokensUsed);
        var checkout = await shop.Context.OnlineCheckouts.SingleAsync();
        Assert.Equal((OnlineCheckoutStatus.AwaitingPayment, "+525512345678", started.Value.CheckoutUrl), (checkout.Status, checkout.CustomerPhone, checkout.CheckoutUrl));
        Assert.Equal(FakeOnlinePaymentGateway.AccountId, checkout.ProviderUserId);
        // Nothing exists for the customer until the payment is approved.
        Assert.Empty(await shop.Context.Orders.ToListAsync());
        Assert.Empty(await shop.Context.TemporaryCustomers.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Checkout_ShouldNotBeOffered_UntilTheBusinessConnectsItsAccountAndTurnsItOn()
    {
        await using var shop = await Shop.OpenAsync(connect: false);
        Assert.False((await shop.MenuAsync()).PaysOnline);
        Assert.Equal(CreateOnlineCheckoutCommandHandler.NotOfferedMessage, (await shop.CheckoutAsync()).Error);

        var account = await shop.ConnectAsync(accepts: false);
        Assert.Equal(CreateOnlineCheckoutCommandHandler.NotOfferedMessage, (await shop.CheckoutAsync()).Error);

        account.SetAcceptsPayments(true);
        await shop.Context.SaveChangesAsync();
        Assert.True((await shop.MenuAsync()).PaysOnline);
        Assert.True((await shop.CheckoutAsync()).IsSuccess);

        // A platform in test mode takes no real account, and without Mercado Pago configured nothing is offered.
        shop.Gateway.Sandbox = true;
        Assert.False((await shop.MenuAsync()).PaysOnline);
        Assert.Equal(CreateOnlineCheckoutCommandHandler.NotOfferedMessage, (await shop.CheckoutAsync()).Error);
        shop.Gateway.Sandbox = false;
        shop.Gateway.IsConfigured = false;
        Assert.False((await shop.MenuAsync()).PaysOnline);
        Assert.Equal(CreateOnlineCheckoutCommandHandler.NotOfferedMessage, (await shop.CheckoutAsync()).Error);
    }

    [Fact]
    public async Task Checkout_ShouldNotBeOffered_WithAnExpiredKeyOrAKeyThatNoLongerDecrypts()
    {
        await using var shop = await Shop.OpenAsync();

        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(181);
        Assert.Equal(CreateOnlineCheckoutCommandHandler.NotOfferedMessage, (await shop.CheckoutAsync()).Error);

        shop.Clock.UtcNow = SalesTestFixture.Now;
        shop.Protector.IsConfigured = false;
        Assert.Equal(CreateOnlineCheckoutCommandHandler.NotOfferedMessage, (await shop.CheckoutAsync()).Error);
        Assert.Empty(shop.Gateway.Checkouts);
    }

    [Fact]
    public async Task Checkout_ShouldRefuseWhatAnOrderWouldRefuse_BeforeAnythingIsCharged()
    {
        await using var shop = await Shop.OpenAsync();

        Assert.Equal(
            PlacePublicOrderCommandHandler.TotalChangedMessage(55m),
            (await shop.CheckoutAsync(expectedTotal: 50m)).Error);
        shop.Product.SetAvailability(false);
        await shop.Context.SaveChangesAsync();
        Assert.Equal("'Latte' no se puede pedir en línea.", (await shop.CheckoutAsync()).Error);
        shop.Product.SetAvailability(true);
        await shop.CloseDrawerAsync();
        Assert.Equal(PlacePublicOrderCommandHandler.OrdersPausedMessage, (await shop.CheckoutAsync()).Error);
        Assert.Empty(shop.Gateway.Checkouts);
        Assert.Empty(await shop.Context.OnlineCheckouts.ToListAsync());
    }

    [Fact]
    public async Task Checkout_ShouldCountThePaymentsInCourseOfAPhone_AsItsOrdersWaiting()
    {
        await using var shop = await Shop.OpenAsync();

        for (var i = 0; i < PlacePublicOrderCommandHandler.MaxPendingPerPhone; i++)
            Assert.True((await shop.CheckoutAsync()).IsSuccess);

        Assert.Equal(PlacePublicOrderCommandHandler.TooManyWaitingMessage, (await shop.CheckoutAsync()).Error);
        Assert.True((await shop.CheckoutAsync(phone: "5587654321")).IsSuccess);
    }

    [Fact]
    public async Task Checkout_WhenMercadoPagoDoesNotAnswer_ShouldTellTheCustomer_AndNotCountIt()
    {
        await using var shop = await Shop.OpenAsync();
        shop.Gateway.Unavailable = true;

        for (var i = 0; i <= PlacePublicOrderCommandHandler.MaxPendingPerPhone; i++)
            Assert.Equal(CreateOnlineCheckoutCommandHandler.GatewayUnavailableMessage, (await shop.CheckoutAsync()).Error);

        shop.Gateway.Unavailable = false;
        Assert.True((await shop.CheckoutAsync()).IsSuccess);
        Assert.Equal(1, await shop.Context.OnlineCheckouts.CountAsync(c => c.PreferenceId != null));
    }

    [Fact]
    public void Checkout_PaidOnline_ShouldNotCarryABillForTheCourier()
    {
        var result = new CreateOnlineCheckoutCommandValidator().Validate(new CreateOnlineCheckoutCommand
        {
            Slug = Slug,
            CustomerName = "Ana",
            CustomerPhone = "5512345678",
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }],
            Delivery = new OrderDeliveryInput { PostalCode = "72000", AddressLine = "Av. Reforma 123", PaysWith = 200m }
        });

        Assert.Contains(result.Errors, e => e.ErrorMessage == CreateOnlineCheckoutCommandHandler.PaidOnlineNoBillMessage);
    }

    [Fact]
    public async Task ApprovedPayment_ShouldPlaceTheOrder_WithAnOnlinePaymentForItsTotal_OutsideTheDrawers()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync(quantity: 2)).Value;
        var payment = shop.Gateway.Approve(started.CheckoutId, 110m, SalesTestFixture.Now.AddMinutes(5));

        var status = await shop.ConfirmAsync(started.CheckoutId, payment.Id, started.ReturnKey);

        var order = await shop.Context.Orders.Include(o => o.Payments).SingleAsync();
        Assert.Equal((OrderChannel.Online, 110m, OrderStatus.Pending), (order.Channel, order.Total, order.Status));
        var paid = Assert.Single(order.Payments);
        Assert.Equal((OrderPaymentMethod.Online, 110m, payment.Id, SalesTestFixture.Now), (paid.Method, paid.Amount, paid.Reference, paid.PaidAt));
        Assert.Equal(0m, order.Balance);
        Assert.NotNull(order.TrackingToken);
        Assert.Empty(await shop.Context.CashTransactions.ToListAsync());

        var checkout = await shop.Context.OnlineCheckouts.SingleAsync();
        Assert.Equal((OnlineCheckoutStatus.Placed, order.Id, payment.Id), (checkout.Status, checkout.OrderId, checkout.PaymentId));
        Assert.Null(checkout.Request);
        Assert.Null(checkout.CustomerPhone);
        Assert.Equal((OnlineCheckoutStatus.Placed, order.OrderNumber, order.TrackingToken), (status!.Status, status.OrderNumber, status.TrackingToken));
        Assert.Null(status.CheckoutUrl);
        Assert.Empty(await shop.Context.OnlinePaymentRefunds.ToListAsync());

        // The return URL reaches request telemetry: without the browser's key, or with another checkout's, no tracking link.
        var other = (await shop.CheckoutAsync(phone: "5587654321")).Value;
        Assert.Null((await shop.ConfirmAsync(started.CheckoutId, payment.Id))!.TrackingToken);
        Assert.Null((await shop.ConfirmAsync(started.CheckoutId, payment.Id, other.ReturnKey))!.TrackingToken);
        Assert.Equal(order.OrderNumber, (await shop.ConfirmAsync(started.CheckoutId, payment.Id, "v1.no-es-una-llave"))!.OrderNumber);
    }

    [Fact]
    public async Task SamePayment_LearnedByEveryPath_ShouldPlaceOneOrder()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync()).Value;
        var payment = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));

        Assert.Equal(OnlinePaymentApplication.Placed, (await shop.NotifyAsync(payment.Id)).Value);
        Assert.Equal(OnlinePaymentApplication.AlreadyApplied, (await shop.NotifyAsync(payment.Id)).Value);
        await shop.ConfirmAsync(started.CheckoutId, payment.Id);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(3);
        await shop.SweepAsync();

        Assert.Equal(1, await shop.Context.Orders.CountAsync());
        Assert.Empty(await shop.Context.OnlinePaymentRefunds.ToListAsync());
    }

    [Fact]
    public async Task SecondPayment_OfAPaidCheckout_ShouldGoBackToTheCustomer()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync()).Value;
        var first = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));
        var second = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(2));

        await shop.ConfirmAsync(started.CheckoutId, paymentId: null);
        Assert.Equal(OnlinePaymentApplication.Duplicate, (await shop.NotifyAsync(second.Id)).Value);

        Assert.Equal(1, await shop.Context.Orders.CountAsync());
        Assert.Equal(first.Id, (await shop.Context.OnlineCheckouts.SingleAsync()).PaymentId);
        var refund = await shop.Context.OnlinePaymentRefunds.SingleAsync();
        Assert.Equal((second.Id, 55m, OnlinePaymentRefundReason.DuplicatePayment, OnlinePaymentRefundStatus.Requested),
            (refund.PaymentId, refund.Amount, refund.Reason, refund.Status));
    }

    [Fact]
    public async Task PaymentThatIsNotOfThisCheckoutAndAccount_ShouldChangeNothing()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync()).Value;
        var otherAccount = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1), collector: 111);
        var otherCheckout = shop.Gateway.Approve(Guid.NewGuid(), 55m, SalesTestFixture.Now.AddMinutes(1));
        var rejected = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1)) with { Status = "rejected" };
        shop.Gateway.Payments[rejected.Id] = rejected;
        var testMoney = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1)) with { LiveMode = false };
        shop.Gateway.Payments[testMoney.Id] = testMoney;

        foreach (var payment in new[] { otherAccount, otherCheckout, rejected, testMoney })
            Assert.Equal(OnlinePaymentApplication.Ignored, (await shop.NotifyAsync(payment.Id)).Value);
        Assert.Equal(OnlineCheckoutStatus.AwaitingPayment, (await shop.ConfirmAsync(started.CheckoutId, otherCheckout.Id))!.Status);

        Assert.Empty(await shop.Context.Orders.ToListAsync());
        Assert.Empty(await shop.Context.OnlinePaymentRefunds.ToListAsync());
        Assert.Equal(OnlineCheckoutStatus.AwaitingPayment, (await shop.Context.OnlineCheckouts.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(54.99, "MXN")]
    [InlineData(55, "USD")]
    public async Task PaymentForAnotherAmountOrCurrency_ShouldGoBack(decimal amount, string currency)
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync()).Value;
        var payment = shop.Gateway.Approve(started.CheckoutId, amount, SalesTestFixture.Now.AddMinutes(1), currency: currency);

        var status = await shop.ConfirmAsync(started.CheckoutId, payment.Id);

        Assert.Empty(await shop.Context.Orders.ToListAsync());
        var checkout = await shop.Context.OnlineCheckouts.SingleAsync();
        Assert.Equal((OnlineCheckoutStatus.NotPlaced, OnlineCheckoutFailure.PaymentMismatch), (checkout.Status, checkout.Failure));
        var refund = await shop.Context.OnlinePaymentRefunds.SingleAsync();
        Assert.Equal((payment.Id, amount, OnlinePaymentRefundReason.NotPlaced), (refund.PaymentId, refund.Amount, refund.Reason));
        Assert.Equal((OnlineCheckoutStatus.NotPlaced, ApplyOnlinePaymentCommandHandler.MismatchMessage, OnlinePaymentRefundStatus.Requested),
            (status!.Status, status.FailureMessage, status.RefundStatus));
    }

    [Fact]
    public async Task PaymentApprovedAfterTheTimeToPay_ShouldGoBack_EvenAfterTheSweepExpiredIt()
    {
        await using var shop = await Shop.OpenAsync();
        var late = (await shop.CheckoutAsync()).Value;
        var expired = (await shop.CheckoutAsync(phone: "5587654321")).Value;
        shop.Clock.UtcNow = SalesTestFixture.Now + OnlineCheckout.TimeToPay + SweepOnlinePaymentsCommandHandler.ExpiryGrace;
        Assert.Equal(2, (await shop.SweepAsync()).Value.Expired);
        var lateCheckout = await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == late.CheckoutId);
        Assert.Equal((OnlineCheckoutStatus.Expired, (string?)null, (string?)null), (lateCheckout.Status, lateCheckout.Request, lateCheckout.CustomerPhone));

        var payment = shop.Gateway.Approve(late.CheckoutId, 55m, SalesTestFixture.Now + OnlineCheckout.TimeToPay + TimeSpan.FromSeconds(1));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, (await shop.NotifyAsync(payment.Id)).Value);

        Assert.Empty(await shop.Context.Orders.ToListAsync());
        var failed = await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == late.CheckoutId);
        Assert.Equal((OnlineCheckoutStatus.NotPlaced, OnlineCheckoutFailure.PaidAfterExpiry), (failed.Status, failed.Failure));
        Assert.Equal(payment.Id, (await shop.Context.OnlinePaymentRefunds.SingleAsync()).PaymentId);
        Assert.Equal(OnlineCheckoutStatus.Expired, (await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == expired.CheckoutId)).Status);
    }

    [Fact]
    public async Task PaidOrder_ThatCanNoLongerBePlaced_ShouldGoBack()
    {
        await using var shop = await Shop.OpenAsync();
        var outOfStock = (await shop.CheckoutAsync()).Value;
        var repriced = (await shop.CheckoutAsync(phone: "5587654321")).Value;
        var closed = (await shop.CheckoutAsync(phone: "5511112222")).Value;

        shop.Product.SetAvailability(false);
        await shop.Context.SaveChangesAsync();
        var first = shop.Gateway.Approve(outOfStock.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, (await shop.NotifyAsync(first.Id)).Value);

        shop.Product.SetAvailability(true);
        Reprice(shop.Product, 60m);
        await shop.Context.SaveChangesAsync();
        var second = shop.Gateway.Approve(repriced.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, (await shop.NotifyAsync(second.Id)).Value);

        await shop.CloseDrawerAsync();
        var third = shop.Gateway.Approve(closed.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, (await shop.NotifyAsync(third.Id)).Value);

        Assert.Empty(await shop.Context.Orders.ToListAsync());
        Assert.Empty(await shop.Context.TemporaryCustomers.IgnoreQueryFilters().ToListAsync());
        var failures = await shop.Context.OnlineCheckouts.OrderBy(c => c.StartedAt).ThenBy(c => c.Amount)
            .ToDictionaryAsync(c => c.Id, c => (c.Failure, c.FailureMessage));
        Assert.Equal((OnlineCheckoutFailure.OrderRefused, "'Latte' no se puede pedir en línea."), failures[outOfStock.CheckoutId]);
        Assert.Equal((OnlineCheckoutFailure.OrderRefused, PlacePublicOrderCommandHandler.TotalChangedMessage(60m)), failures[repriced.CheckoutId]);
        Assert.Equal((OnlineCheckoutFailure.StoppedTakingOrders, PlacePublicOrderCommandHandler.OrdersPausedMessage), failures[closed.CheckoutId]);
        Assert.Equal(3, await shop.Context.OnlinePaymentRefunds.CountAsync(r => r.Reason == OnlinePaymentRefundReason.NotPlaced));
    }

    [Fact]
    public async Task Sweep_ShouldPlaceWhatWasPaid_AndLeaveTheYoungestToTheNotification()
    {
        await using var shop = await Shop.OpenAsync();
        var paid = (await shop.CheckoutAsync()).Value;
        shop.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(2);
        var young = (await shop.CheckoutAsync(phone: "5587654321")).Value;
        shop.Gateway.Approve(paid.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));
        shop.Gateway.Approve(young.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(2));

        var sweep = await shop.SweepAsync();

        Assert.Equal((1, 0), (sweep.Value.Checked, sweep.Value.Expired));
        Assert.Equal(OnlineCheckoutStatus.Placed, (await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == paid.CheckoutId)).Status);
        Assert.Equal(OnlineCheckoutStatus.AwaitingPayment, (await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == young.CheckoutId)).Status);
    }

    [Fact]
    public async Task Sweep_ShouldRenewTheKey_WhenLittleTimeIsLeft()
    {
        await using var shop = await Shop.OpenAsync();
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(155);

        var sweep = await shop.SweepAsync();

        Assert.True(sweep.Value.KeyRenewed);
        var account = await shop.Context.OnlinePaymentAccounts.AsNoTracking().SingleAsync();
        Assert.Equal($"{FakeOnlinePaymentGateway.AccessToken}-1", OnlinePaymentKeys.Access(shop.Protector, account, shop.Clock.UtcNow));
        Assert.False((await shop.SweepAsync()).Value.KeyRenewed);
        Assert.Equal(1, shop.Gateway.Renewals);
    }

    [Fact]
    public async Task Connect_ShouldStoreTheKeysEncrypted_AndStartWithPaymentsOff()
    {
        await using var shop = await Shop.OpenAsync(connect: false);

        var started = await shop.StartConnectionAsync();
        var state = shop.StateOf(started.Value.AuthorizationUrl);
        var connected = await shop.CompleteConnectionAsync(state, "TG-code");

        Assert.True(connected.IsSuccess, connected.Error);
        Assert.Equal((true, FakeOnlinePaymentGateway.AccountId, false), (connected.Value.Connected, connected.Value.AccountId, connected.Value.AcceptsPayments));
        var account = await shop.Context.OnlinePaymentAccounts.SingleAsync();
        // Encrypted for this business: as another business's key it does not decrypt.
        Assert.StartsWith($"protected:mercadopago-access:{shop.Tenant.Id:N}:", account.ProtectedAccessToken);
        Assert.Equal(FakeOnlinePaymentGateway.AccessToken, OnlinePaymentKeys.Access(shop.Protector, account, SalesTestFixture.Now));
        var (code, verifier) = Assert.Single(shop.Gateway.Connections);
        Assert.Equal("TG-code", code);
        Assert.InRange(verifier.Length, 43, 128);
        Assert.Empty(await shop.Context.OnlinePaymentAuthorizations.ToListAsync());
        Assert.Equal("OnlinePaymentAccountConnected", (await shop.Context.AuditLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task Connect_ShouldBeFinishedOnce_ByWhoStartedIt_WithinTenMinutes()
    {
        await using var shop = await Shop.OpenAsync(connect: false);
        var state = shop.StateOf((await shop.StartConnectionAsync()).Value.AuthorizationUrl);

        var owner = shop.Fixture.CurrentUser.UserId;
        shop.Fixture.CurrentUser.UserId = Guid.NewGuid().ToString();
        Assert.Equal(OnlinePaymentSettings.AuthorizationExpiredMessage, (await shop.CompleteConnectionAsync(state, "TG-code")).Error);
        shop.Fixture.CurrentUser.UserId = owner;

        shop.Gateway.Unavailable = true;
        Assert.False((await shop.CompleteConnectionAsync(state, "TG-code")).IsSuccess);
        shop.Gateway.Unavailable = false;
        Assert.True((await shop.CompleteConnectionAsync(state, "TG-code")).IsSuccess);
        Assert.Equal(OnlinePaymentSettings.AuthorizationExpiredMessage, (await shop.CompleteConnectionAsync(state, "TG-code")).Error);

        var late = shop.StateOf((await shop.StartConnectionAsync()).Value.AuthorizationUrl);
        shop.Clock.UtcNow = SalesTestFixture.Now + OnlinePaymentAuthorization.Lifetime;
        Assert.Equal(OnlinePaymentSettings.AuthorizationExpiredMessage, (await shop.CompleteConnectionAsync(late, "TG-code")).Error);
        Assert.Single(shop.Gateway.Connections, c => c.Code == "TG-code");
    }

    [Fact]
    public async Task Connect_ShouldRefuseACodeMercadoPagoRefuses_AndATestAccount()
    {
        await using var shop = await Shop.OpenAsync(connect: false);
        shop.Gateway.RefuseCode = true;
        var refused = await shop.CompleteConnectionAsync(shop.StateOf((await shop.StartConnectionAsync()).Value.AuthorizationUrl), "TG-used");
        Assert.Contains("no aceptó", refused.Error);

        shop.Gateway.RefuseCode = false;
        shop.Gateway.Credentials = new OnlinePaymentCredentials(5, "TEST-1", "TG-1", SalesTestFixture.Now.AddDays(180), LiveMode: false);
        var test = await shop.CompleteConnectionAsync(shop.StateOf((await shop.StartConnectionAsync()).Value.AuthorizationUrl), "TG-code");
        Assert.Equal(CompleteOnlinePaymentConnectionCommandHandler.TestAccountMessage, test.Error);

        Assert.Empty(await shop.Context.OnlinePaymentAccounts.ToListAsync());
        Assert.Empty(await shop.Context.OnlinePaymentAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task Account_ShouldNotChange_WhilePaymentsAreInCourse()
    {
        await using var shop = await Shop.OpenAsync();
        await shop.CheckoutAsync();

        Assert.Equal(OnlinePaymentSettings.InCourseMessage, (await shop.StartConnectionAsync()).Error);
        Assert.Equal(OnlinePaymentSettings.InCourseMessage, (await shop.DisconnectAsync()).Error);
        Assert.Single(await shop.Context.OnlinePaymentAccounts.ToListAsync());

        // Turned off, the payment in course ends (here, unpaid) and then the account can go.
        Assert.False((await shop.AcceptAsync(false)).Value.AcceptsPayments);
        shop.Clock.UtcNow = SalesTestFixture.Now + OnlineCheckout.TimeToPay + SweepOnlinePaymentsCommandHandler.ExpiryGrace;
        await shop.SweepAsync();
        var disconnected = await shop.DisconnectAsync();
        Assert.False(disconnected.Value.Connected);
        Assert.Empty(await shop.Context.OnlinePaymentAccounts.ToListAsync());
        Assert.Equal(
            ["OnlinePaymentsAcceptedChanged", "OnlinePaymentAccountDisconnected"],
            await shop.Context.AuditLogs.OrderBy(a => a.Timestamp).Select(a => a.Action).ToListAsync());
    }

    [Fact]
    public async Task AcceptingPayments_ShouldNeedAConnectedAccountThatWorks()
    {
        await using var shop = await Shop.OpenAsync(connect: false);
        Assert.False((await shop.AcceptAsync(true)).IsSuccess);

        await shop.ConnectAsync(accepts: false);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(181);
        Assert.Equal(OnlinePaymentSettings.ReconnectMessage, (await shop.AcceptAsync(true)).Error);
        Assert.True((await shop.SettingsAsync()).NeedsReconnect);

        shop.Clock.UtcNow = SalesTestFixture.Now;
        Assert.True((await shop.AcceptAsync(true)).Value.AcceptsPayments);
        var settings = await shop.SettingsAsync();
        Assert.Equal((true, true, true, false), (settings.Available, settings.Connected, settings.MenuTakesOrders, settings.NeedsReconnect));
    }

    [Fact]
    public async Task OnlineRefund_ShouldGoBackThroughMercadoPago_AndTheOrderShowsIt()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync(quantity: 2)).Value;
        var payment = shop.Gateway.Approve(started.CheckoutId, 110m, SalesTestFixture.Now.AddMinutes(1));
        await shop.NotifyAsync(payment.Id);
        var order = await shop.Context.Orders.AsNoTracking().SingleAsync();

        var refunded = await shop.RefundAsync(order.Id, 55m);

        Assert.True(refunded.IsSuccess, refunded.Error);
        var online = await shop.Context.OnlinePaymentRefunds.SingleAsync();
        Assert.Equal((payment.Id, 55m, OnlinePaymentRefundReason.ByBusiness, order.Id), (online.PaymentId, online.Amount, online.Reason, online.OrderId));
        Assert.Equal(OnlinePaymentRefundStatus.Requested, Assert.Single(refunded.Value.Refunds).OnlineStatus);
        Assert.Empty(await shop.Context.CashTransactions.ToListAsync());

        await shop.DispatchRefundAsync(online.Id);
        var (paymentId, amount, key) = Assert.Single(shop.Gateway.Refunds);
        Assert.Equal((payment.Id, 55m, online.Id), (paymentId, amount, key));
        var shown = Assert.Single((await shop.OrderAsync(order.Id)).Refunds);
        Assert.Equal((OnlinePaymentRefundStatus.Completed, (string?)null), (shown.OnlineStatus, shown.OnlineFailure));

        // More than what was paid online is refused; Mercado Pago refusing is recorded for the business.
        Assert.False((await shop.RefundAsync(order.Id, 60m)).IsSuccess);
        shop.Gateway.RefundOutcome = new OnlinePaymentRefundOutcome.Refused("Mercado Pago no hizo la devolución: saldo insuficiente");
        var second = await shop.RefundAsync(order.Id, 55m);
        await shop.DispatchRefundAsync((await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync(r => r.Status == OnlinePaymentRefundStatus.Requested)).Id);
        var refusedShown = (await shop.OrderAsync(order.Id)).Refunds.Single(r => r.Id == second.Value.Refunds.OrderBy(x => x.RefundedAt).Last().Id);
        Assert.Equal(OnlinePaymentRefundStatus.Failed, refusedShown.OnlineStatus);
        Assert.Contains("saldo insuficiente", refusedShown.OnlineFailure);
    }

    [Fact]
    public async Task Refund_WithoutTheAccount_ShouldBeRecordedAsRefused_ForTheBusinessToGiveItBack()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync()).Value;
        var first = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));
        var second = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(2));
        await shop.NotifyAsync(first.Id);
        await shop.NotifyAsync(second.Id);
        var refund = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        shop.Context.OnlinePaymentAccounts.RemoveRange(await shop.Context.OnlinePaymentAccounts.ToListAsync());
        await shop.Context.SaveChangesAsync();

        await shop.DispatchRefundAsync(refund.Id);

        var failed = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentRefundStatus.Failed, OnlinePaymentRefundRequestedHandler.AccountGoneMessage), (failed.Status, failed.FailureMessage));
        Assert.Empty(shop.Gateway.Refunds);
    }

    [Fact]
    public async Task CustomerAccount_ThatPaid_ShouldKeepTheOrderInItsOrders()
    {
        await using var shop = await Shop.OpenAsync();
        var customer = new ApplicationUser
        {
            UserName = "mariana@correo.test",
            Email = "mariana@correo.test",
            FirstName = "Mariana",
            LastName = "Ortiz",
            Role = UserRole.Customer,
            IsActive = true
        };
        shop.Context.Users.Add(customer);
        await shop.Context.SaveChangesAsync();
        shop.Fixture.CurrentUser.UserId = customer.Id.ToString();
        var started = (await shop.CheckoutAsync()).Value;
        shop.Fixture.CurrentUser.UserId = Guid.NewGuid().ToString();
        var payment = shop.Gateway.Approve(started.CheckoutId, 55m, SalesTestFixture.Now.AddMinutes(1));

        await shop.NotifyAsync(payment.Id);

        Assert.Equal(customer.Id, (await shop.Context.Orders.SingleAsync()).CustomerUserId);
    }

    [Fact]
    public async Task PaidOnline_ShouldShowApartInTheReports_AndTheInvoiceDeclaresAnIntermediary()
    {
        await using var shop = await Shop.OpenAsync();
        var started = (await shop.CheckoutAsync(quantity: 2)).Value;
        await shop.NotifyAsync(shop.Gateway.Approve(started.CheckoutId, 110m, SalesTestFixture.Now.AddMinutes(1)).Id);
        var order = await shop.Context.Orders.AsNoTracking().SingleAsync();
        await shop.RefundAsync(order.Id, 10m);

        var metrics = await new GetDashboardMetricsQueryHandler(shop.Context, shop.Fixture.TenantService, shop.Clock)
            .Handle(new GetDashboardMetricsQuery(), CancellationToken.None);
        var byChannel = await new Reports.Queries.GetSalesByChannelQueryHandler(shop.Context, shop.Fixture.TenantService, shop.Clock)
            .Handle(new Reports.Queries.GetSalesByChannelQuery(), CancellationToken.None);
        var draft = await new GetOrderCfdiDraftQueryHandler(shop.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = order.Id }, CancellationToken.None);

        Assert.Equal((100m, 100m, 0m), (metrics.TotalRevenue, metrics.OnlineCollected, metrics.PlatformCollected));
        var online = Assert.Single(byChannel.Channels);
        Assert.Equal((OrderChannel.Online, 100m, 100m), (online.Channel, online.NetSales, online.OnlineCollected));
        Assert.Equal(100m, byChannel.Totals.OnlineCollected);
        Assert.Equal("31", draft.Value.PaymentForm);
        Assert.Contains(draft.Value.Warnings, w => w.Contains("Mercado Pago"));
    }

    private static void Reprice(Product product, decimal price)
        => product.Update(
            product.CategoryId, product.Name, product.Description, price, product.PriceCurrency, product.PreparationTimeMinutes,
            product.Sku, product.Barcode, product.SortOrder, product.IsFeatured, product.Tags, product.CompareAtPrice, product.TaxRate,
            product.Calories, product.Allergens, product.MaxQuantityPerOrder, product.Unit, product.AvailableFromTime,
            product.AvailableToTime, product.IsTrackable, product.NotifyCustomer, product.IsTaxExempt);

    /// <summary>A food business with its menu taking orders, an open drawer and, by default, its account connected and on.</summary>
    private sealed class Shop : IAsyncDisposable, ISender
    {
        private Shop(SalesTestFixture fixture, Tenant tenant, Product product)
        {
            Fixture = fixture;
            Tenant = tenant;
            Product = product;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        public Product Product { get; }
        public FakeOnlinePaymentGateway Gateway { get; } = new();
        public FakeSecretProtector Protector { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Shop> OpenAsync(bool connect = true)
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: Slug);
            var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
            settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
            await fixture.OpenCashSessionAsync(tenant);
            var shop = new Shop(fixture, tenant, await fixture.AddProductAsync(tenant, "Latte", 55m));
            if (connect)
                await shop.ConnectAsync(accepts: true);
            return shop;
        }

        public async Task<OnlinePaymentAccount> ConnectAsync(bool accepts)
        {
            var account = OnlinePaymentAccount.Connect(
                Tenant.Id,
                FakeOnlinePaymentGateway.AccountId,
                OnlinePaymentKeys.ProtectAccess(Protector, Tenant.Id, FakeOnlinePaymentGateway.AccessToken),
                OnlinePaymentKeys.ProtectRefresh(Protector, Tenant.Id, FakeOnlinePaymentGateway.RefreshToken),
                SalesTestFixture.Now.AddDays(180),
                liveMode: true,
                Guid.NewGuid(),
                SalesTestFixture.Now);
            account.SetAcceptsPayments(accepts);
            Context.OnlinePaymentAccounts.Add(account);
            await Context.SaveChangesAsync();
            return account;
        }

        public async Task CloseDrawerAsync()
        {
            foreach (var session in await Context.CashSessions.ToListAsync())
                Context.CashSessions.Remove(session);
            await Context.SaveChangesAsync();
        }

        public Task<PublicMenuDto> MenuAsync()
            => new GetPublicMenuQueryHandler(Context, Fixture.TenantService, Clock, Gateway)
                .Handle(new GetPublicMenuQuery(Slug), CancellationToken.None)!;

        public Task<Common.Models.Result<OnlineCheckoutStartedDto>> CheckoutAsync(
            int quantity = 1, decimal? expectedTotal = null, string phone = "5512345678")
            => new CreateOnlineCheckoutCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, new PhoneNormalizationService(), Clock, Gateway, Protector,
                    new Links(), NullLogger<CreateOnlineCheckoutCommandHandler>.Instance)
                .Handle(new CreateOnlineCheckoutCommand
                {
                    Slug = Slug,
                    CustomerName = "Ana",
                    CustomerPhone = phone,
                    Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = quantity }],
                    ExpectedTotal = expectedTotal
                }, CancellationToken.None);

        public Task<OnlineCheckoutStatusDto?> ConfirmAsync(Guid checkoutId, string? paymentId, string? returnKey = null)
            => new ConfirmOnlineCheckoutCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Protector, Clock, this,
                    NullLogger<ConfirmOnlineCheckoutCommandHandler>.Instance)
                .Handle(new ConfirmOnlineCheckoutCommand(Slug, checkoutId, paymentId, returnKey), CancellationToken.None);

        public Task<Common.Models.Result<OnlinePaymentApplication>> NotifyAsync(string paymentId)
            => new ApplyOnlinePaymentNotificationCommandHandler(Context, Gateway, Protector, Clock, this)
                .Handle(new ApplyOnlinePaymentNotificationCommand(paymentId), CancellationToken.None);

        public Task<Common.Models.Result<OnlinePaymentSweep>> SweepAsync()
            => new SweepOnlinePaymentsCommandHandler(
                    Context, Gateway, Protector, Clock, this, NullLogger<SweepOnlinePaymentsCommandHandler>.Instance)
                .Handle(new SweepOnlinePaymentsCommand(), CancellationToken.None);

        public Task<Common.Models.Result<OnlinePaymentConnectionStartDto>> StartConnectionAsync()
            => new StartOnlinePaymentConnectionCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new StartOnlinePaymentConnectionCommand(), CancellationToken.None);

        public Task<Common.Models.Result<OnlinePaymentSettingsDto>> CompleteConnectionAsync(Guid state, string code)
            => new CompleteOnlinePaymentConnectionCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Protector, Clock)
                .Handle(new CompleteOnlinePaymentConnectionCommand(state, code), CancellationToken.None);

        public Task<Common.Models.Result<OnlinePaymentSettingsDto>> DisconnectAsync()
            => new DisconnectOnlinePaymentAccountCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Protector, Clock)
                .Handle(new DisconnectOnlinePaymentAccountCommand(), CancellationToken.None);

        public Task<Common.Models.Result<OnlinePaymentSettingsDto>> AcceptAsync(bool accepts)
            => new SetOnlinePaymentsAcceptedCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Protector, Clock)
                .Handle(new SetOnlinePaymentsAcceptedCommand(accepts), CancellationToken.None);

        public Task<OnlinePaymentSettingsDto> SettingsAsync()
            => new GetOnlinePaymentSettingsQueryHandler(Context, Fixture.TenantService, Gateway, Protector, Clock)
                .Handle(new GetOnlinePaymentSettingsQuery(), CancellationToken.None);

        public Task<Common.Models.Result<Orders.DTOs.OrderDto>> RefundAsync(Guid orderId, decimal amount)
            => new RefundOrderCommandHandler(Context, Fixture.CurrentUser, new OrderCashService(Context, Fixture.CurrentUser), Clock, NoOpOrderInventoryService.Instance)
                .Handle(new RefundOrderCommand { OrderId = orderId, Amount = amount, Method = OrderPaymentMethod.Online, Reason = "Llegó frío", RestockItems = false }, CancellationToken.None);

        public Task<Orders.DTOs.OrderDto> OrderAsync(Guid orderId)
            => new GetOrderByIdQueryHandler(Context).Handle(new GetOrderByIdQuery(orderId), CancellationToken.None);

        public async Task DispatchRefundAsync(Guid refundId)
        {
            await new OnlinePaymentRefundRequestedHandler(Context, Gateway, Protector, Clock, NullLogger<OnlinePaymentRefundRequestedHandler>.Instance)
                .HandleAsync(new OnlinePaymentRefundRequestedEvent(refundId, Tenant.Id),
                    new OutboxEventContext(Guid.NewGuid(), Tenant.Id, null, null, Clock.UtcNow, 1), CancellationToken.None);
            // The dispatcher saves what the handler changed.
            await Context.SaveChangesAsync();
        }

        public Guid StateOf(string authorizationUrl)
            => Guid.ParseExact(System.Web.HttpUtility.ParseQueryString(new Uri(authorizationUrl).Query)["state"]!, "N");

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The applications of payments go to their own handler, as MediatR would send them.
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is not ApplyOnlinePaymentCommand apply)
                throw new NotSupportedException(request.GetType().Name);
            var handler = new ApplyOnlinePaymentCommandHandler(
                Context, Gateway, Clock,
                new CustomerLookupService(Context, new PhoneNormalizationService(), NullLogger<CustomerLookupService>.Instance),
                NoOpOrderInventoryService.Instance, new FolioGenerator(Context), NullLogger<ApplyOnlinePaymentCommandHandler>.Instance);
            return (Task<TResponse>)(object)handler.Handle(apply, cancellationToken);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private sealed class Links : IAppLinks
        {
            public string? BaseUrl => "https://tienditapp.test";
        }
    }
}
