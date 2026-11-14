using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.OnlinePayments.Events;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The deposit that books an appointment online (Fase 31O, ADR 0095): the business's share of the service's price, paid
/// before the request exists; held until the sale of the appointment takes it, it goes back when the business declines,
/// cancels or does not answer in time, or its customer cancels in time, and stays with the business otherwise.
/// </summary>
public class AppointmentDepositTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly string Token = new('a', 43);
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 16, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, "350", null)]
    [InlineData(30, "350", "105")]
    [InlineData(10, "350", "35")]
    [InlineData(100, "350", "350")]
    // To the cent, half away from zero: 33 % of $100.05 is $33.0165.
    [InlineData(33, "100.05", "33.02")]
    // Below $10 none is asked: 10 % of $99.94 is $9.99; of $99.99, $10.00.
    [InlineData(10, "99.94", null)]
    [InlineData(10, "99.99", "10")]
    [InlineData(50, "0", null)]
    public void TheDeposit_ShouldBeTheShareOfTheServicePrice_ToTheCent_AndNoneBelowTenPesos(int? percent, string price, string? deposit)
        => Assert.Equal(deposit is null ? null : decimal.Parse(deposit, System.Globalization.CultureInfo.InvariantCulture),
            BookingDeposits.For(percent, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(9)]
    [InlineData(101)]
    [InlineData(0)]
    public void TheShare_ShouldGoFromTenToOneHundredPercent(int percent)
    {
        Assert.Equal(BookingDeposits.PercentMessage, Assert.Throws<DomainException>(() => BookingDeposits.For(percent, 350m)).Message);
        var settings = new TenantSettings(Business, TenantCapability.Appointments, true);
        Assert.Equal(BookingDeposits.PercentMessage, Assert.Throws<DomainException>(() => settings.SetBookingDepositPercent(percent)).Message);
        Assert.Null(settings.BookingDepositPercent);
    }

    [Fact]
    public void ABusiness_ShouldAskNoDepositByDefault_AndChangeItsShare()
    {
        var settings = new TenantSettings(Business, TenantCapability.Appointments, true);
        Assert.Null(settings.BookingDepositPercent);

        settings.SetBookingDepositPercent(30);
        Assert.Equal(30, settings.BookingDepositPercent);
        settings.SetBookingDepositPercent(null);
        Assert.Null(settings.BookingDepositPercent);
    }

    [Fact]
    public void ARequestPaidOnline_ShouldHoldItsDeposit_AndOneWithoutAsksNone()
    {
        var paid = Request(deposit: 105m);
        Assert.Equal((105m, (AppointmentDepositStatus?)AppointmentDepositStatus.Held, true), (paid.DepositAmount, paid.DepositStatus, paid.HoldsDeposit));
        Assert.Equal(AppointmentStatus.Requested, paid.Status);

        var free = Request();
        Assert.Equal(((decimal?)null, (AppointmentDepositStatus?)null, false), (free.DepositAmount, free.DepositStatus, free.HoldsDeposit));

        foreach (var wrong in new[] { 0m, -1m, 10.001m })
            Assert.Throws<ArgumentOutOfRangeException>(() => Request(deposit: wrong));
    }

    [Fact]
    public void Declined_OrDeclinedAsFake_TheDeposit_ShouldGoBack_Once()
    {
        var declined = Request(deposit: 105m);
        declined.ClearDomainEvents();
        declined.Decline(Now);
        AssertReturned(declined);

        var fake = Request(deposit: 105m);
        fake.ClearDomainEvents();
        fake.MarkFake(Now);
        AssertReturned(fake);
        // Taking the mark back does not ask it again.
        fake.ClearDomainEvents();
        fake.UnmarkFake(Now);
        Assert.DoesNotContain(fake.DomainEvents, e => e is AppointmentDepositReturnedEvent);

        var free = Request();
        free.ClearDomainEvents();
        free.Decline(Now);
        Assert.Null(free.DepositStatus);
        Assert.DoesNotContain(free.DomainEvents, e => e is AppointmentDepositReturnedEvent);
    }

    [Fact]
    public void ItsCustomer_ShouldGetItBack_CancellingARequest_OrADayBefore_OrOneTheBusinessMoved()
    {
        // A request not answered yet, even an hour before.
        var request = Request(Now.AddHours(1), deposit: 105m);
        Assert.True(request.CustomerCancelReturnsDeposit(Now));
        request.ClearDomainEvents();
        request.CancelByCustomer(Now);
        AssertReturned(request);

        // Agendada, exactly a day before.
        var dayBefore = Accepted(Now.AddDays(1));
        Assert.True(dayBefore.CustomerCancelReturnsDeposit(Now));
        dayBefore.CancelByCustomer(Now);
        AssertReturned(dayBefore);

        // Agendada and moved by the business to another time: whenever they cancel.
        var moved = Accepted(Now.AddHours(30));
        moved.Update("Valeria", "+525522107765", "Corte de cabello", Now.AddHours(3), Now.AddHours(4), null, null);
        Assert.True(moved.CustomerCancelReturnsDeposit(Now));
        moved.CancelByCustomer(Now);
        AssertReturned(moved);
    }

    [Fact]
    public void ItsCustomer_CancellingLate_TheBusiness_ShouldKeepIt()
    {
        var late = Accepted(Now.AddDays(1).AddTicks(-1));
        Assert.False(late.CustomerCancelReturnsDeposit(Now));

        late.CancelByCustomer(Now);

        Assert.Equal((AppointmentStatus.Cancelled, (AppointmentDepositStatus?)AppointmentDepositStatus.Kept), (late.Status, late.DepositStatus));
        Assert.DoesNotContain(late.DomainEvents, e => e is AppointmentDepositReturnedEvent);
        // Without a deposit nothing is said either way.
        Assert.False(Request().CustomerCancelReturnsDeposit(Now));
    }

    [Fact]
    public void CancelledByTheBusiness_ItShouldGoBack_AndWhenTheCustomerDidNotCome_TheBusinessKeepsIt()
    {
        var cancelled = Accepted(Now.AddHours(2));
        cancelled.ChangeStatus(AppointmentStatus.Cancelled);
        AssertReturned(cancelled);

        var missed = Accepted(Now.AddHours(2));
        missed.ChangeStatus(AppointmentStatus.NoShow);
        Assert.Equal(AppointmentDepositStatus.Kept, missed.DepositStatus);
        Assert.DoesNotContain(missed.DomainEvents, e => e is AppointmentDepositReturnedEvent);

        // Confirmed, in service or done without a sale, it still waits for the sale.
        var done = Accepted(Now.AddHours(2));
        done.ChangeStatus(AppointmentStatus.Confirmed);
        done.ChangeStatus(AppointmentStatus.Completed);
        Assert.Equal(AppointmentDepositStatus.Held, done.DepositStatus);
    }

    [Fact]
    public void ARequestNotAnsweredByItsTime_ShouldGiveItBack_AndStayARequest()
    {
        var waiting = Request(Now.AddHours(2), deposit: 105m);
        waiting.ClearDomainEvents();

        Assert.False(waiting.ReturnUnansweredDeposit(Now));
        Assert.False(waiting.ReturnUnansweredDeposit(Now.AddHours(2).AddTicks(-1)));
        Assert.True(waiting.ReturnUnansweredDeposit(Now.AddHours(2)));

        AssertReturned(waiting);
        Assert.Equal(AppointmentStatus.Requested, waiting.Status);
        Assert.False(waiting.ReturnUnansweredDeposit(Now.AddHours(3)));
        // Answered, or without a deposit, nothing goes back.
        Assert.False(Accepted(Now.AddHours(2)).ReturnUnansweredDeposit(Now.AddHours(3)));
        Assert.False(Request(Now.AddHours(2)).ReturnUnansweredDeposit(Now.AddHours(3)));
    }

    [Fact]
    public void ItsSale_ShouldTakeTheDeposit()
    {
        var appointment = Accepted(Now.AddHours(2));

        appointment.RecordSale(Guid.NewGuid());

        Assert.Equal((AppointmentStatus.Completed, (AppointmentDepositStatus?)AppointmentDepositStatus.Applied), (appointment.Status, appointment.DepositStatus));
        Assert.Equal(BookingDeposits.NothingToReturnMessage, Assert.Throws<DomainException>(() => appointment.ReturnDeposit()).Message);
    }

    [Fact]
    public void TheBusiness_ShouldGiveBackOneHeldOrKept_NeverOneSoldReturnedOrNone()
    {
        var held = Accepted(Now.AddHours(2));
        held.ReturnDeposit();
        AssertReturned(held);
        Assert.Equal(BookingDeposits.NothingToReturnMessage, Assert.Throws<DomainException>(() => held.ReturnDeposit()).Message);

        var kept = Accepted(Now.AddHours(2));
        kept.ChangeStatus(AppointmentStatus.NoShow);
        kept.ReturnDeposit();
        AssertReturned(kept);

        Assert.Equal(BookingDeposits.NothingToReturnMessage, Assert.Throws<DomainException>(() => Request().ReturnDeposit()).Message);
    }

    [Fact]
    public void ADepositCheckout_ShouldKeepTheRequest_AndBookTheAppointment_Once()
    {
        var checkout = OnlineCheckout.ForAppointmentRequest(Business, 1234L, 105m, "{\"serviceProductId\":1}", null, "+525522107765", Now);

        Assert.Equal((true, false, (Guid?)null, "+525522107765", OnlineCheckoutStatus.AwaitingPayment, Now + OnlineCheckout.TimeToPay),
            (checkout.ForAppointment, checkout.IsForQuote, checkout.AppointmentId, checkout.CustomerPhone, checkout.Status, checkout.ExpiresAt));
        Assert.Throws<InvalidOperationException>(() => checkout.Place("98765", Now.AddMinutes(5), Guid.NewGuid()));

        var appointmentId = Guid.NewGuid();
        checkout.PlaceAppointment(" 98765 ", Now.AddMinutes(5), appointmentId);

        Assert.Equal((OnlineCheckoutStatus.Placed, (Guid?)appointmentId, (Guid?)null, "98765", (string?)null, (string?)null),
            (checkout.Status, checkout.AppointmentId, checkout.OrderId, checkout.PaymentId, checkout.Request, checkout.CustomerPhone));
        Assert.Throws<DomainException>(() => checkout.PlaceAppointment("98766", Now.AddMinutes(6), appointmentId));

        var saleId = Guid.NewGuid();
        checkout.ApplyToSale(saleId);
        Assert.Equal(saleId, checkout.OrderId);
        Assert.Throws<DomainException>(() => checkout.ApplyToSale(Guid.NewGuid()));
    }

    [Fact]
    public void ADepositCheckout_ShouldBePaidInTime_AndOnlyIt_BooksAnAppointment()
    {
        var late = OnlineCheckout.ForAppointmentRequest(Business, 1234L, 105m, "{}", null, "+525522107765", Now);
        Assert.Throws<DomainException>(() => late.PlaceAppointment("98765", late.ExpiresAt.AddTicks(1), Guid.NewGuid()));
        Assert.Throws<DomainException>(() => late.PlaceAppointment("98765", Now, Guid.Empty));
        Assert.Equal(OnlineCheckoutStatus.AwaitingPayment, late.Status);

        var menu = OnlineCheckout.Start(Business, 1234L, 105m, "{}", null, null, Now);
        Assert.False(menu.ForAppointment);
        Assert.Throws<InvalidOperationException>(() => menu.PlaceAppointment("98765", Now, Guid.NewGuid()));
        menu.Place("98765", Now, Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => menu.ApplyToSale(Guid.NewGuid()));
        // Not paid yet, it goes to no sale.
        Assert.Throws<InvalidOperationException>(() => late.ApplyToSale(Guid.NewGuid()));
    }

    [Fact]
    public void TheSale_ShouldTakeTheDepositAsAnOnlinePayment_FirstAndOnce_WhateverItSells()
    {
        var sale = Sale(OrderType.Retail, 350m);

        var payment = sale.RegisterAppointmentDeposit(" 98765 ", 105m, Now, null);

        Assert.Equal((105m, OrderPaymentMethod.Online, "98765"), (payment.Amount, payment.Method, payment.Reference));
        Assert.Equal((105m, 245m, 105m), (sale.PaidAmount, sale.Balance, sale.OnlineRefundable));
        Assert.Equal(Order.AppointmentDepositFirstMessage,
            Assert.Throws<DomainException>(() => sale.RegisterAppointmentDeposit("98766", 10m, Now, null)).Message);
        sale.RegisterPayment(245m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(0m, sale.Balance);

        // After another payment it is refused too.
        var paidFirst = Sale(OrderType.Service, 350m);
        paidFirst.RegisterPayment(10m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(Order.AppointmentDepositFirstMessage,
            Assert.Throws<DomainException>(() => paidFirst.RegisterAppointmentDeposit("98765", 105m, Now, null)).Message);
    }

    [Fact]
    public void ASaleOfLessThanTheDeposit_ShouldBeRefused()
    {
        Assert.Equal(BookingDeposits.AboveSaleMessage,
            Assert.Throws<DomainException>(() => Sale(OrderType.Service, 100m).RegisterAppointmentDeposit("98765", 100.01m, Now, null)).Message);
        Sale(OrderType.Service, 100m).RegisterAppointmentDeposit("98765", 100m, Now, null);
        foreach (var amount in new[] { 0m, -1m, 10.001m })
            Assert.Equal(Order.OnlineDepositAmountMessage,
                Assert.Throws<DomainException>(() => Sale(OrderType.Service, 100m).RegisterAppointmentDeposit("98765", amount, Now, null)).Message);
        Assert.Throws<DomainException>(() => Sale(OrderType.Service, 100m).RegisterAppointmentDeposit(" ", 50m, Now, null));
    }

    [Fact]
    public void TheRefundOfADeposit_ShouldBeAskedFromItsCheckout()
    {
        var checkoutId = Guid.NewGuid();

        var refund = OnlinePaymentRefund.ForAppointmentDeposit(Business, checkoutId, "98765", 105m, Now);

        Assert.Equal((OnlinePaymentRefundReason.AppointmentDeposit, (Guid?)checkoutId, (Guid?)null, 105m, OnlinePaymentRefundStatus.Requested),
            (refund.Reason, refund.CheckoutId, refund.OrderId, refund.Amount, refund.Status));
        Assert.IsType<OnlinePaymentRefundRequestedEvent>(Assert.Single(refund.DomainEvents));
        Assert.Throws<DomainException>(() => OnlinePaymentRefund.ForAppointmentDeposit(Business, Guid.Empty, "98765", 105m, Now));
        Assert.Throws<DomainException>(() => OnlinePaymentRefund.ForCheckout(
            Business, checkoutId, "98765", 105m, OnlinePaymentRefundReason.AppointmentDeposit, Now));
    }

    private static void AssertReturned(Appointment appointment)
    {
        Assert.Equal(AppointmentDepositStatus.Returned, appointment.DepositStatus);
        var returned = Assert.IsType<AppointmentDepositReturnedEvent>(Assert.Single(appointment.DomainEvents, e => e is AppointmentDepositReturnedEvent));
        Assert.Equal((appointment.Id, Business), (returned.AppointmentId, returned.TenantId));
    }

    private static Appointment Request(DateTimeOffset? startAt = null, decimal? deposit = null)
    {
        var start = startAt ?? Now.AddDays(3);
        return Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", start, start.AddHours(1),
            null, null, Guid.NewGuid(), Token, deposit: deposit);
    }

    private static Appointment Accepted(DateTimeOffset startAt)
    {
        var appointment = Request(startAt, deposit: 105m);
        appointment.Accept(Now);
        appointment.ClearDomainEvents();
        return appointment;
    }

    private static Order Sale(OrderType type, decimal price)
    {
        var order = new Order(
            tenantId: Business,
            customerUserId: null,
            temporaryCustomerId: Guid.NewGuid(),
            createdByBusiness: true,
            orderNumber: "ORD-000413",
            businessType: BusinessType.Service,
            orderType: type,
            customerName: "Valeria Ruiz",
            customerPhone: "+525522107765",
            customerNotes: null,
            tableNumber: null,
            pricesIncludeTax: true,
            channel: OrderChannel.Counter);
        order.AddItem(Guid.NewGuid(), "Corte de cabello", 1, price);
        return order;
    }
}
