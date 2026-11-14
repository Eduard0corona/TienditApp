using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Quotes.Events;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The deposit that accepts a quote from its link (Fase 31N, ADR 0094): the business's share of what is left to pay, fixed
/// when each version is sent; paid online it accepts that version, and it becomes an «En línea» payment of its service order
/// whose rest is paid, and refunded, at the business.
/// </summary>
public class QuoteDepositTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static readonly QuoteCustomer Valeria = new(null, "Valeria Ruiz", "+525522107765", null);

    [Theory]
    [InlineData(null, "1160", null)]
    [InlineData(30, "1160", "348")]
    [InlineData(10, "1160", "116")]
    [InlineData(100, "1160", "1160")]
    // To the cent, half away from zero: 33 % of $100.05 is $33.0165.
    [InlineData(33, "100.05", "33.02")]
    [InlineData(50, "1160.05", "580.03")]
    [InlineData(50, "0.05", null)]
    // Below $10 none is asked: a quote of a few pesos is accepted as before. 10 % of $99.94 is $9.99; of $99.99, $10.00.
    [InlineData(10, "99.94", null)]
    [InlineData(10, "99.99", "10")]
    public void TheDeposit_ShouldBeTheShareOfWhatIsLeftToPay_ToTheCent_AndNoneBelowTenPesos(int? percent, string toPay, string? deposit)
        => Assert.Equal(deposit is null ? null : decimal.Parse(deposit, System.Globalization.CultureInfo.InvariantCulture),
            QuoteRules.DepositFor(percent, decimal.Parse(toPay, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(9)]
    [InlineData(101)]
    [InlineData(0)]
    public void TheShare_ShouldGoFromTenToOneHundredPercent(int percent)
    {
        Assert.Equal(QuoteRules.DepositPercentMessage, Assert.Throws<DomainException>(() => QuoteRules.DepositFor(percent, 1000m)).Message);
        var settings = new TenantSettings(Business, TenantCapability.Service, true);
        Assert.Equal(QuoteRules.DepositPercentMessage, Assert.Throws<DomainException>(() => settings.SetQuoteDepositPercent(percent)).Message);
        Assert.Null(settings.QuoteDepositPercent);
    }

    [Fact]
    public void ABusiness_ShouldAskNoDepositByDefault_AndChangeItsShare()
    {
        var settings = new TenantSettings(Business, TenantCapability.Service, true);
        Assert.Null(settings.QuoteDepositPercent);

        settings.SetQuoteDepositPercent(30);
        Assert.Equal(30, settings.QuoteDepositPercent);
        settings.SetQuoteDepositPercent(null);
        Assert.Null(settings.QuoteDepositPercent);
    }

    [Fact]
    public void AVersion_ShouldKeepTheDepositItWasSentWith()
    {
        var quote = Draft();

        quote.Send(Today, Token, null, Now, deposit: 348m);

        Assert.Equal(348m, quote.DepositAmount);
        Assert.Null(Sent(deposit: null).DepositAmount);
    }

    [Theory]
    [InlineData(9.99)]
    [InlineData(1160.01)]
    [InlineData(100.001)]
    public void AVersion_ShouldBeSentWithADepositFromTenPesosToItsTotal(double deposit)
    {
        var quote = Draft();

        Assert.Equal(QuoteRules.DepositMessage, Assert.Throws<DomainException>(() => quote.Send(Today, Token, null, Now, (decimal)deposit)).Message);
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Null(quote.DepositAmount);
    }

    [Fact]
    public void AVersionWithADeposit_ShouldNotBeAcceptedFromItsLinkWithoutPayingIt_ButRejected()
    {
        var accept = Sent(deposit: 348m);
        Assert.Equal(QuoteRules.DepositRequiredMessage,
            Assert.Throws<DomainException>(() => accept.ReplyFromLink(true, Today, "Sí", Now)).Message);
        Assert.Equal(QuoteStatus.Sent, accept.Status);
        Assert.Empty(accept.DomainEvents);

        var reject = Sent(deposit: 348m);
        Assert.True(reject.ReplyFromLink(false, Today, "Muy caro", Now));
        Assert.Equal(QuoteStatus.Rejected, reject.Status);

        // The business still records an acceptance it got some other way: a deposit paid in person, say.
        var inPerson = Sent(deposit: 348m);
        inPerson.Accept(Today, QuoteReplyChannel.InPerson, "Dejó el anticipo en efectivo", Now);
        Assert.Equal(QuoteStatus.Accepted, inPerson.Status);
    }

    [Fact]
    public void ThePaidDeposit_ShouldAcceptTheVersionFromItsLink_AndTellTheBusiness()
    {
        var quote = Sent(deposit: 348m);

        quote.AcceptWithDeposit(Today, Now);

        Assert.Equal((QuoteStatus.Accepted, (QuoteReplyChannel?)QuoteReplyChannel.Link, (string?)null, (DateTimeOffset?)Now),
            (quote.Status, quote.ReplyChannel, quote.CustomerResponse, quote.ClosedAt));
        var told = Assert.IsType<QuoteAnsweredFromLinkEvent>(Assert.Single(quote.DomainEvents));
        Assert.Equal((quote.Id, Business, true), (told.QuoteId, told.TenantId, told.Accepted));
    }

    [Fact]
    public void APaymentStartedWhileValid_ShouldAcceptIt_EvenIfTheValidityEndedWhileTheyPaid()
    {
        var quote = Sent(deposit: 348m, validityDays: 1);
        var lastDay = Today.AddDays(1);
        Assert.Equal(QuoteStatus.Expired, quote.StatusOn(lastDay.AddDays(1)));

        quote.AcceptWithDeposit(startedOn: lastDay, Now.AddDays(2));

        Assert.Equal(QuoteStatus.Accepted, quote.Status);
        // Started after it expired, it is refused.
        var late = Sent(deposit: 348m, validityDays: 1);
        Assert.Equal(QuoteRules.ExpiredForCustomerMessage,
            Assert.Throws<DomainException>(() => late.AcceptWithDeposit(lastDay.AddDays(1), Now.AddDays(2))).Message);
    }

    [Fact]
    public void ADeposit_ShouldNotAcceptAVersionAnsweredReplacedOrWithoutDeposit_NorTwice()
    {
        var answered = Sent(deposit: 348m);
        answered.Reject(Today, QuoteReplyChannel.WhatsApp, null, Now);
        Assert.Equal(QuoteRules.AnsweredAlreadyMessage, Assert.Throws<DomainException>(() => answered.AcceptWithDeposit(Today, Now)).Message);

        var paid = Sent(deposit: 348m);
        paid.AcceptWithDeposit(Today, Now);
        Assert.Equal(QuoteRules.AnsweredAlreadyMessage, Assert.Throws<DomainException>(() => paid.AcceptWithDeposit(Today, Now)).Message);

        var replaced = Sent(deposit: 348m);
        var correction = replaced.Revise(2, Now);
        correction.Send(Today, new string('b', 43), replaced, Now);
        Assert.Equal(QuoteRules.ReplacedMessage, Assert.Throws<DomainException>(() => replaced.AcceptWithDeposit(Today, Now)).Message);

        var none = Sent(deposit: null);
        Assert.Equal(QuoteRules.NoDepositMessage, Assert.Throws<DomainException>(() => none.AcceptWithDeposit(Today, Now)).Message);
        Assert.Equal(QuoteStatus.Sent, none.Status);
    }

    [Fact]
    public void ADepositCheckout_ShouldBeOfAQuote_WithoutWhatTheCustomerAskedFor()
    {
        var quoteId = Guid.NewGuid();

        var checkout = OnlineCheckout.ForQuote(Business, 1234L, quoteId, 348m, Now);

        Assert.Equal((quoteId, true, (string?)null, (string?)null, 348m, OnlineCheckoutStatus.AwaitingPayment, Now + OnlineCheckout.TimeToPay),
            (checkout.QuoteId, checkout.IsForQuote, checkout.Request, checkout.CustomerPhone, checkout.Amount, checkout.Status, checkout.ExpiresAt));
        var orderId = Guid.NewGuid();
        checkout.Place("98765", Now.AddMinutes(5), orderId);
        Assert.Equal((OnlineCheckoutStatus.Placed, (Guid?)orderId, (Guid?)quoteId), (checkout.Status, checkout.OrderId, checkout.QuoteId));
        Assert.False(OnlineCheckout.Start(Business, 1234L, 100m, "{}", null, null, Now).IsForQuote);

        Assert.Throws<DomainException>(() => OnlineCheckout.ForQuote(Business, 1234L, Guid.Empty, 348m, Now));
        Assert.Throws<DomainException>(() => OnlineCheckout.ForQuote(Business, 0L, quoteId, 348m, Now));
        Assert.Throws<DomainException>(() => OnlineCheckout.ForQuote(Business, 1234L, quoteId, 0m, Now));
        Assert.Throws<DomainException>(() => OnlineCheckout.ForQuote(Business, 1234L, quoteId, 348.001m, Now));
        Assert.Throws<DomainException>(() => OnlineCheckout.ForQuote(Guid.Empty, 1234L, quoteId, 348m, Now));
    }

    [Fact]
    public void TheDeposit_ShouldBeAnOnlinePaymentOfTheServiceOrder_PartOfItsTotal_Once()
    {
        var order = ServiceOrder();

        var payment = order.RegisterOnlineDeposit(" 98765 ", 348m, Now, null);

        Assert.Equal((348m, OrderPaymentMethod.Online, "98765"), (payment.Amount, payment.Method, payment.Reference));
        Assert.Equal((348m, 812m, 348m), (order.PaidAmount, order.Balance, order.OnlineRefundable));
        Assert.Equal(Order.OnlineDepositOnceMessage,
            Assert.Throws<DomainException>(() => order.RegisterOnlineDeposit("98766", 10m, Now, null)).Message);
        // The rest is paid at the business, as in any service order.
        order.RegisterPayment(812m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(0m, order.Balance);
    }

    [Fact]
    public void TheDeposit_ShouldBeOfAServiceOrder_FromACentToWhatIsLeft_NotCancelled()
    {
        Assert.Equal(Order.OnlineDepositOnlyServiceMessage,
            Assert.Throws<DomainException>(() => ServiceOrder(OrderType.Retail).RegisterOnlineDeposit("98765", 100m, Now, null)).Message);
        foreach (var amount in new[] { 0m, -1m, 1160.01m, 10.001m })
            Assert.Equal(Order.OnlineDepositAmountMessage,
                Assert.Throws<DomainException>(() => ServiceOrder().RegisterOnlineDeposit("98765", amount, Now, null)).Message);
        var paidAlready = ServiceOrder();
        paidAlready.RegisterPayment(1000m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(Order.OnlineDepositAmountMessage,
            Assert.Throws<DomainException>(() => paidAlready.RegisterOnlineDeposit("98765", 160.01m, Now, null)).Message);
        paidAlready.RegisterOnlineDeposit("98765", 160m, Now, null);
        Assert.Throws<DomainException>(() => ServiceOrder().RegisterOnlineDeposit(" ", 100m, Now, null));
    }

    [Fact]
    public void WithADepositPaidOnline_TheRest_ShouldGoBackAsItWasPaid_AndTheDepositOnlyOnline()
    {
        var order = ServiceOrder();
        order.RegisterOnlineDeposit("98765", 348m, Now, null);
        order.RegisterPayment(500m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(500m, order.NonOnlineRefundable);

        // What was paid at the business goes back from the business, up to what was paid that way.
        order.RegisterRefund(300m, OrderPaymentMethod.Cash, "Se cambió una pieza", Now, "dueño", null, null, false, []);
        Assert.Equal(200m, order.NonOnlineRefundable);
        Assert.Equal(Order.PaidOnlineRefundMessage, Assert.Throws<DomainException>(() =>
            order.RegisterRefund(200.01m, OrderPaymentMethod.Cash, "Más", Now, "dueño", null, null, false, [])).Message);
        // The deposit goes back only online, and not more than it.
        Assert.Equal(Order.OnlineRefundOnlyOfOnlineMessage, Assert.Throws<DomainException>(() =>
            order.RegisterRefund(348.01m, OrderPaymentMethod.Online, "Más", Now, "dueño", null, null, false, [])).Message);
        order.RegisterRefund(348m, OrderPaymentMethod.Online, "El cliente canceló", Now, "dueño", null, null, false, []);
        order.RegisterRefund(200m, OrderPaymentMethod.Card, "El resto", Now, "dueño", null, null, false, []);
        Assert.Equal((0m, 0m), (order.OnlineRefundable, order.NonOnlineRefundable));
    }

    private const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static Quote Draft(int validityDays = 15)
    {
        var quote = Quote.Start(Business, Folios.QuoteNumber(7), Valeria, null, true, validityDays, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        return quote;
    }

    private static Quote Sent(decimal? deposit, int validityDays = 15)
    {
        var quote = Draft(validityDays);
        quote.Send(Today, Token, null, Now, deposit);
        return quote;
    }

    private static Order ServiceOrder(OrderType type = OrderType.Service)
    {
        var order = new Order(
            tenantId: Business,
            customerUserId: null,
            temporaryCustomerId: Guid.NewGuid(),
            createdByBusiness: true,
            orderNumber: "ORD-000412",
            businessType: BusinessType.Service,
            orderType: type,
            customerName: "Valeria Ruiz",
            customerPhone: "+525522107765",
            customerNotes: null,
            tableNumber: null,
            pricesIncludeTax: true,
            channel: OrderChannel.Counter);
        order.AddItem(Guid.NewGuid(), "Cambio de calentador", 1, 1160m);
        return order;
    }
}
