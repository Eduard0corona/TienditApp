using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Appointments booked online (Fase 24D, ADR 0025): the booking page offers the services and free times of a business
/// that turned it on, a request holds its time «Por confirmar» until the business accepts or declines it, and its customer
/// follows and cancels it with the link or the account.
/// </summary>
public class OnlineBookingFlowTests
{
    // The fixture's now is Sunday 4 October 2026, 11:00 in Mexico City (UTC-6); the business opens Monday to Friday 9 to 14.
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private const string Slug = "barberia";

    private static DateTimeOffset Local(DateOnly date, string time) => new(date.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(-6));

    [Fact]
    public async Task BookingPage_ShouldOfferTheAvailableServices_WithTheirDuration()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        await ServiceAsync(fixture, tenant, "Corte de cabello", 45);
        await ServiceAsync(fixture, tenant, "Barba", null);
        var hidden = await ServiceAsync(fixture, tenant, "Tinte", 90);
        hidden.SetAvailability(false);
        await fixture.AddProductAsync(tenant, "Cera para cabello", 120m, unit: UnitType.Unit);
        await fixture.Context.SaveChangesAsync();

        var page = await new GetPublicBookingQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock, new PublicDirectoryCache(), NullLogger<GetPublicBookingQueryHandler>.Instance)
            .Handle(new GetPublicBookingQuery(Slug), CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal([("Barba", 60), ("Corte de cabello", 45)], page.Services.Select(s => (s.Name, s.DurationMinutes)).OrderBy(s => s.Name));
        Assert.Equal(OnlineBookingRules.DaysAhead, page.DaysAhead);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("menu")]
    [InlineData("hours")]
    [InlineData("module")]
    [InlineData("premises")]
    public async Task BookingPage_ShouldNotExist_UntilTheBusinessHasWhatItNeeds(string missing)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture,
            bookingOn: missing != "off", menuOn: missing != "menu", hours: missing != "hours",
            capabilities: missing == "module" ? TenantCapability.Service : TenantCapability.Service | TenantCapability.Appointments);
        if (missing == "premises")
        {
            var branch = await fixture.DefaultBranchAsync();
            branch.Update(branch.Name, branch.Code, new Domain.Branches.Entities.BranchContact(), Domain.Branches.Enums.BranchServiceMode.DeliveryOnly);
            await fixture.Context.SaveChangesAsync();
        }
        await ServiceAsync(fixture, tenant, "Corte de cabello", 45);

        var page = await new GetPublicBookingQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock, new PublicDirectoryCache(), NullLogger<GetPublicBookingQueryHandler>.Instance)
            .Handle(new GetPublicBookingQuery(Slug), CancellationToken.None);
        var request = await RequestAsync(fixture, (await fixture.Context.Products.SingleAsync()).Id, Local(Monday, "10:00"));

        Assert.Null(page);
        Assert.Equal(OnlineBookingAvailability.NotBookingMessage, request.Error);
    }

    [Fact]
    public async Task Slots_ShouldBeAWeekOfFreeTimes_FromAnHourFromNow()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        fixture.Context.Appointments.Add(new Appointment(tenant.Id, "Ana", "5512345678", "Barba", Local(Monday, "10:00"), Local(Monday, "11:00")));
        await fixture.Context.SaveChangesAsync();

        var week = await SlotsAsync(fixture, haircut.Id);

        Assert.NotNull(week);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(new DateOnly(2026, 10, 4), week.Days[0].Date);
        Assert.Equal("domingo 4 de octubre", week.Days[0].Label);
        Assert.Empty(week.Days[0].Slots);
        Assert.Equal(
            ["9:00 a. m.", "11:00 a. m.", "11:30 a. m.", "12:00 p. m.", "12:30 p. m.", "1:00 p. m."],
            week.Days[1].Slots.Select(s => s.Label));
        Assert.Equal(Local(Monday, "09:00"), week.Days[1].Slots[0].StartAt);
        Assert.Equal(new DateOnly(2026, 10, 11), week.NextFrom);
    }

    [Fact]
    public async Task Slots_ShouldLeaveTheTripOfAVisitFree_WhenNobodyIsSaid()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var zone = new Domain.Delivery.DeliveryZone(tenant.Id, new Domain.Delivery.DeliveryZoneFields("Centro", 50m, 0m, null, ["72000"]));
        fixture.Context.DeliveryZones.Add(zone);
        // A visit at a customer's home from 10 to 11, with the business's half hour of trip on each side (Fase 29B).
        var visit = new Appointment(tenant.Id, "Ana", "5512345678", "Corte a domicilio", Local(Monday, "10:00"), Local(Monday, "11:00"));
        visit.ArrangeVisit(Domain.Orders.HomeDelivery.DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone, SalesTestFixture.Now);
        fixture.Context.Appointments.Add(visit);
        await fixture.Context.SaveChangesAsync();

        var week = await SlotsAsync(fixture, haircut.Id);
        var taken = await RequestAsync(fixture, haircut.Id, Local(Monday, "11:00"));

        Assert.Equal(["11:30 a. m.", "12:00 p. m.", "12:30 p. m.", "1:00 p. m."], week!.Days[1].Slots.Select(s => s.Label));
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, taken.Error);
    }

    [Fact]
    public async Task Slots_ShouldStopAtTheLastDayItBooks_AndRefuseAServiceItDoesNotBook()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var wax = await fixture.AddProductAsync(tenant, "Cera para cabello", 120m, unit: UnitType.Unit);

        var lastWeek = await SlotsAsync(fixture, haircut.Id, new DateOnly(2026, 10, 30));
        var past = await SlotsAsync(fixture, haircut.Id, new DateOnly(2026, 11, 3));

        Assert.Equal([new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2)],
            lastWeek!.Days.Select(d => d.Date));
        Assert.Null(lastWeek.NextFrom);
        Assert.Empty(past!.Days);
        Assert.Null(await SlotsAsync(fixture, wax.Id));
    }

    [Fact]
    public async Task Request_ShouldHoldTheTime_PorConfirmar_AndTellTheCustomer()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 45);

        var result = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), notes: "Primera vez");

        Assert.True(result.IsSuccess, result.Error);
        var receipt = result.Value!;
        Assert.Equal(("Corte de cabello", "lunes 5 de octubre", "10:00 a. m.", false), (receipt.ServiceName, receipt.DateLabel, receipt.TimeLabel, receipt.LinkedToAccount));
        Assert.True(Domain.Orders.Entities.Order.IsTrackingToken(receipt.TrackingToken));
        var stored = await fixture.Context.Appointments.SingleAsync();
        Assert.Equal((AppointmentStatus.Requested, AppointmentChannel.Online, "+525522107765", Local(Monday, "10:45")),
            (stored.Status, stored.Channel, stored.CustomerPhone, stored.EndAt));
        var notice = await fixture.Context.OutgoingMessages.SingleAsync(m => m.Purpose == MessagePurpose.AppointmentNotice);
        Assert.Contains("recibimos tu solicitud de cita de Corte de cabello el lunes 5 de octubre a las 10:00 a. m.", notice.Body);
    }

    [Fact]
    public async Task ARequestFromTheTeam_ShouldBeKeptAsSuch_ByItsPhone_OrWhoIsSignedIn()
    {
        // Fase 29M (ADR 0067): decided when it arrives, whatever way each phone was written.
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 45);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        tenant.Update(tenant.Name, tenant.ContactEmail, "(55) 2222-3333", null);
        fixture.Context.Branches.Add(new Branch(tenant.Id, "Norte", "NORTE", new BranchContact(ContactPhone: "55 4444 5555")));
        var member = await MemberAsync(fixture, tenant.Id, "55 1234 5678", TenantUserStatus.Active);
        await MemberAsync(fixture, tenant.Id, "+52 55 8765 4321", TenantUserStatus.Disabled);
        await MemberAsync(fixture, tenant.Id, "55 7777 8888", TenantUserStatus.Invited);
        // As a mobile is saved when its owner confirms it by WhatsApp: with the 1.
        await MemberAsync(fixture, tenant.Id, "+5215566667777", TenantUserStatus.Active);
        var elsewhere = await MemberAsync(fixture, Guid.NewGuid(), null, TenantUserStatus.Active);
        await fixture.Context.SaveChangesAsync();

        async Task<bool> FromTeamAsync(string phone, DateOnly day, string at, Guid? signedIn = null)
        {
            fixture.CurrentUser.UserId = (signedIn ?? Guid.NewGuid()).ToString();
            var result = await RequestAsync(fixture, haircut.Id, Local(day, at), phone);
            Assert.True(result.IsSuccess, result.Error);
            return (await fixture.Context.Appointments.AsNoTracking().SingleAsync(a => a.TrackingToken == result.Value!.TrackingToken)).FromTeam;
        }

        // A member, the business, a branch and its WhatsApp; and a member signed in with any phone.
        Assert.True(await FromTeamAsync("5512345678", Monday, "09:00"));
        Assert.True(await FromTeamAsync("55 2222 3333", Monday, "10:00"));
        Assert.True(await FromTeamAsync("+52 55 4444 5555", Monday, "11:00"));
        Assert.True(await FromTeamAsync("5500000000", Monday, "12:00"));
        Assert.True(await FromTeamAsync("5599990000", Monday, "13:00", member));
        // A customer, someone who left the team, and a member of another business.
        var tuesday = Monday.AddDays(1);
        Assert.False(await FromTeamAsync("5511112222", tuesday, "09:00"));
        Assert.False(await FromTeamAsync("5587654321", tuesday, "10:00"));
        Assert.False(await FromTeamAsync("5533334444", tuesday, "11:00", elsewhere));
        // Someone invited to the team, and a mobile saved with the 1, are of the team too.
        Assert.True(await FromTeamAsync("5577778888", tuesday, "12:00"));
        Assert.True(await FromTeamAsync("5566667777", tuesday, "13:00"));
    }

    [Fact]
    public async Task Request_ShouldRefuse_ATimeTakenMeanwhile_OrOneNotOffered()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        Assert.True((await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"))).IsSuccess);

        var taken = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:30"), phone: "5511112222");
        var outOfStep = await RequestAsync(fixture, haircut.Id, Local(Monday, "11:15"), phone: "5511112222");
        var closed = await RequestAsync(fixture, haircut.Id, Local(Monday.AddDays(-1), "12:00"), phone: "5511112222");
        var tooFar = await RequestAsync(fixture, haircut.Id, Local(Monday.AddDays(40), "10:00"), phone: "5511112222");

        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, taken.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, outOfStep.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, closed.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TooFarMessage, tooFar.Error);
        Assert.Equal(1, await fixture.Context.Appointments.CountAsync());
    }

    [Fact]
    public async Task Request_ShouldTakeTheTime_WhileFewerThanTheBusinessAttendsAtOnce()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, capacity: 2);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);

        var first = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        var second = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), phone: "5511112222");
        var third = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), phone: "5533334444");

        Assert.True(first.IsSuccess && second.IsSuccess, first.Error ?? second.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, third.Error);
    }

    [Fact]
    public async Task SameRequestAgain_ShouldAnswerTheOneStored_NotASecond()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var key = Guid.NewGuid();

        var first = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), key: key);
        var again = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), key: key);
        var otherPhone = await RequestAsync(fixture, haircut.Id, Local(Monday, "11:00"), key: key, phone: "5511112222");

        Assert.Equal(first.Value!.TrackingToken, again.Value!.TrackingToken);
        Assert.Equal(RequestPublicAppointmentCommandHandler.AlreadySentMessage, otherPhone.Error);
        Assert.Equal(1, await fixture.Context.Appointments.CountAsync());
    }

    [Fact]
    public async Task Phone_ShouldNotPileUpRequests_WhileTheBusinessDecidesThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, capacity: 5);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        foreach (var time in new[] { "09:00", "10:00", "11:00" })
            Assert.True((await RequestAsync(fixture, haircut.Id, Local(Monday, time))).IsSuccess);

        var fourth = await RequestAsync(fixture, haircut.Id, Local(Monday, "12:00"));

        Assert.Equal(RequestPublicAppointmentCommandHandler.TooManyPendingMessage, fourth.Error);
    }

    [Fact]
    public async Task Phone_ShouldNotAskAndCancelOverAndOver_ForTheCancellationsOfTheLastDayCountToo()
    {
        // Fase 29P (ADR 0070): each request and each cancellation tells the business.
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, capacity: 5);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        foreach (var time in new[] { "09:00", "10:00" })
        {
            Assert.True((await RequestAsync(fixture, haircut.Id, Local(Monday, time))).IsSuccess);
            await CancelLastAsync(fixture);
        }
        // One the business declined does not count: it was not its customer who cancelled it.
        Assert.True((await RequestAsync(fixture, haircut.Id, Local(Monday, "11:00"))).IsSuccess);
        var declined = await fixture.Context.Appointments.OrderByDescending(a => a.StartAt).FirstAsync();
        declined.Decline(fixture.Clock.UtcNow);
        await fixture.Context.SaveChangesAsync();
        // Two cancelled and one waiting: a fourth is refused, with the same words as three waiting.
        Assert.True((await RequestAsync(fixture, haircut.Id, Local(Monday, "12:00"))).IsSuccess);
        var fourth = await RequestAsync(fixture, haircut.Id, Local(Monday, "13:00"));
        // The same mobile written with the 1 WhatsApp adds is the same phone.
        var withTheOne = await RequestAsync(fixture, haircut.Id, Local(Monday, "13:00"), phone: "+5215522107765");
        // Another phone is not held back by them: two of its own waiting, and one more fits.
        foreach (var time in new[] { "09:00", "10:00", "11:00" })
        {
            var ofAnother = await RequestAsync(fixture, haircut.Id, Local(Monday, time), phone: "5511112222");
            Assert.True(ofAnother.IsSuccess, $"{time}: {ofAnother.Error}");
        }

        Assert.Equal(RequestPublicAppointmentCommandHandler.TooManyPendingMessage, fourth.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TooManyPendingMessage, withTheOne.Error);

        // A day after they were cancelled they no longer count: with the one still waiting, one more fits.
        fixture.Clock.UtcNow = SalesTestFixture.Now + OnlineBookingRules.CancelledCountFor - TimeSpan.FromHours(2);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TooManyPendingMessage,
            (await RequestAsync(fixture, haircut.Id, Local(Monday, "13:00"))).Error);
        fixture.Clock.UtcNow = SalesTestFixture.Now + OnlineBookingRules.CancelledCountFor + TimeSpan.FromMinutes(1);
        var later = await RequestAsync(fixture, haircut.Id, Local(Monday, "13:00"));
        Assert.True(later.IsSuccess, later.Error);
    }

    [Fact]
    public async Task Accept_ShouldScheduleTheRequest_AndDecline_ShouldFreeItsTime_EachTellingTheCustomer()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        await RequestAsync(fixture, haircut.Id, Local(Monday, "12:00"), phone: "5511112222");
        var requests = await fixture.Context.Appointments.OrderBy(a => a.StartAt).Select(a => a.Id).ToListAsync();

        var accepted = await RequestHandler(fixture).Handle(new AcceptAppointmentRequestCommand(requests[0]), CancellationToken.None);
        var declined = await RequestHandler(fixture).Handle(new DeclineAppointmentRequestCommand(requests[1]), CancellationToken.None);

        Assert.Equal(AppointmentStatus.Scheduled, accepted.Value!.Status);
        Assert.Equal(AppointmentStatus.Cancelled, declined.Value!.Status);
        var notices = await fixture.Context.OutgoingMessages.Where(m => m.Purpose == MessagePurpose.AppointmentNotice).Select(m => m.Body).ToListAsync();
        Assert.Contains(notices, body => body.Contains("quedó agendada tu cita de Corte de cabello el lunes 5 de octubre a las 10:00 a. m."));
        Assert.Contains(notices, body => body.Contains("no pudimos confirmar tu cita de Corte de cabello del lunes 5 de octubre a las 12:00 p. m."));
        // The declined time is offered again.
        Assert.Contains(Local(Monday, "12:00"), (await SlotsAsync(fixture, haircut.Id))!.Days[1].Slots.Select(s => s.StartAt));
    }

    [Fact]
    public async Task MovingARequest_ShouldKeepItWaiting_AndTellTheCustomerItIsStillARequest()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        var request = await fixture.Context.Appointments.SingleAsync();

        var moved = await new UpdateAppointmentCommandHandler(fixture.Context, fixture.CurrentUser, fixture.Clock, Notices(fixture))
            .Handle(new UpdateAppointmentCommand
            {
                AppointmentId = request.Id,
                CustomerName = request.CustomerName,
                CustomerPhone = request.CustomerPhone,
                ServiceProductId = haircut.Id,
                StartAt = Local(Monday, "12:00"),
                EndAt = Local(Monday, "13:00")
            }, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Requested, moved.Value!.Status);
        var notices = await fixture.Context.OutgoingMessages.Where(m => m.Purpose == MessagePurpose.AppointmentNotice).Select(m => m.Body).ToListAsync();
        Assert.Contains(notices, body => body.Contains(
            "tu solicitud de cita de Corte de cabello ahora es para el lunes 5 de octubre a las 12:00 p. m.")
            && body.Contains("Te avisamos en cuanto la confirmen."));
    }

    [Fact]
    public async Task Accept_ShouldTake_ARequestWhoseTimeBegan_WithoutTellingTheCustomerItIsAgendada()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        fixture.Clock.UtcNow = Local(Monday, "10:30");

        var accepted = await RequestHandler(fixture)
            .Handle(new AcceptAppointmentRequestCommand(fixture.Context.Appointments.Single().Id), CancellationToken.None);

        Assert.Equal(AppointmentStatus.Scheduled, accepted.Value!.Status);
        // Only the notice of the request: the customer is already there.
        Assert.Equal(1, await fixture.Context.OutgoingMessages.CountAsync(m => m.Purpose == MessagePurpose.AppointmentNotice));
    }

    [Fact]
    public async Task Accept_ShouldRefuse_ARequestThatEnded()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        fixture.Clock.UtcNow = Local(Monday, "11:00");

        var error = await Assert.ThrowsAsync<DomainException>(() => RequestHandler(fixture)
            .Handle(new AcceptAppointmentRequestCommand(fixture.Context.Appointments.Single().Id), CancellationToken.None));

        Assert.Equal(OnlineBookingRules.RequestTimePassedMessage, error.Message);
    }

    [Fact]
    public async Task Request_ShouldRefuse_ADayAfterTheLastOneOffered()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);

        // The fixture's today is Sunday 4 October: the last day offered is Monday 2 November.
        var lastDay = await RequestAsync(fixture, haircut.Id, Local(new DateOnly(2026, 11, 2), "10:00"));
        var dayAfter = await RequestAsync(fixture, haircut.Id, Local(new DateOnly(2026, 11, 3), "10:00"), phone: "5511112222");

        Assert.True(lastDay.IsSuccess, lastDay.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TooFarMessage, dayAfter.Error);
    }

    [Fact]
    public async Task Slots_AfterMidnight_ShouldSayTheirOwnDate()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, hours: false);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.SetOpeningHours(OpeningHours.Create([new OpeningInterval(DayOfWeek.Monday, new TimeOnly(22, 0), new TimeOnly(2, 0))]));
        await fixture.Context.SaveChangesAsync();
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);

        var monday = (await SlotsAsync(fixture, haircut.Id))!.Days[1];

        Assert.Equal(["10:00 p. m.", "10:30 p. m.", "11:00 p. m.", "11:30 p. m.", "12:00 a. m.", "12:30 a. m.", "1:00 a. m."],
            monday.Slots.Select(s => s.Label));
        var afterMidnight = monday.Slots[5];
        Assert.Equal((new DateOnly(2026, 10, 6), "martes 6 de octubre"), (afterMidnight.Date, afterMidnight.DateLabel));
        Assert.Equal((Monday, "lunes 5 de octubre"), (monday.Slots[0].Date, monday.Slots[0].DateLabel));
    }

    [Fact]
    public async Task ChangingTheStatusOfARequest_ShouldAcceptOrDeclineIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        await RequestAsync(fixture, haircut.Id, Local(Monday, "12:00"), phone: "5511112222");
        var requests = await fixture.Context.Appointments.OrderBy(a => a.StartAt).Select(a => a.Id).ToListAsync();
        var status = new UpdateAppointmentStatusCommandHandler(fixture.Context, Notices(fixture), clock: fixture.Clock);

        var scheduled = await status.Handle(new UpdateAppointmentStatusCommand { AppointmentId = requests[0], NewStatus = AppointmentStatus.Scheduled }, CancellationToken.None);
        var cancelled = await status.Handle(new UpdateAppointmentStatusCommand { AppointmentId = requests[1], NewStatus = AppointmentStatus.Cancelled }, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Scheduled, scheduled.Value!.Status);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Value!.Status);
    }

    [Fact]
    public async Task Link_ShouldShowTheAppointment_AndCancelIt_BeforeItStarts()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var token = (await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"))).Value!.TrackingToken;
        fixture.TenantService.SetTenant(Guid.Empty);

        var seen = await new GetPublicAppointmentQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicAppointmentQuery(token), CancellationToken.None);
        fixture.TenantService.SetTenant(Guid.Empty);
        var cancelled = await new CancelPublicAppointmentCommandHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new CancelPublicAppointmentCommand(token), CancellationToken.None);

        Assert.Equal(("Por confirmar", true, "Negocio barberia"), (seen!.StatusLabel, seen.CanCancel, seen.BusinessName));
        Assert.Equal((AppointmentStatus.Cancelled, "La cancelaste", false), (cancelled.Value!.Status, cancelled.Value.StatusLabel, cancelled.Value.CanCancel));
        Assert.NotNull((await fixture.Context.Appointments.SingleAsync()).CancelledByCustomerAt);
    }

    [Fact]
    public async Task Link_ShouldFindNothing_ForAnUnknownOrChangedLink()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var token = (await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"))).Value!.TrackingToken;
        var changed = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        var unknown = await new GetPublicAppointmentQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicAppointmentQuery(changed), CancellationToken.None);
        var cancel = await new CancelPublicAppointmentCommandHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new CancelPublicAppointmentCommand("no-es-un-enlace"), CancellationToken.None);

        Assert.Null(unknown);
        Assert.Equal(CustomerAppointments.NotFoundMessage, cancel.Error);
        Assert.Equal(AppointmentStatus.Requested, (await fixture.Context.Appointments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Account_ShouldSeeItsAppointments_InEveryBusiness_AndCancelOnlyItsOwn()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var mine = await CustomerAsync(fixture, "Valeria");
        fixture.CurrentUser.UserId = mine.ToString();
        var receipt = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        fixture.CurrentUser.UserId = Guid.NewGuid().ToString();
        await RequestAsync(fixture, haircut.Id, Local(Monday, "12:00"), phone: "5511112222");

        fixture.CurrentUser.UserId = mine.ToString();
        fixture.TenantService.SetTenant(Guid.Empty);
        var upcoming = await new GetMyAppointmentsQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
            .Handle(new GetMyAppointmentsQuery(), CancellationToken.None);
        var stranger = await fixture.Context.Appointments.IgnoreQueryFilters().SingleAsync(a => a.CustomerUserId == null);

        Assert.True(receipt.Value!.LinkedToAccount);
        Assert.Equal(["Corte de cabello"], upcoming.Items.Select(a => a.ServiceName));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => CancelMineAsync(fixture, stranger.Id));
        var cancelled = await CancelMineAsync(fixture, upcoming.Items[0].Id);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Value!.Status);
        fixture.TenantService.SetTenant(Guid.Empty);
        var past = await new GetMyAppointmentsQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
            .Handle(new GetMyAppointmentsQuery { Scope = MyAppointmentsScope.Past }, CancellationToken.None);
        Assert.Equal(["La cancelaste"], past.Items.Select(a => a.StatusLabel));
    }

    [Fact]
    public async Task Account_ShouldRead_ARequestNobodyDecided_AsNotConfirmed_OnceItsTimeBegan()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        fixture.CurrentUser.UserId = (await CustomerAsync(fixture, "Valeria")).ToString();
        await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        fixture.Clock.UtcNow = Local(Monday, "10:30");

        fixture.TenantService.SetTenant(Guid.Empty);
        var past = await new GetMyAppointmentsQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
            .Handle(new GetMyAppointmentsQuery { Scope = MyAppointmentsScope.Past }, CancellationToken.None);

        var request = Assert.Single(past.Items);
        Assert.Equal(("No se confirmó", false), (request.StatusLabel, request.CanCancel));
    }

    [Fact]
    public async Task OwnerSettings_ShouldSayWhatIsMissing_AndRecordTheChange()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, bookingOn: false, menuOn: false, hours: false);
        var handler = new OnlineBookingSettingsHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser);

        var updated = await handler.Handle(new UpdateOnlineBookingSettingsCommand(true, 3), CancellationToken.None);

        Assert.Equal((true, 3, false), (updated.Value!.Enabled, updated.Value.Capacity, updated.Value.BooksOnline));
        Assert.Equal(
            [OnlineBookingAvailability.Describe(OnlineBookingBlocker.MenuOff), OnlineBookingAvailability.Describe(OnlineBookingBlocker.NoWeeklyHours)],
            updated.Value.Missing);
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "OnlineBookingUpdated"));
        Assert.False(new UpdateOnlineBookingSettingsCommandValidator().Validate(new UpdateOnlineBookingSettingsCommand(true, 0)).IsValid);
    }

    [Fact]
    public async Task Attention_ShouldCountTheRequestsAhead_TodaysAsUrgent()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"));
        // It arrived now, as the API stamps it: to answer in time by Monday at 10:00, not today (Fase 29L).
        (await fixture.Context.Appointments.SingleAsync()).CreatedAt = fixture.Clock.UtcNow;
        await fixture.Context.SaveChangesAsync();

        var attention = await new GetAttentionQueryHandler(
                fixture.Context, new Application.Common.Security.CurrentAccessContext(fixture.CurrentUser, fixture.TenantService, new OwnerAccess()),
                fixture.CurrentUser, fixture.Clock)
            .Handle(new GetAttentionQuery(), CancellationToken.None);

        var requests = Assert.Single(attention.Items, i => i.Key == AttentionKeys.AppointmentRequests);
        Assert.Equal((1, 0), (requests.Count, requests.UrgentCount));
    }

    private static async Task<Tenant> BusinessAsync(
        SalesTestFixture fixture,
        bool bookingOn = true,
        bool menuOn = true,
        bool hours = true,
        int capacity = 1,
        TenantCapability capabilities = TenantCapability.Service | TenantCapability.Appointments)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, capabilities, slug: Slug);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineBooking(bookingOn, capacity);
        if (menuOn)
            settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        if (hours)
            settings.SetOpeningHours(OpeningHours.Create(
                new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                    .Select(day => new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(14, 0)))));
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Product> ServiceAsync(SalesTestFixture fixture, Tenant tenant, string name, int? minutes)
    {
        var category = await fixture.Context.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
        if (category is null)
        {
            category = new Category(tenant.Id, "Servicios", null);
            fixture.Context.Categories.Add(category);
        }

        var product = new Product(tenant.Id, category.Id, name, null, 150m, "MXN", preparationTimeMinutes: minutes, unit: UnitType.Service);
        fixture.Context.Products.Add(product);
        await fixture.Context.SaveChangesAsync();
        return product;
    }

    private static Task<PublicBookingSlotsDto?> SlotsAsync(SalesTestFixture fixture, Guid serviceId, DateOnly? from = null)
        => new GetPublicBookingSlotsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicBookingSlotsQuery(Slug, serviceId, from), CancellationToken.None);

    private static Task<Result<PublicAppointmentReceiptDto>> RequestAsync(
        SalesTestFixture fixture, Guid serviceId, DateTimeOffset startAt, string phone = "5522107765", Guid? key = null, string? notes = null)
        => new RequestPublicAppointmentCommandHandler(
                fixture.Context, fixture.TenantService, fixture.CurrentUser, new PhoneNormalizationService(), fixture.Clock,
                new NoBookingLock(), Notices(fixture))
            .Handle(new RequestPublicAppointmentCommand
            {
                Slug = Slug,
                ServiceProductId = serviceId,
                StartAt = startAt,
                CustomerName = "Valeria Ruiz",
                CustomerPhone = phone,
                Notes = notes,
                IdempotencyKey = key
            }, CancellationToken.None);

    /// <summary>Its customer cancels the request made last, now (as from its link).</summary>
    private static async Task CancelLastAsync(SalesTestFixture fixture)
    {
        var last = await fixture.Context.Appointments.OrderByDescending(a => a.StartAt).FirstAsync();
        last.CancelByCustomer(fixture.Clock.UtcNow);
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<Result<CustomerAppointmentDto>> CancelMineAsync(SalesTestFixture fixture, Guid appointmentId)
        => new CancelMyAppointmentCommandHandler(fixture.Context, fixture.CurrentUser, fixture.TenantService, fixture.Clock)
            .Handle(new CancelMyAppointmentCommand(appointmentId), CancellationToken.None);

    private static AppointmentRequestCommandHandler RequestHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, Notices(fixture));

    private static AppointmentNotices Notices(SalesTestFixture fixture)
        => new(fixture.Context, new PhoneNormalizationService(), fixture.Clock);

    private static async Task<Guid> CustomerAsync(SalesTestFixture fixture, string firstName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"{firstName.ToLowerInvariant()}@cliente.test",
            UserName = $"{firstName.ToLowerInvariant()}@cliente.test",
            FirstName = firstName,
            LastName = "Ruiz",
            Role = UserRole.Customer,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        await fixture.Context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Someone of the team of a business, with the phone of their account as they wrote it.</summary>
    private static async Task<Guid> MemberAsync(SalesTestFixture fixture, Guid tenantId, string? phone, TenantUserStatus status)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@equipo.test",
            UserName = $"{Guid.NewGuid():N}@equipo.test",
            FirstName = "Ana",
            LastName = "Ruiz",
            Role = UserRole.Staff,
            IsActive = true,
            PhoneNumber = phone
        };
        fixture.Context.Users.Add(user);
        fixture.Context.TenantUsers.Add(new TenantUser(tenantId, user.Id, TenantUserRole.Staff, status));
        await fixture.Context.SaveChangesAsync();
        return user.Id;
    }

    private static async Task ConnectWhatsAppAsync(SalesTestFixture fixture, Guid tenantId)
    {
        var config = new WhatsAppConfig(tenantId);
        config.Update(true, "token", "123456", "waba", "+52 55 0000 0000");
        fixture.Context.WhatsAppConfigs.Add(config);
        await fixture.Context.SaveChangesAsync();
    }

    private sealed class NoBookingLock : IAppointmentBookingLock
    {
        public Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>An owner of the business: every permission and the modules of its settings.</summary>
    private sealed class OwnerAccess : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Service | TenantCapability.Appointments, [], IsManager: true));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken)
            => Task.FromResult((capability & (TenantCapability.Service | TenantCapability.Appointments)) == capability);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken)
            => Task.FromResult((capabilities & (TenantCapability.Service | TenantCapability.Appointments)) != 0);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
