using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Choosing who attends an online appointment (Fase 24J, ADR 0032): the owner says who customers book, with their services
/// and hours; the booking page offers each person's free times, or whoever's; a request keeps who attends it, and the
/// customer reads the name the business gave them. A business that says nobody keeps booking as before.
/// </summary>
public class OnlineBookingStaffFlowTests
{
    // The fixture's now is Sunday 4 October 2026, 11:00 in Mexico City (UTC-6); the business opens Monday to Friday 9 to 14.
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private const string Slug = "barberia";

    private static DateTimeOffset Local(DateOnly date, string time) => new(date.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(-6));

    [Fact]
    public async Task Settings_ShouldListTheActiveTeam_WithTheSuggestedNames_AndTheServicesToGive()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 45);
        var ana = await MemberAsync(fixture, tenant.Id, "Ana", "López");
        await MemberAsync(fixture, tenant.Id, "Beto", "Ruiz", TenantUserStatus.Disabled);
        await MemberAsync(fixture, tenant.Id, "Carla", "Díaz", userActive: false);

        var settings = await Handler(fixture).Handle(new GetOnlineBookingStaffQuery(), CancellationToken.None);

        var member = Assert.Single(settings.Members);
        Assert.Equal((ana, "Ana López", "Ana L.", false, false, true), (
            member.UserId, member.FullName, member.Name, member.Configured, member.Enabled, member.AllServices));
        Assert.Null(member.WeeklyHours);
        Assert.Equal([(haircut.Id, "Corte de cabello")], settings.Services.Select(s => (s.Id, s.Name)));
        Assert.Equal(OnlineBookingStaff.MaxEnabled, settings.MaxEnabled);
    }

    [Fact]
    public async Task Update_ShouldSetThePerson_AndRecordTheChange_OnlyWhenSomethingChanged()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 45);
        var ana = await MemberAsync(fixture, tenant.Id, "Ana", "López");
        var command = new UpdateOnlineBookingStaffCommand
        {
            UserId = ana,
            Enabled = true,
            Name = "Ana L. 💈",
            AllServices = false,
            ServiceIds = [haircut.Id, haircut.Id],
            WeeklyHours = [new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = "10:00", Closes = "13:00" }]
        };

        var saved = await Handler(fixture).Handle(command, CancellationToken.None);
        await Handler(fixture).Handle(command, CancellationToken.None);

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal(("Ana L. 💈", true, true, false), (saved.Value!.Name, saved.Value.Enabled, saved.Value.Configured, saved.Value.AllServices));
        Assert.Equal([haircut.Id], saved.Value.ServiceIds);
        Assert.Equal("10:00", Assert.Single(saved.Value.WeeklyHours!).Opens);
        var stored = await fixture.Context.OnlineBookingStaff.Include(s => s.Services).SingleAsync();
        Assert.Equal((tenant.Id, ana), (stored.TenantId, stored.UserId));
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "OnlineBookingStaffUpdated"));
    }

    [Fact]
    public async Task Update_ShouldRefuse_SomeoneOutOfTheTeam_AServiceItDoesNotBook_AndBadHours()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var wax = await fixture.AddProductAsync(tenant, "Cera para cabello", 120m, unit: UnitType.Unit);
        var ana = await MemberAsync(fixture, tenant.Id, "Ana", "López");
        var disabled = await MemberAsync(fixture, tenant.Id, "Beto", "Ruiz", TenantUserStatus.Disabled);

        var stranger = await Handler(fixture).Handle(Person(Guid.NewGuid()), CancellationToken.None);
        var notActive = await Handler(fixture).Handle(Person(disabled), CancellationToken.None);
        var notBooked = await Handler(fixture).Handle(Person(ana) with { AllServices = false, ServiceIds = [wax.Id] }, CancellationToken.None);
        var overlap = Person(ana) with
        {
            WeeklyHours =
            [
                new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = "09:00", Closes = "13:00" },
                new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = "12:00", Closes = "14:00" }
            ]
        };

        Assert.Equal(OnlineBookingStaffHandler.NotInTeamMessage, stranger.Error);
        Assert.Equal(OnlineBookingStaffHandler.NotInTeamMessage, notActive.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.ServiceNotBookedMessage, notBooked.Error);
        await Assert.ThrowsAsync<DomainException>(() => Handler(fixture).Handle(overlap, CancellationToken.None));
        Assert.Empty(fixture.Context.OnlineBookingStaff);
        Assert.False(new UpdateOnlineBookingStaffCommandValidator().Validate(Person(ana) with { Name = "" }).IsValid);
        Assert.False(new UpdateOnlineBookingStaffCommandValidator().Validate(Person(ana) with
        {
            WeeklyHours = [new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = "9:00", Closes = "13:00" }]
        }).IsValid);
    }

    [Fact]
    public async Task Update_ShouldTurnOn_AtMostThirtyPeople()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        for (var index = 0; index < OnlineBookingStaff.MaxEnabled; index++)
        {
            var member = await MemberAsync(fixture, tenant.Id, $"Persona{index}", "Equipo");
            fixture.Context.OnlineBookingStaff.Add(OnlineBookingStaff.Create(tenant.Id, member, $"Persona {index}", true, true, [], null));
        }
        await fixture.Context.SaveChangesAsync();
        var oneMore = await MemberAsync(fixture, tenant.Id, "Ana", "López");

        var refused = await Handler(fixture).Handle(Person(oneMore), CancellationToken.None);
        var off = await Handler(fixture).Handle(Person(oneMore) with { Enabled = false }, CancellationToken.None);

        Assert.Equal(OnlineBookingStaffHandler.TooManyEnabledMessage, refused.Error);
        Assert.True(off.IsSuccess);
    }

    [Fact]
    public async Task BookingPage_ShouldOfferThePeople_AndOnlyTheServicesSomeoneDoes()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 45);
        var beard = await ServiceAsync(fixture, tenant, "Barba", 30);
        await ServiceAsync(fixture, tenant, "Tinte", 90);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.", services: [haircut.Id]);
        var luis = await StaffAsync(fixture, tenant, "Luis", "Luis M.", services: [haircut.Id, beard.Id]);
        await StaffAsync(fixture, tenant, "Off", "Apagado", enabled: false);

        var page = await new GetPublicBookingQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPublicBookingQuery(Slug), CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal([(ana.Id, "Ana L."), (luis.Id, "Luis M.")], page.Staff.Select(p => (p.Id, p.Name)));
        Assert.Equal(["Barba", "Corte de cabello"], page.Services.Select(s => s.Name).Order());
        Assert.Equal([luis.Id], page.Services.Single(s => s.Name == "Barba").StaffIds);
        Assert.Equal([ana.Id, luis.Id], page.Services.Single(s => s.Name == "Corte de cabello").StaffIds);
    }

    [Fact]
    public async Task Slots_ShouldBeEachPersonsFreeTimes_OrWhoeversIsFree()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        // Ana comes from 11; Luis has an appointment from 9 to 11.
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.", hours: Weekdays("11:00", "14:00"));
        var luis = await StaffAsync(fixture, tenant, "Luis", "Luis M.");
        await AppointmentAsync(fixture, tenant, Local(Monday, "09:00"), Local(Monday, "11:00"), luis.UserId);

        var anyone = await SlotsAsync(fixture, haircut.Id);
        var withAna = await SlotsAsync(fixture, haircut.Id, staffId: ana.Id);
        var withLuis = await SlotsAsync(fixture, haircut.Id, staffId: luis.Id);

        Assert.Equal(["11:00 a. m.", "11:30 a. m.", "12:00 p. m.", "12:30 p. m.", "1:00 p. m."], anyone!.Days[1].Slots.Select(s => s.Label));
        Assert.Equal(anyone.Days[1].Slots.Select(s => s.StartAt), withAna!.Days[1].Slots.Select(s => s.StartAt));
        Assert.Equal(anyone.Days[1].Slots.Select(s => s.StartAt), withLuis!.Days[1].Slots.Select(s => s.StartAt));
        // Tuesday Luis is free from 9; Ana still from 11.
        Assert.Equal("9:00 a. m.", anyone.Days[2].Slots[0].Label);
        Assert.Equal("11:00 a. m.", withAna.Days[2].Slots[0].Label);
    }

    [Fact]
    public async Task Slots_ShouldRefuse_APersonWhoDoesNotTakeTheService_OrAnyPersonWhenNobodyIsSaid()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var beard = await ServiceAsync(fixture, tenant, "Barba", 30);
        Assert.Null(await SlotsAsync(fixture, haircut.Id, staffId: Guid.NewGuid()));

        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.", services: [haircut.Id]);

        Assert.Null(await SlotsAsync(fixture, beard.Id));
        Assert.Null(await SlotsAsync(fixture, beard.Id, staffId: ana.Id));
        Assert.Null(await SlotsAsync(fixture, haircut.Id, staffId: Guid.NewGuid()));
        Assert.NotNull(await SlotsAsync(fixture, haircut.Id, staffId: ana.Id));
    }

    [Fact]
    public async Task Slots_ShouldLeave_ATimeAnAppointmentWithNobodyTakes_WhenNobodyElseIsFree()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.");
        await AppointmentAsync(fixture, tenant, Local(Monday, "10:00"), Local(Monday, "11:00"), staffUserId: null);

        var week = await SlotsAsync(fixture, haircut.Id, staffId: ana.Id);

        Assert.DoesNotContain("10:00 a. m.", week!.Days[1].Slots.Select(s => s.Label));
        Assert.Contains("9:00 a. m.", week.Days[1].Slots.Select(s => s.Label));
    }

    [Fact]
    public async Task Slots_ShouldKeep_TheTripAroundAPersonsVisit()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.");
        var visit = await AppointmentAsync(fixture, tenant, Local(Monday, "11:00"), Local(Monday, "12:00"), ana.UserId);
        var zone = new Domain.Delivery.DeliveryZone(tenant.Id, new Domain.Delivery.DeliveryZoneFields("Centro", 50m, 0m, null, ["06000"]));
        fixture.Context.DeliveryZones.Add(zone);
        visit.ArrangeVisit(Domain.Orders.HomeDelivery.DeliveryAddress.ForVisit("06000", "Calle 1, Col. Centro", null), zone, fixture.Clock.UtcNow);
        await fixture.Context.SaveChangesAsync();

        var week = await SlotsAsync(fixture, haircut.Id, staffId: ana.Id);

        // Thirty minutes of trip before and after: from 10:30 to 12:30 she is away.
        Assert.Equal(["9:00 a. m.", "9:30 a. m.", "12:30 p. m.", "1:00 p. m."], week!.Days[1].Slots.Select(s => s.Label));
    }

    [Fact]
    public async Task Request_ShouldKeep_ThePersonChosen_AndTheReceiptSaysTheirName()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.");
        var luis = await StaffAsync(fixture, tenant, "Luis", "Luis M.");
        var key = Guid.NewGuid();

        var receipt = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), luis.Id, key: key);
        var again = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), luis.Id, key: key);

        Assert.True(receipt.IsSuccess, receipt.Error);
        Assert.Equal("Luis M.", receipt.Value!.StaffName);
        Assert.Equal("Luis M.", again.Value!.StaffName);
        var appointment = await fixture.Context.Appointments.SingleAsync();
        Assert.Equal((luis.UserId, "Luis Equipo"), (appointment.StaffUserId, appointment.StaffName));
        // Luis's time is taken now, Ana's is not.
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage,
            (await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), luis.Id, phone: "5512345678")).Error);
        Assert.True((await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), ana.Id, phone: "5512345678")).IsSuccess);
    }

    [Fact]
    public async Task Request_WithWhoeverIsFree_ShouldGoToTheOneWithTheFewestAppointmentsThatDay()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.");
        var luis = await StaffAsync(fixture, tenant, "Luis", "Luis M.");
        await AppointmentAsync(fixture, tenant, Local(Monday, "09:00"), Local(Monday, "09:30"), ana.UserId);

        var first = await RequestAsync(fixture, haircut.Id, Local(Monday, "12:00"), staffId: null);
        var second = await RequestAsync(fixture, haircut.Id, Local(Monday, "12:00"), staffId: null, phone: "5512345678");
        var third = await RequestAsync(fixture, haircut.Id, Local(Monday, "12:00"), staffId: null, phone: "5587654321");

        Assert.Equal("Luis M.", first.Value!.StaffName);
        Assert.Equal("Ana L.", second.Value!.StaffName);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, third.Error);
    }

    [Fact]
    public async Task Request_ShouldRefuse_APersonWhoNoLongerTakesIt_OrAnyoneWhenNobodyIsSaid()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);

        var noPeople = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), Guid.NewGuid());
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.", enabled: false);
        var off = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), ana.Id);

        Assert.Equal(RequestPublicAppointmentCommandHandler.StaffGoneMessage, noPeople.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.StaffGoneMessage, off.Error);
        // Nobody on: the business books by how many it attends at once, as before.
        var asBefore = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), staffId: null);
        Assert.True(asBefore.IsSuccess, asBefore.Error);
        Assert.Null(asBefore.Value!.StaffName);
        Assert.Null((await fixture.Context.Appointments.SingleAsync()).StaffUserId);
    }

    [Fact]
    public async Task CustomerPages_ShouldSay_WhoAttendsIt_ByTheirNameForCustomers()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.");
        fixture.CurrentUser.UserId = (await CustomerAsync(fixture)).ToString();
        var receipt = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), ana.Id);

        var followed = await new GetPublicAppointmentQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicAppointmentQuery(receipt.Value!.TrackingToken), CancellationToken.None);
        // Mis citas reads every business of the account (without the business's filter).
        fixture.TenantService.SetTenant(Guid.Empty);
        var mine = await new GetMyAppointmentsQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
            .Handle(new GetMyAppointmentsQuery(), CancellationToken.None);

        Assert.Equal("Ana L.", followed!.StaffName);
        Assert.Equal("Ana L.", Assert.Single(mine.Items).StaffName);
    }

    [Fact]
    public async Task Request_ShouldKeep_ALongFullName_CutToWhatTheAgendaKeeps()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var userId = await MemberAsync(fixture, tenant.Id, new string('a', 100), new string('b', 100));
        var staff = OnlineBookingStaff.Create(tenant.Id, userId, "Ana L.", true, true, [], null);
        fixture.Context.OnlineBookingStaff.Add(staff);
        await fixture.Context.SaveChangesAsync();

        var receipt = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), staff.Id);

        Assert.True(receipt.IsSuccess, receipt.Error);
        var appointment = await fixture.Context.Appointments.SingleAsync();
        Assert.Equal(Appointment.MaxStaffNameLength, appointment.StaffName!.Length);
        Assert.Equal(userId, appointment.StaffUserId);
    }

    [Fact]
    public async Task APersonOutOfTheTeam_ShouldNotBeOffered_NorBooked_ThoughStillTurnedOn()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.");
        // Luis left the team and Marta's account was turned off; their profiles are still on.
        var luis = OnlineBookingStaff.Create(
            tenant.Id, await MemberAsync(fixture, tenant.Id, "Luis", "Equipo", TenantUserStatus.Disabled), "Luis M.", true, true, [], null);
        var marta = OnlineBookingStaff.Create(
            tenant.Id, await MemberAsync(fixture, tenant.Id, "Marta", "Equipo", userActive: false), "Marta R.", true, true, [], null);
        fixture.Context.OnlineBookingStaff.AddRange(luis, marta);
        await fixture.Context.SaveChangesAsync();

        var page = await new GetPublicBookingQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPublicBookingQuery(Slug), CancellationToken.None);

        Assert.Equal([ana.Id], page!.Staff.Select(person => person.Id));
        Assert.Null(await SlotsAsync(fixture, haircut.Id, staffId: luis.Id));
        Assert.Null(await SlotsAsync(fixture, haircut.Id, staffId: marta.Id));
        var refused = await RequestAsync(fixture, haircut.Id, Local(Monday, "10:00"), luis.Id);
        Assert.Equal(RequestPublicAppointmentCommandHandler.StaffGoneMessage, refused.Error);
        Assert.Equal(1, (await OnlineBookingCalendarStaffCountAsync(fixture, tenant.Id)));
    }

    [Fact]
    public async Task Menu_AndExplorar_ShouldStopOfferingBooking_WhenNobodyWhoAttendsDoesAServiceStillBooked()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var beard = await ServiceAsync(fixture, tenant, "Barba", 30);
        await StaffAsync(fixture, tenant, "Ana", "Ana L.", services: [beard.Id]);
        Assert.True((await MenuAsync(fixture))!.BooksOnline);
        Assert.Contains((await ExplorarAsync(fixture)).Entries, entry => entry.TenantId == tenant.Id && entry.Business.BooksOnline);

        beard.SetAvailability(false);
        await fixture.Context.SaveChangesAsync();

        // The haircut is still booked, but nobody who attends does it: the booking page would have no service.
        var page = await new GetPublicBookingQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPublicBookingQuery(Slug), CancellationToken.None);
        Assert.Empty(page!.Services);
        Assert.False((await MenuAsync(fixture))!.BooksOnline);
        Assert.DoesNotContain((await ExplorarAsync(fixture)).Entries, entry => entry.TenantId == tenant.Id && entry.Business.BooksOnline);
        fixture.TenantService.SetTenant(tenant.Id);
        var settings = await new OnlineBookingSettingsHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new GetOnlineBookingSettingsQuery(), CancellationToken.None);
        Assert.False(settings.BooksOnline);
        Assert.Equal([OnlineBookingSettingsHandler.NobodyOffersMessage], settings.Missing);
    }

    [Fact]
    public async Task APausedService_ShouldStayWithThePerson_AndComeBackToThePageWithThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var haircut = await ServiceAsync(fixture, tenant, "Corte de cabello", 60);
        var beard = await ServiceAsync(fixture, tenant, "Barba", 30);
        beard.SetAvailability(false);
        await fixture.Context.SaveChangesAsync();
        var ana = await MemberAsync(fixture, tenant.Id, "Ana", "López");

        var settings = await Handler(fixture).Handle(new GetOnlineBookingStaffQuery(), CancellationToken.None);
        var saved = await Handler(fixture).Handle(
            Person(ana) with { AllServices = false, ServiceIds = [haircut.Id, beard.Id] }, CancellationToken.None);

        Assert.Equal([("Barba", false), ("Corte de cabello", true)], settings.Services.Select(service => (service.Name, service.BookableNow)));
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal([haircut.Id], (await BookingAsync(fixture)).Services.Select(service => service.Id));
        beard.SetAvailability(true);
        await fixture.Context.SaveChangesAsync();
        var back = await BookingAsync(fixture);
        Assert.Equal(2, back.Services.Count);
        Assert.All(back.Services, service => Assert.Single(service.StaffIds));
    }

    [Fact]
    public async Task OwnerSettings_ShouldCount_ThePeopleCustomersBook()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        await StaffAsync(fixture, tenant, "Ana", "Ana L.");
        await StaffAsync(fixture, tenant, "Off", "Apagado", enabled: false);

        var settings = await new OnlineBookingSettingsHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new GetOnlineBookingSettingsQuery(), CancellationToken.None);

        Assert.Equal(1, settings.StaffCount);
    }

    private static async Task<PublicBookingDto> BookingAsync(SalesTestFixture fixture)
        => (await new GetPublicBookingQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPublicBookingQuery(Slug), CancellationToken.None))!;

    private static Task<PublicMenuDto?> MenuAsync(SalesTestFixture fixture)
        => new GetPublicMenuQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock, FakeOnlinePaymentGateway.Off)
            .Handle(new GetPublicMenuQuery(Slug), CancellationToken.None);

    private static Task<PublicDirectorySnapshot> ExplorarAsync(SalesTestFixture fixture)
        => PublicDirectorySnapshot.BuildAsync(fixture.Context, fixture.Clock.UtcNow, CancellationToken.None);

    private static async Task<int> OnlineBookingCalendarStaffCountAsync(SalesTestFixture fixture, Guid tenantId)
        => (await OnlineBookingCalendar.StaffAsync(fixture.Context, tenantId, CancellationToken.None)).Count;

    private static async Task<Guid> CustomerAsync(SalesTestFixture fixture)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "valeria@cliente.test",
            UserName = "valeria@cliente.test",
            FirstName = "Valeria",
            LastName = "Ruiz",
            Role = UserRole.Customer,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        await fixture.Context.SaveChangesAsync();
        return user.Id;
    }

    private static UpdateOnlineBookingStaffCommand Person(Guid userId) => new() { UserId = userId, Enabled = true, Name = "Ana L.", AllServices = true };

    private static OnlineBookingStaffHandler Handler(SalesTestFixture fixture) => new(fixture.Context, fixture.TenantService, fixture.CurrentUser);

    private static OpeningHours Weekdays(string opens, string closes) => OpeningHours.Create(
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new OpeningInterval(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes))));

    private static async Task<Tenant> BusinessAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: Slug);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineBooking(true, 1);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetOpeningHours(Weekdays("09:00", "14:00"));
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

    private static async Task<Guid> MemberAsync(
        SalesTestFixture fixture, Guid tenantId, string firstName, string lastName,
        TenantUserStatus status = TenantUserStatus.Active, bool userActive = true)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"{firstName.ToLowerInvariant()}@negocio.test",
            UserName = $"{firstName.ToLowerInvariant()}@negocio.test",
            FirstName = firstName,
            LastName = lastName,
            Role = UserRole.Staff,
            IsActive = userActive
        };
        fixture.Context.Users.Add(user);
        fixture.Context.TenantUsers.Add(new TenantUser(tenantId, user.Id, TenantUserRole.Staff, status));
        await fixture.Context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>A team member the business books online.</summary>
    private static async Task<OnlineBookingStaff> StaffAsync(
        SalesTestFixture fixture, Tenant tenant, string firstName, string name,
        bool enabled = true, Guid[]? services = null, OpeningHours? hours = null)
    {
        var userId = await MemberAsync(fixture, tenant.Id, firstName, "Equipo");
        var staff = OnlineBookingStaff.Create(tenant.Id, userId, name, enabled, services is null, services ?? [], hours);
        fixture.Context.OnlineBookingStaff.Add(staff);
        await fixture.Context.SaveChangesAsync();
        return staff;
    }

    private static async Task<Appointment> AppointmentAsync(
        SalesTestFixture fixture, Tenant tenant, DateTimeOffset startAt, DateTimeOffset endAt, Guid? staffUserId)
    {
        var appointment = new Appointment(
            tenant.Id, "Cliente", "5511112222", "Corte", startAt, endAt, staffName: staffUserId is null ? null : "Alguien", staffUserId: staffUserId);
        fixture.Context.Appointments.Add(appointment);
        await fixture.Context.SaveChangesAsync();
        return appointment;
    }

    private static Task<PublicBookingSlotsDto?> SlotsAsync(SalesTestFixture fixture, Guid serviceId, DateOnly? from = null, Guid? staffId = null)
        => new GetPublicBookingSlotsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicBookingSlotsQuery(Slug, serviceId, from, staffId), CancellationToken.None);

    private static Task<Result<PublicAppointmentReceiptDto>> RequestAsync(
        SalesTestFixture fixture, Guid serviceId, DateTimeOffset startAt, Guid? staffId, string phone = "5522107765", Guid? key = null)
        => new RequestPublicAppointmentCommandHandler(
                fixture.Context, fixture.TenantService, fixture.CurrentUser, new PhoneNormalizationService(), fixture.Clock,
                new NoBookingLock(), new AppointmentNotices(fixture.Context, new PhoneNormalizationService(), fixture.Clock))
            .Handle(new RequestPublicAppointmentCommand
            {
                Slug = Slug,
                ServiceProductId = serviceId,
                StartAt = startAt,
                CustomerName = "Valeria Ruiz",
                CustomerPhone = phone,
                IdempotencyKey = key,
                StaffId = staffId
            }, CancellationToken.None);

    private sealed class NoBookingLock : IAppointmentBookingLock
    {
        public Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
