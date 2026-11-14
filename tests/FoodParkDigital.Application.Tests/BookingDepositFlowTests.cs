using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The deposit to book an appointment online (Fase 31O, ADR 0095): the business asks a share of each service's price; the
/// customer pays it in Mercado Pago before the request exists; the approved payment books the request with the deposit held;
/// declined, cancelled in time or not answered by its time it goes back, and its sale takes it as its «En línea» payment.
/// </summary>
public class BookingDepositFlowTests
{
    // The fixture's now is Sunday 4 October 2026, 11:00 in Mexico City (UTC-6); the business opens Monday to Friday 9 to 14.
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateTimeOffset MondayTen = Local(Monday, "10:00");
    private const string Slug = "barberia";

    private static DateTimeOffset Local(DateOnly date, string time) => new(date.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(-6));

    [Fact]
    public async Task Settings_ShouldAskADeposit_OnlyWithAnAccountThatCollects()
    {
        await using var shop = await Shop.OpenAsync(percent: null, connect: false);

        Assert.False((await shop.SettingsAsync()).DepositAvailable);
        Assert.Equal(BookingDepositOffer.UnavailableMessage, (await shop.UpdateSettingsAsync(30)).Error);

        // A test account takes no real money: not with the platform in its real mode.
        await shop.ConnectAsync(liveMode: false);
        Assert.False((await shop.SettingsAsync()).DepositAvailable);
        shop.Context.OnlinePaymentAccounts.RemoveRange(shop.Context.OnlinePaymentAccounts);
        await shop.Context.SaveChangesAsync();

        // Paying online in the menu is its own switch: the account connected is enough.
        await shop.ConnectAsync();
        var asked = await shop.UpdateSettingsAsync(30);
        Assert.True(asked.IsSuccess, asked.Error);
        Assert.Equal((30, true, 24), (asked.Value!.DepositPercent, asked.Value.DepositAvailable, asked.Value.DepositCancelHours));
        Assert.Contains(await shop.Context.AuditLogs.Select(a => a.Metadata).ToListAsync(), m => m!.Contains("\"BookingDepositPercent\":30"));

        // While the account cannot collect for a while, the share already asked stays and the rest still saves; a new one does not.
        shop.Gateway.IsConfigured = false;
        var saved = await shop.Settings.Handle(new UpdateOnlineBookingSettingsCommand(true, 2, null, 30), CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal((2, (int?)30, false), (saved.Value!.Capacity, saved.Value.DepositPercent, saved.Value.DepositAvailable));
        Assert.Equal(BookingDepositOffer.UnavailableMessage, (await shop.UpdateSettingsAsync(50)).Error);
        // Taking it off always works, and null leaves it as it is.
        Assert.Equal(30, (await shop.Settings.Handle(new UpdateOnlineBookingSettingsCommand(true, 1), CancellationToken.None)).Value!.DepositPercent);
        Assert.Null((await shop.UpdateSettingsAsync(0)).Value!.DepositPercent);

        var validator = new UpdateOnlineBookingSettingsCommandValidator();
        Assert.False(validator.Validate(new UpdateOnlineBookingSettingsCommand(true, 1, null, 5)).IsValid);
        Assert.False(validator.Validate(new UpdateOnlineBookingSettingsCommand(true, 1, null, 101)).IsValid);
        Assert.True(validator.Validate(new UpdateOnlineBookingSettingsCommand(true, 1, null, 0)).IsValid);
        Assert.True(validator.Validate(new UpdateOnlineBookingSettingsCommand(true, 1, null, null)).IsValid);
    }

    [Fact]
    public async Task BookingPage_ShouldSayEachServicesDeposit_AndARequestWithoutPayingIt_ShouldBeRefused()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var cheap = await shop.ServiceAsync("Ceja", 20m);

        var page = (await shop.PageAsync())!;

        Assert.Equal(45m, page.Services.Single(s => s.Id == shop.Haircut.Id).DepositAmount);
        // Under $10 it asks none: booked as before.
        Assert.Null(page.Services.Single(s => s.Id == cheap.Id).DepositAmount);
        Assert.Equal(24, page.DepositCancelHours);
        Assert.Equal(BookingDepositOffer.DepositRequiredMessage, (await shop.RequestAsync(shop.Haircut.Id, MondayTen)).Error);
        Assert.True((await shop.RequestAsync(cheap.Id, MondayTen)).IsSuccess);

        // While the business cannot collect it, nothing is asked and the booking page books as before.
        shop.Gateway.IsConfigured = false;
        Assert.Null((await shop.PageAsync())!.Services.Single(s => s.Id == shop.Haircut.Id).DepositAmount);
        Assert.True((await shop.RequestAsync(shop.Haircut.Id, MondayTen.AddHours(1))).IsSuccess);
        Assert.Equal(BookingDepositOffer.NotOfferedMessage, (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(2))).Error);
        Assert.Empty(await shop.Context.OnlineCheckouts.ToListAsync());
    }

    [Fact]
    public async Task Start_ShouldCheckTheRequestAsItWouldBeBooked_AndSendToMercadoPago_WithoutHoldingTheTime()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);

        var started = await shop.StartAsync(shop.Haircut.Id, MondayTen, expected: 45m);

        Assert.True(started.IsSuccess, started.Error);
        var checkout = await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync();
        Assert.Equal((true, 45m, "+525522107765", OnlineCheckoutStatus.AwaitingPayment, (Guid?)null),
            (checkout.ForAppointment, checkout.Amount, checkout.CustomerPhone, checkout.Status, checkout.AppointmentId));
        var page = Assert.Single(shop.Gateway.Checkouts);
        Assert.Equal(($"https://tienditapp.test/m/{Slug}/cita/pago/{checkout.Id}", 45m), (page.ReturnUrl, page.Amount));
        Assert.Contains("Anticipo de tu cita", page.Title);
        Assert.False(string.IsNullOrEmpty(started.Value!.ReturnKey));
        Assert.Empty(await shop.Context.Appointments.ToListAsync());

        // The page showed another deposit, the time is taken, or a service that asks none: nothing is charged.
        Assert.Equal(BookingDepositOffer.DepositChangedMessage(45m), (await shop.StartAsync(shop.Haircut.Id, MondayTen, expected: 30m)).Error);
        await shop.TeamBooksAsync(MondayTen.AddHours(2));
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(2))).Error);
        var cheap = await shop.ServiceAsync("Ceja", 20m);
        Assert.Equal(BookingDepositOffer.NoDepositMessage, (await shop.StartAsync(cheap.Id, MondayTen.AddHours(3))).Error);
        Assert.Single(shop.Gateway.Checkouts);
        // The same phone with three deposits in course waits.
        await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(1));
        await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(3));
        Assert.Equal(RequestPublicAppointmentCommandHandler.TooManyPendingMessage, (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddMinutes(30))).Error);
    }

    [Fact]
    public async Task ThePhonesRequestsWaiting_AndItsDepositsInCourse_ShouldCountTogether()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var cheap = await shop.ServiceAsync("Ceja", 20m);
        // One request waiting (a service that asks no deposit) and two deposits in course: three, the limit.
        Assert.True((await shop.RequestAsync(cheap.Id, MondayTen)).IsSuccess);
        Assert.True((await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(1))).IsSuccess);
        Assert.True((await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(2))).IsSuccess);

        Assert.Equal(RequestPublicAppointmentCommandHandler.TooManyPendingMessage, (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(3))).Error);
    }

    [Fact]
    public async Task AnApprovedDeposit_ShouldBookTheRequest_WithTheDepositHeld_AndTheBusinessHearsOfIt()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var key = Guid.NewGuid();
        var started = (await shop.StartAsync(shop.Haircut.Id, MondayTen, key: key)).Value!;
        var payment = shop.Gateway.Approve(started.CheckoutId, 45m, SalesTestFixture.Now.AddMinutes(5));
        shop.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(6);

        var status = await shop.ConfirmAsync(started.CheckoutId, payment.Id, started.ReturnKey);

        Assert.Equal(OnlineCheckoutStatus.Placed, status!.Status);
        // In line with the other bookings of the business, as a request of the booking page.
        Assert.Equal([shop.Tenant.Id], shop.BookingLock.Tenants);
        var appointment = await shop.Context.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal((AppointmentStatus.Requested, AppointmentChannel.Online, (decimal?)45m, (AppointmentDepositStatus?)AppointmentDepositStatus.Held, (Guid?)key),
            (appointment.Status, appointment.Channel, appointment.DepositAmount, appointment.DepositStatus, appointment.ClientRequestId));
        Assert.Equal(MondayTen, appointment.StartAt);
        Assert.Equal(appointment.TrackingToken, status.Receipt!.TrackingToken);
        Assert.Equal("Corte de cabello", status.Receipt.ServiceName);
        var checkout = await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync();
        Assert.Equal(((Guid?)appointment.Id, payment.Id, (string?)null), (checkout.AppointmentId, checkout.PaymentId, checkout.Request));

        // Without this browser's return key it says it was booked, but not the link; applying it again changes nothing.
        Assert.Null((await shop.ConfirmAsync(started.CheckoutId, payment.Id, null))!.Receipt);
        Assert.Null((await shop.ConfirmAsync(started.CheckoutId, payment.Id, "otra-llave"))!.Receipt);
        Assert.Equal(OnlinePaymentApplication.AlreadyApplied, await shop.ApplyAsync(started.CheckoutId, payment));
        Assert.Single(await shop.Context.Appointments.ToListAsync());
        // The return page of the menu does not answer for it, and a retry of the request says it was sent.
        Assert.Null(await shop.MenuConfirmAsync(started.CheckoutId));
        Assert.Equal(RequestPublicAppointmentCommandHandler.AlreadySentMessage, (await shop.StartAsync(shop.Haircut.Id, MondayTen, key: key)).Error);
    }

    [Fact]
    public async Task ADepositThatCanNoLongerBook_ShouldGoBack_WithWhy()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var taken = (await shop.StartAsync(shop.Haircut.Id, MondayTen)).Value!;
        var late = (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(1))).Value!;
        var stopped = (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(2))).Value!;
        // The team booked that time while the customer paid.
        await shop.TeamBooksAsync(MondayTen);

        var payment = shop.Gateway.Approve(taken.CheckoutId, 45m, SalesTestFixture.Now.AddMinutes(5));
        var lateOne = shop.Gateway.Approve(late.CheckoutId, 45m, late.ExpiresAt.AddMinutes(1));
        shop.Clock.UtcNow = late.ExpiresAt.AddMinutes(2);
        var refused = await shop.ConfirmAsync(taken.CheckoutId, payment.Id, taken.ReturnKey);
        var paidLate = await shop.ConfirmAsync(late.CheckoutId, lateOne.Id, late.ReturnKey);

        Assert.Equal((OnlineCheckoutStatus.NotPlaced, RequestPublicAppointmentCommandHandler.TimeTakenMessage, (OnlinePaymentRefundStatus?)OnlinePaymentRefundStatus.Requested),
            (refused!.Status, refused.FailureMessage, refused.RefundStatus));
        Assert.Null(refused.Receipt);
        Assert.Equal(ApplyOnlinePaymentCommandHandler.PaidLateMessage, paidLate!.FailureMessage);
        // A payment of another amount is not the deposit, said as one.
        var other = (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(3))).Value!;
        var wrong = shop.Gateway.Approve(other.CheckoutId, 44m, other.ExpiresAt.AddMinutes(-1));
        Assert.Equal(ApplyOnlinePaymentCommandHandler.DepositMismatchMessage,
            (await shop.ConfirmAsync(other.CheckoutId, wrong.Id, other.ReturnKey))!.FailureMessage);
        Assert.Equal(3, await shop.Context.OnlinePaymentRefunds.CountAsync(r => r.Reason == OnlinePaymentRefundReason.NotPlaced));

        // The business stopped booking online while the customer paid.
        var settings = await shop.Context.TenantSettings.SingleAsync();
        settings.UpdateOnlineBooking(false, 1);
        await shop.Context.SaveChangesAsync();
        var third = shop.Gateway.Approve(stopped.CheckoutId, 45m, stopped.ExpiresAt.AddMinutes(-1));
        Assert.Equal(OnlinePaymentApplication.NotPlaced, await shop.ApplyAsync(stopped.CheckoutId, third));
        Assert.Equal(BookingDepositOffer.StoppedBookingMessage, (await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == stopped.CheckoutId)).FailureMessage);
        // Only the team's appointment exists.
        Assert.Equal(AppointmentChannel.Business, (await shop.Context.Appointments.AsNoTracking().SingleAsync()).Channel);
    }

    [Fact]
    public async Task TheFirstTimeOffered_ShouldBeBooked_ByItsPaymentMinutesLater_ButNeverOnceItBegan()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        // At 9:00 the first time offered is 10:00, an hour ahead; the customer pays it in a few minutes.
        shop.Clock.UtcNow = Local(Monday, "09:00");
        var first = (await shop.StartAsync(shop.Haircut.Id, MondayTen)).Value!;
        var eleven = (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(1))).Value!;
        var payment = shop.Gateway.Approve(first.CheckoutId, 45m, Local(Monday, "09:05"));
        shop.Clock.UtcNow = Local(Monday, "09:06");

        // The hour of notice counts from when they went to pay: nobody took 10:00.
        var booked = await shop.ConfirmAsync(first.CheckoutId, payment.Id, first.ReturnKey);
        Assert.Equal(OnlineCheckoutStatus.Placed, booked!.Status);
        Assert.Equal(MondayTen, (await shop.Context.Appointments.AsNoTracking().SingleAsync()).StartAt);

        // Paid in time, but applied once its time began (a notification that came late): it goes back.
        var late = shop.Gateway.Approve(eleven.CheckoutId, 45m, Local(Monday, "09:20"));
        shop.Clock.UtcNow = Local(Monday, "11:01");
        var refused = await shop.ConfirmAsync(eleven.CheckoutId, late.Id, eleven.ReturnKey);
        Assert.Equal((OnlineCheckoutStatus.NotPlaced, RequestPublicAppointmentCommandHandler.TimeTakenMessage),
            (refused!.Status, refused.FailureMessage));
        Assert.Single(await shop.Context.Appointments.ToListAsync());
    }

    [Fact]
    public async Task ADepositPaidFromAnAccount_ShouldKeepTheRequestInIt_WhileItIsActive_AndLeaveItsCartAlone()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var mariana = new ApplicationUser { UserName = "mariana@demo.com", Email = "mariana@demo.com", FirstName = "Mariana", LastName = "Ortiz", Role = UserRole.Customer, IsActive = true };
        var pedro = new ApplicationUser { UserName = "pedro@demo.com", Email = "pedro@demo.com", FirstName = "Pedro", LastName = "Luna", Role = UserRole.Customer, IsActive = true };
        shop.Context.Users.AddRange(mariana, pedro);
        // Mariana left something in the store's cart before booking.
        shop.Context.AccountCarts.Add(AccountCart.Create(mariana.Id, shop.Tenant.Id, [new AccountCartLine(shop.Haircut.Id, [], 1, null)], null, null, SalesTestFixture.Now));
        await shop.Context.SaveChangesAsync();

        shop.Fixture.CurrentUser.UserId = mariana.Id.ToString();
        shop.Fixture.CurrentUser.Role = "CUSTOMER";
        var hers = (await shop.StartAsync(shop.Haircut.Id, MondayTen)).Value!;
        shop.Fixture.CurrentUser.UserId = pedro.Id.ToString();
        var his = (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(1))).Value!;
        // Pedro's account is closed while he pays: the request is booked, but not into an account no longer his.
        (await shop.Context.Users.SingleAsync(u => u.Id == pedro.Id)).IsActive = false;
        await shop.Context.SaveChangesAsync();
        var herPayment = shop.Gateway.Approve(hers.CheckoutId, 45m, SalesTestFixture.Now.AddMinutes(5));
        var hisPayment = shop.Gateway.Approve(his.CheckoutId, 45m, SalesTestFixture.Now.AddMinutes(5));
        shop.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(6);

        Assert.Equal(OnlineCheckoutStatus.Placed, (await shop.ConfirmAsync(his.CheckoutId, hisPayment.Id, his.ReturnKey))!.Status);
        // Back with her session, her return says it is in her account.
        shop.Fixture.CurrentUser.UserId = mariana.Id.ToString();
        Assert.True((await shop.ConfirmAsync(hers.CheckoutId, herPayment.Id, hers.ReturnKey))!.Receipt!.LinkedToAccount);

        var appointments = await shop.Context.Appointments.AsNoTracking().OrderBy(a => a.StartAt).ToListAsync();
        Assert.Equal([(Guid?)mariana.Id, null], appointments.Select(a => a.CustomerUserId));
        // A deposit orders nothing of the menu: her cart stays.
        Assert.Single((await shop.Context.AccountCarts.AsNoTracking().SingleAsync()).Lines);
    }

    [Fact]
    public async Task TwoPaymentsOfTheSameRequest_ShouldBookItOnce_AndGiveTheOtherBack()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        // The same request went to pay twice (its key), each payment for its own checkout.
        var key = Guid.NewGuid();
        var first = (await shop.StartAsync(shop.Haircut.Id, MondayTen, key: key)).Value!;
        var second = (await shop.StartAsync(shop.Haircut.Id, MondayTen.AddHours(1), key: key)).Value!;
        var firstPayment = shop.Gateway.Approve(first.CheckoutId, 45m, SalesTestFixture.Now.AddMinutes(5));
        var secondPayment = shop.Gateway.Approve(second.CheckoutId, 45m, SalesTestFixture.Now.AddMinutes(6));
        shop.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(7);

        Assert.Equal(OnlineCheckoutStatus.Placed, (await shop.ConfirmAsync(first.CheckoutId, firstPayment.Id, first.ReturnKey))!.Status);
        var again = await shop.ConfirmAsync(second.CheckoutId, secondPayment.Id, second.ReturnKey);

        Assert.Equal((OnlineCheckoutStatus.NotPlaced, RequestPublicAppointmentCommandHandler.AlreadySentMessage),
            (again!.Status, again.FailureMessage));
        Assert.Equal(MondayTen, (await shop.Context.Appointments.AsNoTracking().SingleAsync()).StartAt);
        Assert.Equal(secondPayment.Id, (await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).PaymentId);
    }

    [Fact]
    public async Task Declined_TheDeposit_ShouldGoBack_Once_FromItsPayment()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var (appointmentId, paymentId, _) = await shop.BookedAsync(MondayTen);

        var declined = await shop.Requests.Handle(new DeclineAppointmentRequestCommand(appointmentId), CancellationToken.None);

        Assert.Equal(AppointmentDepositStatus.Returned, declined.Value!.DepositStatus);
        await shop.DeliverReturnsAsync();
        await shop.DeliverReturnsAsync();
        var refund = await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentRefundReason.AppointmentDeposit, paymentId, 45m, OnlinePaymentRefundStatus.Requested),
            (refund.Reason, refund.PaymentId, refund.Amount, refund.Status));
        Assert.Equal((await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync()).Id, refund.CheckoutId);

        // Declined as fake, too.
        var (fakeId, _, _) = await shop.BookedAsync(MondayTen.AddHours(1));
        Assert.Equal(AppointmentDepositStatus.Returned,
            (await shop.Requests.Handle(new MarkAppointmentRequestFakeCommand(fakeId), CancellationToken.None)).Value!.DepositStatus);
    }

    [Fact]
    public async Task ItsCustomer_ShouldGetItBack_CancellingInTime_AndTheBusinessKeepsIt_WhenTheyCancelLate()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        // A request not answered yet.
        var (requestId, _, requestToken) = await shop.BookedAsync(MondayTen);
        Assert.True((await shop.PublicAsync(requestToken))!.CancelReturnsDeposit);
        var cancelled = await shop.CancelAsync(requestToken);
        Assert.Equal(AppointmentDepositStatus.Returned, cancelled.Value!.DepositStatus);

        // Agendada for Monday at 10:30: the fixture's now (Sunday 11:00) is 23 and a half hours before.
        var (lateId, _, lateToken) = await shop.BookedAsync(Local(Monday, "10:30"));
        Assert.True((await shop.Requests.Handle(new AcceptAppointmentRequestCommand(lateId), CancellationToken.None)).IsSuccess);
        var view = (await shop.PublicAsync(lateToken))!;
        Assert.Equal((45m, (AppointmentDepositStatus?)AppointmentDepositStatus.Held, false, true),
            (view.DepositAmount!.Value, view.DepositStatus, view.CancelReturnsDeposit, view.CanCancel));
        // A page read earlier said it went back: nothing is cancelled, and the customer reads why.
        Assert.Equal(CustomerCancellation.DepositKeptNowMessage, (await shop.CancelAsync(lateToken, expectDepositBack: true)).Error);
        Assert.Equal(AppointmentDepositStatus.Held, (await shop.Context.Appointments.AsNoTracking().SingleAsync(a => a.Id == lateId)).DepositStatus);
        var late = await shop.CancelAsync(lateToken, expectDepositBack: false);
        Assert.Equal(AppointmentDepositStatus.Kept, late.Value!.DepositStatus);

        // Agendada for Tuesday: more than a day away.
        var (earlyId, _, earlyToken) = await shop.BookedAsync(Local(Monday.AddDays(1), "10:00"));
        Assert.True((await shop.Requests.Handle(new AcceptAppointmentRequestCommand(earlyId), CancellationToken.None)).IsSuccess);
        Assert.Equal(CustomerCancellation.DepositBackNowMessage, (await shop.CancelAsync(earlyToken, expectDepositBack: false)).Error);
        Assert.Equal(AppointmentDepositStatus.Returned, (await shop.CancelAsync(earlyToken, expectDepositBack: true)).Value!.DepositStatus);

        await shop.DeliverReturnsAsync();
        Assert.Equal(2, await shop.Context.OnlinePaymentRefunds.CountAsync(r => r.Reason == OnlinePaymentRefundReason.AppointmentDeposit));
        Assert.NotEqual(requestId, earlyId);
    }

    [Fact]
    public async Task ARequestNotAnsweredByItsTime_ShouldGiveItBack_WithTheSweep()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var (appointmentId, _, _) = await shop.BookedAsync(MondayTen);

        shop.Clock.UtcNow = MondayTen.AddMinutes(-1);
        Assert.Equal(0, (await shop.SweepAsync()).Value!.DepositsReturned);
        shop.Clock.UtcNow = MondayTen;
        Assert.Equal(1, (await shop.SweepAsync()).Value!.DepositsReturned);

        var appointment = await shop.Context.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        Assert.Equal((AppointmentStatus.Requested, (AppointmentDepositStatus?)AppointmentDepositStatus.Returned), (appointment.Status, appointment.DepositStatus));
        await shop.DeliverReturnsAsync();
        Assert.Single(await shop.Context.OnlinePaymentRefunds.ToListAsync());
        Assert.Equal(0, (await shop.SweepAsync()).Value!.DepositsReturned);
    }

    [Fact]
    public async Task CancelledByTheBusiness_ItShouldGoBack_AndWhenTheCustomerDidNotCome_TheBusinessKeepsIt_UntilItGivesItBack()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var (cancelledId, _, _) = await shop.BookedAsync(MondayTen);
        var (missedId, _, _) = await shop.BookedAsync(MondayTen.AddHours(1));
        foreach (var id in new[] { cancelledId, missedId })
            Assert.True((await shop.Requests.Handle(new AcceptAppointmentRequestCommand(id), CancellationToken.None)).IsSuccess);

        var status = new UpdateAppointmentStatusCommandHandler(shop.Context, shop.Notices, clock: shop.Clock);
        Assert.Equal(AppointmentDepositStatus.Returned, (await status.Handle(
            new UpdateAppointmentStatusCommand { AppointmentId = cancelledId, NewStatus = AppointmentStatus.Cancelled }, CancellationToken.None)).Value!.DepositStatus);
        Assert.Equal(AppointmentDepositStatus.Kept, (await status.Handle(
            new UpdateAppointmentStatusCommand { AppointmentId = missedId, NewStatus = AppointmentStatus.NoShow }, CancellationToken.None)).Value!.DepositStatus);

        // The business gives back the one it kept; once is all.
        var returned = await shop.ReturnAsync(missedId);
        Assert.Equal(AppointmentDepositStatus.Returned, returned.Value!.DepositStatus);
        Assert.Contains(await shop.Context.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == "AppointmentDepositReturned");
        await Assert.ThrowsAsync<Domain.Exceptions.DomainException>(() => shop.ReturnAsync(missedId));
        await shop.DeliverReturnsAsync();
        Assert.Equal(2, await shop.Context.OnlinePaymentRefunds.CountAsync());
    }

    [Fact]
    public async Task ItsSale_ShouldTakeTheDepositAsItsOnlinePayment_AndTheCashierChargesTheRest()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var (appointmentId, paymentId, _) = await shop.BookedAsync(MondayTen);
        Assert.True((await shop.Requests.Handle(new AcceptAppointmentRequestCommand(appointmentId), CancellationToken.None)).IsSuccess);
        await shop.Fixture.OpenCashSessionAsync(shop.Tenant);

        // More than what is left after the deposit is refused, and nothing is saved.
        var over = await shop.SellAsync(appointmentId, cash: 105.01m);
        Assert.Equal(CreateOrderCommandValidator.AppointmentDepositPaymentsMessage, over.Error);
        Assert.Equal(AppointmentDepositStatus.Held, (await shop.Context.Appointments.AsNoTracking().SingleAsync()).DepositStatus);

        var sold = await shop.SellAsync(appointmentId, cash: 105m);

        Assert.True(sold.IsSuccess, sold.Error);
        var order = await shop.Context.Orders.Include(o => o.Payments).AsNoTracking().SingleAsync();
        Assert.Equal((150m, 150m), (order.Total, order.PaidAmount));
        Assert.Equal([(OrderPaymentMethod.Online, 45m, (string?)paymentId), (OrderPaymentMethod.Cash, 105m, null)],
            order.Payments.OrderBy(p => p.Method != OrderPaymentMethod.Online).Select(p => (p.Method, p.Amount, p.Reference)));
        var appointment = await shop.Context.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal((AppointmentStatus.Completed, (AppointmentDepositStatus?)AppointmentDepositStatus.Applied, (Guid?)order.Id),
            (appointment.Status, appointment.DepositStatus, appointment.SaleOrderId));
        Assert.Equal(order.Id, (await shop.Context.OnlineCheckouts.AsNoTracking().SingleAsync()).OrderId);
        // Taken by its sale, it is not given back from the appointment.
        await Assert.ThrowsAsync<Domain.Exceptions.DomainException>(() => shop.ReturnAsync(appointmentId));
    }

    [Fact]
    public async Task TheDepositRefundedFromItsSale_ShouldSayHowMercadoPagoHasIt()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var (appointmentId, paymentId, _) = await shop.BookedAsync(MondayTen);
        Assert.True((await shop.Requests.Handle(new AcceptAppointmentRequestCommand(appointmentId), CancellationToken.None)).IsSuccess);
        await shop.Fixture.OpenCashSessionAsync(shop.Tenant);
        var sold = (await shop.SellAsync(appointmentId, cash: 105m)).Value!;

        var refunded = await new RefundOrderCommandHandler(
                shop.Context, shop.Fixture.CurrentUser, new OrderCashService(shop.Context, shop.Fixture.CurrentUser), shop.Clock,
                NoOpOrderInventoryService.Instance)
            .Handle(new RefundOrderCommand { OrderId = sold.Id, Amount = 45m, Method = OrderPaymentMethod.Online, Reason = "No llegó la barbera" },
                CancellationToken.None);

        // A sale at the counter, but its deposit was paid online: its refund says it was asked to Mercado Pago.
        Assert.True(refunded.IsSuccess, refunded.Error);
        var refund = Assert.Single(refunded.Value!.Refunds);
        Assert.Equal((OrderPaymentMethod.Online, (OnlinePaymentRefundStatus?)OnlinePaymentRefundStatus.Requested), (refund.Method, refund.OnlineStatus));
        Assert.Equal(paymentId, (await shop.Context.OnlinePaymentRefunds.AsNoTracking().SingleAsync()).PaymentId);
    }

    [Fact]
    public async Task ASaleThatTookOffADepositGivenBackMeanwhile_ShouldBeRefused_NotSavedShort()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var (appointmentId, _, _) = await shop.BookedAsync(MondayTen);
        Assert.True((await shop.Requests.Handle(new AcceptAppointmentRequestCommand(appointmentId), CancellationToken.None)).IsSuccess);
        await shop.Fixture.OpenCashSessionAsync(shop.Tenant);
        // Vender showed the deposit of $45; meanwhile someone gave it back.
        Assert.True((await shop.ReturnAsync(appointmentId)).IsSuccess);
        shop.Context.ChangeTracker.Clear();

        var sold = await shop.SellAsync(appointmentId, cash: 105m, expectedDeposit: 45m);

        Assert.Equal(CreateOrderCommandValidator.AppointmentDepositChangedMessage, sold.Error);
        Assert.Empty(await shop.Context.Orders.ToListAsync());
        // Read again, the screen charges it whole.
        Assert.True((await shop.SellAsync(appointmentId, cash: 150m, expectedDeposit: 0m)).IsSuccess);
    }

    [Fact]
    public async Task ASaleOfLessThanTheDeposit_ShouldBeRefused()
    {
        await using var shop = await Shop.OpenAsync(percent: 100);
        var (appointmentId, _, _) = await shop.BookedAsync(MondayTen);
        Assert.True((await shop.Requests.Handle(new AcceptAppointmentRequestCommand(appointmentId), CancellationToken.None)).IsSuccess);
        var cheap = await shop.ServiceAsync("Ceja", 20m);

        var sold = await shop.SellAsync(appointmentId, cash: 0m, service: cheap);

        Assert.Equal(BookingDeposits.AboveSaleMessage, sold.Error);
        Assert.Empty(await shop.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task ASaleOfAnAppointmentWhosePaymentIsMissing_ShouldBeRefused_KeepingItsDepositHeld()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        var (appointmentId, _, _) = await shop.BookedAsync(MondayTen);
        Assert.True((await shop.Requests.Handle(new AcceptAppointmentRequestCommand(appointmentId), CancellationToken.None)).IsSuccess);
        // Its deposit says held, but the payment that paid it is not found (fixed by hand in the database, say).
        shop.Context.OnlineCheckouts.Remove(await shop.Context.OnlineCheckouts.SingleAsync());
        await shop.Context.SaveChangesAsync();

        var sold = await shop.SellAsync(appointmentId, cash: 150m);

        // Charging it whole would leave the deposit as taken by a sale that never registered it.
        Assert.Equal(Orders.AppointmentSalePlacement.DepositNotFoundMessage, sold.Error);
        Assert.Empty(await shop.Context.Orders.ToListAsync());
        Assert.Equal(AppointmentDepositStatus.Held, (await shop.Context.Appointments.AsNoTracking().SingleAsync()).DepositStatus);
    }

    [Fact]
    public async Task MercadoPago_ShouldNotGo_WhileADepositIsAskedOrHeld()
    {
        await using var shop = await Shop.OpenAsync(percent: 30);
        Assert.Equal(OnlinePaymentSettings.BookingDepositFirstMessage, (await shop.DisconnectAsync()).Error);

        var (appointmentId, _, _) = await shop.BookedAsync(MondayTen);
        Assert.Null((await shop.UpdateSettingsAsync(0)).Value!.DepositPercent);
        // A deposit held goes back only through the account that took it.
        Assert.Equal(OnlinePaymentSettings.BookingDepositsHeldMessage, (await shop.DisconnectAsync()).Error);

        Assert.True((await shop.Requests.Handle(new DeclineAppointmentRequestCommand(appointmentId), CancellationToken.None)).IsSuccess);
        await shop.DeliverReturnsAsync();
        // The refund in course waits too (as any), and then it goes.
        Assert.Equal(OnlinePaymentSettings.InCourseMessage, (await shop.DisconnectAsync()).Error);
        var refund = await shop.Context.OnlinePaymentRefunds.SingleAsync();
        refund.Complete("refund-1", shop.Clock.UtcNow);
        await shop.Context.SaveChangesAsync();
        Assert.True((await shop.DisconnectAsync()).IsSuccess);
    }

    /// <summary>A barbershop that books online, its Mercado Pago account connected and its share asked.</summary>
    private sealed class Shop : IAsyncDisposable, ISender
    {
        private Shop(SalesTestFixture fixture, Tenant tenant, Product haircut)
        {
            Fixture = fixture;
            Tenant = tenant;
            Haircut = haircut;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        /// <summary>«Corte de cabello», $150, 45 minutes.</summary>
        public Product Haircut { get; }
        public FakeOnlinePaymentGateway Gateway { get; } = new();
        public FakeSecretProtector Protector { get; } = new();
        /// <summary>The businesses whose booking lock the payments took, in order.</summary>
        public RecordingBookingLock BookingLock { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;
        public AppointmentNotices Notices => new(Context, new PhoneNormalizationService(), Clock);
        public OnlineBookingSettingsHandler Settings => new(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock);
        public AppointmentRequestCommandHandler Requests => new(Context, Clock, Notices);

        public static async Task<Shop> OpenAsync(int? percent, bool connect = true)
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(
                BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, pricesIncludeTax: true, slug: Slug);
            var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
            settings.UpdateOnlineBooking(true, 1);
            settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
            settings.SetOpeningHours(OpeningHours.Create(
                new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                    .Select(day => new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(14, 0)))));
            await fixture.Context.SaveChangesAsync();
            var category = new Category(tenant.Id, "Servicios", null);
            fixture.Context.Categories.Add(category);
            var haircut = new Product(tenant.Id, category.Id, "Corte de cabello", null, 150m, "MXN", preparationTimeMinutes: 45, unit: UnitType.Service);
            fixture.Context.Products.Add(haircut);
            await fixture.Context.SaveChangesAsync();

            var shop = new Shop(fixture, tenant, haircut);
            if (connect)
                await shop.ConnectAsync();
            if (percent is not null)
                Assert.True((await shop.UpdateSettingsAsync(percent)).IsSuccess);
            return shop;
        }

        public async Task<Product> ServiceAsync(string name, decimal price)
        {
            var category = await Context.Categories.FirstAsync();
            var product = new Product(Tenant.Id, category.Id, name, null, price, "MXN", preparationTimeMinutes: 30, unit: UnitType.Service);
            Context.Products.Add(product);
            await Context.SaveChangesAsync();
            return product;
        }

        public async Task ConnectAsync(bool liveMode = true)
        {
            Context.OnlinePaymentAccounts.Add(OnlinePaymentAccount.Connect(
                Tenant.Id,
                FakeOnlinePaymentGateway.AccountId,
                OnlinePaymentKeys.ProtectAccess(Protector, Tenant.Id, FakeOnlinePaymentGateway.AccessToken),
                OnlinePaymentKeys.ProtectRefresh(Protector, Tenant.Id, FakeOnlinePaymentGateway.RefreshToken),
                SalesTestFixture.Now.AddDays(180),
                liveMode,
                Guid.NewGuid(),
                SalesTestFixture.Now));
            await Context.SaveChangesAsync();
        }

        public Task<OnlineBookingSettingsDto> SettingsAsync() => Settings.Handle(new GetOnlineBookingSettingsQuery(), CancellationToken.None);

        public Task<Result<OnlineBookingSettingsDto>> UpdateSettingsAsync(int? depositPercent)
            => Settings.Handle(new UpdateOnlineBookingSettingsCommand(true, 1, null, depositPercent), CancellationToken.None);

        public Task<PublicBookingDto?> PageAsync()
            => new GetPublicBookingQueryHandler(Context, Fixture.TenantService, Clock, new PublicDirectoryCache(), NullLogger<GetPublicBookingQueryHandler>.Instance, Gateway)
                .Handle(new GetPublicBookingQuery(Slug), CancellationToken.None);

        public Task<Result<PublicAppointmentReceiptDto>> RequestAsync(Guid serviceId, DateTimeOffset startAt)
            => new RequestPublicAppointmentCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, new PhoneNormalizationService(), Clock, new NoBookingLock(), Notices,
                    gateway: Gateway)
                .Handle(new RequestPublicAppointmentCommand
                {
                    Slug = Slug,
                    ServiceProductId = serviceId,
                    StartAt = startAt,
                    CustomerName = "Valeria Ruiz",
                    CustomerPhone = "5522107765"
                }, CancellationToken.None);

        public Task<Result<OnlineCheckoutStartedDto>> StartAsync(Guid serviceId, DateTimeOffset startAt, decimal? expected = null, Guid? key = null)
            => new StartAppointmentDepositCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, new PhoneNormalizationService(), Clock, Gateway, Protector, new Links(),
                    NullLogger<StartAppointmentDepositCommandHandler>.Instance)
                .Handle(new StartAppointmentDepositCommand
                {
                    Slug = Slug,
                    ServiceProductId = serviceId,
                    StartAt = startAt,
                    CustomerName = "Valeria Ruiz",
                    CustomerPhone = "5522107765",
                    IdempotencyKey = key ?? Guid.NewGuid(),
                    ExpectedDeposit = expected
                }, CancellationToken.None);

        public Task<AppointmentDepositStatusDto?> ConfirmAsync(Guid checkoutId, string? paymentId, string? returnKey)
            => new ConfirmAppointmentDepositCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Protector, Clock, this,
                    NullLogger<ConfirmAppointmentDepositCommandHandler>.Instance)
                .Handle(new ConfirmAppointmentDepositCommand(Slug, checkoutId, paymentId, returnKey), CancellationToken.None);

        public Task<OnlineCheckoutStatusDto?> MenuConfirmAsync(Guid checkoutId)
            => new ConfirmOnlineCheckoutCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Protector, Clock, this,
                    NullLogger<ConfirmOnlineCheckoutCommandHandler>.Instance)
                .Handle(new ConfirmOnlineCheckoutCommand(Slug, checkoutId), CancellationToken.None);

        /// <summary>A request paid and booked for <paramref name="startAt"/>: its id, its payment and its link.</summary>
        public async Task<(Guid AppointmentId, string PaymentId, string Token)> BookedAsync(DateTimeOffset startAt)
        {
            var now = Clock.UtcNow;
            var started = await StartAsync(Haircut.Id, startAt);
            Assert.True(started.IsSuccess, started.Error);
            var payment = Gateway.Approve(started.Value!.CheckoutId, started.Value.Amount, now.AddMinutes(1));
            var status = await ConfirmAsync(started.Value.CheckoutId, payment.Id, started.Value.ReturnKey);
            Assert.Equal(OnlineCheckoutStatus.Placed, status!.Status);
            var checkout = await Context.OnlineCheckouts.AsNoTracking().SingleAsync(c => c.Id == started.Value.CheckoutId);
            return (checkout.AppointmentId!.Value, payment.Id, status.Receipt!.TrackingToken);
        }

        /// <summary>The team books <paramref name="startAt"/> in its agenda.</summary>
        public async Task TeamBooksAsync(DateTimeOffset startAt)
        {
            Context.Appointments.Add(new Appointment(Tenant.Id, "Luis", "+525522107766", "Barba", startAt, startAt.AddHours(1)));
            await Context.SaveChangesAsync();
        }

        public Task<CustomerAppointmentDto?> PublicAsync(string token)
            => new GetPublicAppointmentQueryHandler(Context, Fixture.TenantService, Clock).Handle(new GetPublicAppointmentQuery(token), CancellationToken.None);

        public Task<Result<CustomerAppointmentDto>> CancelAsync(string token, bool? expectDepositBack = null)
            => new CancelPublicAppointmentCommandHandler(Context, Fixture.TenantService, Clock)
                .Handle(new CancelPublicAppointmentCommand(token, expectDepositBack), CancellationToken.None);

        public Task<Result<Appointments.DTOs.AppointmentDto>> ReturnAsync(Guid appointmentId)
            => new ReturnAppointmentDepositCommandHandler(Context, Fixture.CurrentUser).Handle(new ReturnAppointmentDepositCommand(appointmentId), CancellationToken.None);

        /// <summary>The queue delivers the deposits given back since the last time: each asks its refund, once.</summary>
        public async Task DeliverReturnsAsync()
        {
            var handler = new AppointmentDepositReturnedHandler(Context, Clock, NullLogger<AppointmentDepositReturnedHandler>.Instance);
            foreach (var appointment in await Context.Appointments.AsNoTracking().Where(a => a.DepositStatus == AppointmentDepositStatus.Returned).ToListAsync())
            {
                await handler.HandleAsync(new AppointmentDepositReturnedEvent(appointment.Id, appointment.TenantId), null!, CancellationToken.None);
                await Context.SaveChangesAsync();
            }
        }

        public Task<Result<OnlinePaymentSweep>> SweepAsync()
            => new SweepOnlinePaymentsCommandHandler(Context, Gateway, Protector, Clock, this, NullLogger<SweepOnlinePaymentsCommandHandler>.Instance)
                .Handle(new SweepOnlinePaymentsCommand(), CancellationToken.None);

        public Task<Result<OnlinePaymentSettingsDto>> DisconnectAsync()
            => new DisconnectOnlinePaymentAccountCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Protector, Clock)
                .Handle(new DisconnectOnlinePaymentAccountCommand(), CancellationToken.None);

        public async Task<Result<Orders.DTOs.OrderDto>> SellAsync(Guid appointmentId, decimal cash, Product? service = null, decimal? expectedDeposit = null)
        {
            var result = await Fixture.CreateOrderHandler(new OrderInventoryService(Context, Clock, new FolioGenerator(Context), NullLogger<OrderInventoryService>.Instance))
                .Handle(new CreateOrderCommand
                {
                    OrderType = OrderType.Service,
                    CustomerName = "Valeria Ruiz",
                    CreatedByBusiness = true,
                    Items = [new CreateOrderItemDto { ProductId = (service ?? Haircut).Id, Quantity = 1 }],
                    AppointmentId = appointmentId,
                    ExpectedAppointmentDeposit = expectedDeposit,
                    Payments = cash > 0 ? [new CreateOrderPaymentDto { Amount = cash, Method = OrderPaymentMethod.Cash }] : []
                }, CancellationToken.None);
            // A refused sale is never saved: the next one starts clean, as a new request would.
            Context.ChangeTracker.Clear();
            return result;
        }

        /// <summary>The payment, as the notification or the sweep would apply it.</summary>
        public Task<OnlinePaymentApplication> ApplyAsync(Guid checkoutId, OnlinePaymentDetails payment)
            => new OnlinePaymentChecker(Context, Gateway, Protector, Clock, this, NullLogger.Instance).ApplyAsync(checkoutId, payment, CancellationToken.None);

        // The applications of payments go to their own handler, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is not ApplyOnlinePaymentCommand apply)
                throw new NotSupportedException(request.GetType().Name);
            var phones = new PhoneNormalizationService();
            var folios = new FolioGenerator(Context);
            var handler = new ApplyOnlinePaymentCommandHandler(
                Context, Gateway, Clock,
                new CustomerLookupService(Context, phones, NullLogger<CustomerLookupService>.Instance),
                new OrderInventoryService(Context, Clock, folios, NullLogger<OrderInventoryService>.Instance),
                folios, phones, new OrderCashService(Context, Fixture.CurrentUser),
                NullLogger<ApplyOnlinePaymentCommandHandler>.Instance,
                BookingLock);
            return (TResponse)(object)await handler.Handle(apply, cancellationToken);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        private sealed class Links : IAppLinks
        {
            public string? BaseUrl => "https://tienditapp.test";
        }

        private sealed class NoBookingLock : IAppointmentBookingLock
        {
            public Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public sealed class RecordingBookingLock : IAppointmentBookingLock
        {
            public List<Guid> Tenants { get; } = [];

            public Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken)
            {
                Tenants.Add(tenantId);
                return Task.CompletedTask;
            }
        }
    }
}
