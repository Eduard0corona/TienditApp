using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Fiscal;
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
/// Visits to quote requested online (Fase 29B, ADR 0053 points 11 to 13): the request page of a business that receives
/// visits, whether it goes to a postal code and what the visit costs there, free times that leave the trip free, and the
/// request, an online appointment «Por confirmar» at the customer's address that keeps how the visit is charged.
/// </summary>
public class HomeVisitBookingFlowTests
{
    // SalesTestFixture.Now is Sunday 4 October 2026, 11:00 in Mexico City (UTC-6); trades open Monday to Saturday 9 to 18.
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private const string Slug = "plomero";
    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    private static DateTimeOffset Local(DateOnly date, string time) => new(date.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(-6));

    [Fact]
    public async Task Page_ShouldDescribeTheVisit_ItsDurationAndWhatItCosts()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, zones: [("Centro", 50m, ["72000"]), ("Norte", 80m, ["72001"])]);
        await SetVisitAsync(fixture, tenant, await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 45));

        var page = await PageAsync(fixture);

        Assert.NotNull(page);
        Assert.Equal(("Visita y diagnóstico", 45, HomeVisitChargePolicy.Charged, OnlineBookingRules.DaysAhead),
            (page.VisitName, page.DurationMinutes, page.ChargePolicy, page.DaysAhead));
        Assert.Equal((350m, true), (page.Price.Amount, page.Price.IsStarting));
        Assert.Empty(page.Staff);
    }

    [Fact]
    public async Task Page_WithoutAVisitService_ShouldBeAnHourThatCostsWhatTheZoneCharges()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);

        var page = await PageAsync(fixture);

        Assert.Equal((HomeVisitRules.DefaultVisitName, 60, 50m, false),
            (page!.VisitName, page.DurationMinutes, page.Price.Amount, page.Price.IsStarting));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("menu")]
    [InlineData("hours")]
    [InlineData("module")]
    [InlineData("zones")]
    public async Task Page_ShouldNotExist_UntilTheBusinessReceivesVisits(string missing)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture,
            capabilities: missing == "module" ? TenantCapability.Service : TenantCapability.Service | TenantCapability.Appointments,
            menu: missing != "menu", hours: missing != "hours", zones: missing == "zones" ? [] : null);
        if (missing == "off")
            await SetVisitAsync(fixture, tenant, null, enabled: false);

        Assert.Null(await PageAsync(fixture));
        Assert.Null(await CoverageAsync(fixture, "72000"));
        Assert.Null(await SlotsAsync(fixture));
        Assert.Equal(RequestPublicHomeVisitCommandHandler.NotReceivingMessage, (await RequestAsync(fixture, Local(Monday, "10:00"))).Error);
    }

    [Fact]
    public async Task Coverage_ShouldSayWhetherItGoes_AndWhatTheVisitCostsThere_OnlyWithItsOwnZones()
    {
        await using var fixture = SalesTestFixture.Create();
        // Another business goes to 72009: that says nothing about this one.
        await TradeAsync(fixture, slug: "electricista", zones: [("Sur", 20m, ["72009"])]);
        var tenant = await TradeAsync(fixture, zones: [("Centro", 50m, ["72000"]), ("Norte", 80m, ["72001"])]);
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 60);
        await SetVisitAsync(fixture, tenant, visit);

        var north = await CoverageAsync(fixture, " 72001 ");
        var elsewhere = await CoverageAsync(fixture, "72009");
        await SetVisitAsync(fixture, tenant, visit, HomeVisitChargePolicy.Free);
        var free = await CoverageAsync(fixture, "72001");

        Assert.Equal(("72001", true, (decimal?)380m), (north!.PostalCode, north.Covered, north.Price));
        Assert.Equal((false, (decimal?)null), (elsewhere!.Covered, elsewhere.Price));
        Assert.Equal((decimal?)0m, free!.Price);
    }

    [Fact]
    public async Task Slots_ShouldLeaveTheTripFree_AroundTheBusinessesOwnAppointments()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await TradeAsync(fixture, slug: "electricista");
        fixture.Context.Appointments.Add(new Appointment(other.Id, "Ana", "5512345678", "Revisión", Local(Monday, "09:00"), Local(Monday, "10:00")));
        var tenant = await TradeAsync(fixture);
        await SetVisitAsync(fixture, tenant, await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 60));
        fixture.Context.Appointments.Add(new Appointment(tenant.Id, "Luis", "5512345679", "Revisión", Local(Monday, "12:00"), Local(Monday, "13:00")));
        await fixture.Context.SaveChangesAsync();

        var week = await SlotsAsync(fixture);

        Assert.NotNull(week);
        var monday = week.Days.Single(day => day.Date == Monday).Slots.Select(slot => slot.Label).ToList();
        // Half an hour of trip on each side (the business's travel time): from 11:00 to 13:00 the visit would touch Luis's.
        Assert.Equal(
            ["9:00 a. m.", "9:30 a. m.", "10:00 a. m.", "10:30 a. m.", "1:30 p. m.", "2:00 p. m.", "2:30 p. m.", "3:00 p. m.",
                "3:30 p. m.", "4:00 p. m.", "4:30 p. m.", "5:00 p. m."],
            monday);
        Assert.Equal(new DateOnly(2026, 10, 11), week.NextFrom);
    }

    [Fact]
    public async Task Staff_ShouldBeWhoDoesTheVisit_EachWithTheirTrip()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 60);
        var install = await ServiceAsync(fixture, tenant, "Instalación de boiler", 900m, 120);
        await SetVisitAsync(fixture, tenant, visit);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.", [visit.Id]);
        var luis = await StaffAsync(fixture, tenant, "Luis", "Luis P.", [install.Id]);
        fixture.Context.Appointments.Add(new Appointment(
            tenant.Id, "Cliente", "5511112222", "Revisión", Local(Monday, "10:00"), Local(Monday, "11:00"), staffName: "Ana López", staffUserId: ana.UserId));
        await fixture.Context.SaveChangesAsync();

        var page = await PageAsync(fixture);
        var withAna = await SlotsAsync(fixture, staffId: ana.Id);
        var withLuis = await SlotsAsync(fixture, staffId: luis.Id);
        var taken = await RequestAsync(fixture, Local(Monday, "11:00"), price: 350m);
        var requested = await RequestAsync(fixture, Local(Monday, "11:30"), price: 350m);
        var refused = await RequestAsync(fixture, Local(Monday, "14:00"), phone: "5511113333", staffId: luis.Id, price: 350m);

        Assert.Equal(["Ana L."], page!.Staff.Select(person => person.Name));
        var monday = withAna!.Days.Single(day => day.Date == Monday).Slots.Select(slot => slot.Label).ToList();
        Assert.DoesNotContain("9:00 a. m.", monday);
        Assert.DoesNotContain("11:00 a. m.", monday);
        Assert.Contains("11:30 a. m.", monday);
        Assert.Null(withLuis);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, taken.Error);
        Assert.True(requested.IsSuccess, requested.Error);
        Assert.Equal("Ana L.", requested.Value!.StaffName);
        Assert.Equal(RequestPublicHomeVisitCommandHandler.StaffGoneMessage, refused.Error);
        var stored = await fixture.Context.Appointments.SingleAsync(a => a.Channel == AppointmentChannel.Online);
        Assert.Equal((ana.UserId, "Ana Equipo"), (stored.StaffUserId, stored.StaffName));
    }

    [Fact]
    public async Task Request_ShouldHoldTheTime_PorConfirmar_AtTheCustomersAddress()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 45);
        await SetVisitAsync(fixture, tenant, visit);

        var result = await RequestAsync(fixture, Local(Monday, "10:00"), email: " valeria@correo.mx ", price: 350m);

        Assert.True(result.IsSuccess, result.Error);
        var receipt = result.Value!;
        Assert.Equal(("Visita y diagnóstico", "lunes 5 de octubre", "10:00 a. m."), (receipt.ServiceName, receipt.DateLabel, receipt.TimeLabel));
        Assert.Null(receipt.Address);
        Assert.Equal(("72000", (HomeVisitChargePolicy?)HomeVisitChargePolicy.Charged), (receipt.Visit!.PostalCode, receipt.Visit.ChargePolicy));
        var stored = await fixture.Context.Appointments.SingleAsync();
        Assert.Equal((AppointmentStatus.Requested, AppointmentChannel.Online, "+525522107765", Local(Monday, "10:45")),
            (stored.Status, stored.Channel, stored.CustomerPhone, stored.EndAt));
        Assert.Equal((visit.Id, "Gotea el calentador", "valeria@correo.mx"), (stored.ServiceProductId, stored.Notes, stored.CustomerEmail));
        Assert.Equal(("72000", "Av. Juárez 123, Col. Centro", "Portón negro", "Centro", 50m, (HomeVisitChargePolicy?)HomeVisitChargePolicy.Charged),
            (stored.Visit!.PostalCode, stored.Visit.AddressLine, stored.Visit.References, stored.Visit.ZoneName, stored.Visit.Fee, stored.Visit.ChargePolicy));
        var notice = await fixture.Context.OutgoingMessages.SingleAsync(m => m.Purpose == MessagePurpose.AppointmentNotice);
        Assert.Contains(
            "recibimos tu solicitud de cita de Visita y diagnóstico el lunes 5 de octubre a las 10:00 a. m. a domicilio (CP 72000) con Negocio plomero.",
            notice.Body);
        // Their address stays out of the messages' log.
        Assert.DoesNotContain("Juárez", notice.Body);

        // The agenda shows where its quote goes and how the visit is charged; accepted, the customer reads what it costs.
        var agenda = AppointmentDto.From(stored);
        Assert.Equal(("valeria@correo.mx", (HomeVisitChargePolicy?)HomeVisitChargePolicy.Charged), (agenda.CustomerEmail, agenda.Visit!.ChargePolicy));
        var accepted = await AcceptAsync(fixture, stored.Id);
        Assert.True(accepted.IsSuccess, accepted.Error);
        var scheduled = await fixture.Context.OutgoingMessages
            .Where(m => m.Purpose == MessagePurpose.AppointmentNotice && m.Body.Contains("quedó agendada"))
            .SingleAsync();
        Assert.EndsWith("Visita: $350.00.", scheduled.Body);
    }

    [Fact]
    public async Task Request_SinCosto_ShouldChargeNothing_AndSaySo()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        await SetVisitAsync(fixture, tenant, await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 60), HomeVisitChargePolicy.Free);

        var result = await RequestAsync(fixture, Local(Monday, "10:00"), price: 0m, policy: HomeVisitChargePolicy.Free);
        var stored = await fixture.Context.Appointments.SingleAsync();
        await AcceptAsync(fixture, stored.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal((0m, (HomeVisitChargePolicy?)HomeVisitChargePolicy.Free), (stored.Visit!.Fee, stored.Visit.ChargePolicy));
        var scheduled = await fixture.Context.OutgoingMessages
            .Where(m => m.Purpose == MessagePurpose.AppointmentNotice && m.Body.Contains("quedó agendada"))
            .SingleAsync();
        Assert.EndsWith("La visita es sin costo.", scheduled.Body);
    }

    [Fact]
    public async Task ARequestFromTheBusinessPhone_ShouldBeKeptAsFromTheTeam()
    {
        // Fase 29M (ADR 0067): a visit requested from the business's own phone does not count in Explorar.
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 45);
        await SetVisitAsync(fixture, tenant, visit);
        tenant.Update(tenant.Name, tenant.ContactEmail, "222 123 4567", null);
        await fixture.Context.SaveChangesAsync();

        var ours = await RequestAsync(fixture, Local(Monday, "10:00"), phone: "+52 222 123 4567", price: 350m);
        var theirs = await RequestAsync(fixture, Local(Monday, "12:00"), phone: "5522107765", price: 350m);

        Assert.True(ours.IsSuccess, ours.Error);
        Assert.True(theirs.IsSuccess, theirs.Error);
        var fromTeam = await fixture.Context.Appointments.AsNoTracking().ToDictionaryAsync(a => a.TrackingToken!, a => a.FromTeam);
        Assert.Equal((true, false), (fromTeam[ours.Value!.TrackingToken], fromTeam[theirs.Value!.TrackingToken]));
    }

    [Fact]
    public async Task Request_ShouldRefuse_APostalCodeItDoesNotGoTo_OrATimeThatLeavesNoTrip()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture, slug: "electricista", zones: [("Sur", 20m, ["72009"])]);
        var tenant = await TradeAsync(fixture);
        fixture.Context.Appointments.Add(new Appointment(tenant.Id, "Luis", "5512345679", "Revisión", Local(Monday, "12:00"), Local(Monday, "13:00")));
        await fixture.Context.SaveChangesAsync();

        var elsewhere = await RequestAsync(fixture, Local(Monday, "10:00"), postalCode: "72009");
        var noTrip = await RequestAsync(fixture, Local(Monday, "13:00"));
        var tooFar = await RequestAsync(fixture, Local(Monday.AddDays(40), "10:00"));
        var after = await RequestAsync(fixture, Local(Monday, "13:30"));

        Assert.Equal(HomeVisitRules.NotCoveredMessage("72009"), elsewhere.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, noTrip.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TooFarMessage, tooFar.Error);
        Assert.True(after.IsSuccess, after.Error);
        Assert.Equal(1, await fixture.Context.Appointments.CountAsync(a => a.Channel == AppointmentChannel.Online));
    }

    [Fact]
    public async Task Request_ShouldCount_WithThePhonesOtherRequests_AndAnswerTheSameRequestOnce()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var key = Guid.NewGuid();

        var first = await RequestAsync(fixture, Local(Monday, "09:00"), key: key);
        var again = await RequestAsync(fixture, Local(Monday, "09:00"), key: key);
        var otherPhone = await RequestAsync(fixture, Local(Monday, "16:00"), key: key, phone: "5511112222");
        Assert.True((await RequestAsync(fixture, Local(Monday, "11:00"))).IsSuccess);
        // An appointment the same phone asked for online at the business counts too.
        fixture.Context.Appointments.Add(Appointment.RequestOnline(
            (await fixture.Context.Tenants.SingleAsync(t => t.Slug == Slug)).Id, "Valeria Ruiz", "+525522107765", Guid.NewGuid(), "Revisión",
            Local(Monday.AddDays(1), "10:00"), Local(Monday.AddDays(1), "11:00"), null, null, Guid.NewGuid(), new string('a', 43)));
        await fixture.Context.SaveChangesAsync();
        var fourth = await RequestAsync(fixture, Local(Monday, "15:00"));

        Assert.Equal(first.Value!.TrackingToken, again.Value!.TrackingToken);
        Assert.Equal(RequestPublicAppointmentCommandHandler.AlreadySentMessage, otherPhone.Error);
        Assert.Equal(RequestPublicAppointmentCommandHandler.TooManyPendingMessage, fourth.Error);
        Assert.Equal(3, await fixture.Context.Appointments.CountAsync());
    }

    [Fact]
    public async Task Request_ShouldRefuse_AVisitChargedOtherwiseThanThePageSaid()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 60);
        await SetVisitAsync(fixture, tenant, visit, HomeVisitChargePolicy.Free);

        // The page said «Sin costo»; the owner charges it meanwhile, or charges more for it.
        await SetVisitAsync(fixture, tenant, visit, HomeVisitChargePolicy.Charged);
        var policyChanged = await RequestAsync(fixture, Local(Monday, "10:00"), price: 0m, policy: HomeVisitChargePolicy.Free);
        var priceChanged = await RequestAsync(fixture, Local(Monday, "10:00"), price: 300m);
        var agreed = await RequestAsync(fixture, Local(Monday, "10:00"), price: 350m);

        Assert.Equal(RequestPublicHomeVisitCommandHandler.VisitChangedMessage, policyChanged.Error);
        Assert.Equal(RequestPublicHomeVisitCommandHandler.VisitChangedMessage, priceChanged.Error);
        Assert.True(agreed.IsSuccess, agreed.Error);
        Assert.Equal(1, await fixture.Context.Appointments.CountAsync());
    }

    [Fact]
    public async Task AVisitNobodyOfTheOnlineStaffDoes_ShouldStillBeRequested_WithNobodyAssigned()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 60);
        var install = await ServiceAsync(fixture, tenant, "Instalación de boiler", 900m, 120);
        await SetVisitAsync(fixture, tenant, visit);
        var luis = await StaffAsync(fixture, tenant, "Luis", "Luis P.", [install.Id]);

        var page = await PageAsync(fixture);
        var week = await SlotsAsync(fixture);
        var withLuis = await SlotsAsync(fixture, staffId: luis.Id);
        var requested = await RequestAsync(fixture, Local(Monday, "10:00"), price: 350m);

        // The store never offers a visit nobody can take: the business assigns it in the agenda.
        Assert.Empty(page!.Staff);
        Assert.Contains("10:00 a. m.", week!.Days.Single(day => day.Date == Monday).Slots.Select(slot => slot.Label));
        Assert.Null(withLuis);
        Assert.True(requested.IsSuccess, requested.Error);
        var stored = await fixture.Context.Appointments.SingleAsync();
        Assert.Equal(((Guid?)null, (string?)null), (stored.StaffUserId, stored.StaffName));
    }

    [Fact]
    public async Task TheTrip_ShouldCountOnce_BetweenTwoVisitsOfThePerson()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 300m, 60);
        await SetVisitAsync(fixture, tenant, visit);
        var ana = await StaffAsync(fixture, tenant, "Ana", "Ana L.", [visit.Id]);
        var zone = await fixture.Context.DeliveryZones.Include(z => z.PostalCodes).SingleAsync(z => z.TenantId == tenant.Id);
        var other = new Appointment(
            tenant.Id, "Cliente", "5511112222", "Revisión", Local(Monday, "10:00"), Local(Monday, "11:00"), staffName: "Ana Equipo", staffUserId: ana.UserId);
        other.ArrangeVisit(Domain.Orders.HomeDelivery.DeliveryAddress.ForVisit("72000", "Calle 5 de Mayo 8", null), zone, SalesTestFixture.Now);
        fixture.Context.Appointments.Add(other);
        await fixture.Context.SaveChangesAsync();

        var monday = (await SlotsAsync(fixture, staffId: ana.Id))!.Days.Single(day => day.Date == Monday).Slots.Select(slot => slot.Label).ToList();

        // Half an hour between the two visits, not an hour: the other visit's trip is this one's.
        Assert.DoesNotContain("11:00 a. m.", monday);
        Assert.Contains("11:30 a. m.", monday);
        // Before it, the visit at 9:00 would end when the other starts: no time to get there.
        Assert.DoesNotContain("9:00 a. m.", monday);
    }

    [Fact]
    public async Task Request_ShouldRefuse_AnEmailItCannotWriteTo()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);

        var result = await RequestAsync(fixture, Local(Monday, "10:00"), email: "Valeria <valeria@correo.mx>");
        var validation = new RequestPublicHomeVisitCommandValidator().Validate(new RequestPublicHomeVisitCommand
        {
            Slug = Slug,
            StartAt = Local(Monday, "10:00"),
            Address = new AppointmentVisitInput { PostalCode = "7200", AddressLine = "Av. Juárez 123" },
            Problem = new string('a', 501),
            CustomerName = "Valeria",
            CustomerPhone = "5522107765",
            CustomerEmail = "no es un correo"
        });

        Assert.Equal(InvoiceEmailAddress.InvalidMessage, result.Error);
        Assert.Equal(0, await fixture.Context.Appointments.CountAsync());
        Assert.Equal(
            [HomeVisitPlacement.PostalCodeMessage, HomeVisitRules.ProblemTooLongMessage, InvoiceEmailAddress.InvalidMessage],
            validation.Errors.Select(error => error.ErrorMessage));
    }

    [Fact]
    public async Task ItsLink_AndTheAccount_ShouldSayItIsAVisit_ByItsPostalCodeOnly()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var customer = await CustomerAsync(fixture);
        fixture.CurrentUser.UserId = customer.ToString();

        var receipt = (await RequestAsync(fixture, Local(Monday, "10:00"))).Value!;
        var tracked = await new GetPublicAppointmentQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicAppointmentQuery(receipt.TrackingToken), CancellationToken.None);
        var mine = await new GetMyAppointmentsQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
            .Handle(new GetMyAppointmentsQuery(), CancellationToken.None);

        Assert.True(receipt.LinkedToAccount);
        Assert.Equal(("72000", (HomeVisitChargePolicy?)HomeVisitChargePolicy.Charged, (string?)null),
            (tracked!.Visit!.PostalCode, tracked.Visit.ChargePolicy, tracked.Address));
        var listed = Assert.Single(mine.Items);
        Assert.Equal(("72000", (string?)null), (listed.Visit!.PostalCode, listed.Address));
    }

    private static async Task<Tenant> TradeAsync(
        SalesTestFixture fixture,
        string slug = Slug,
        TenantCapability capabilities = TenantCapability.Service | TenantCapability.Appointments,
        bool menu = true,
        bool hours = true,
        (string Name, decimal Fee, string[] PostalCodes)[]? zones = null)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, capabilities, pricesIncludeTax: true, slug: slug);
        tenant.SetBusinessInfo(null, null, HomeTrades.Plumbing, null, null);
        var settings = await SettingsAsync(fixture, tenant);
        settings.StartReceivingHomeVisits();
        if (menu)
            settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        if (hours)
            settings.SetOpeningHours(OpeningHours.Create(MondayToSaturday.Select(day =>
                new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(18, 0)))));
        foreach (var (name, fee, postalCodes) in zones ?? [("Centro", 50m, ["72000"])])
            fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields(name, fee, 0m, null, postalCodes)));
        var main = await fixture.Context.Branches.SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        main.Update(main.Name, main.Code, new BranchContact(), BranchServiceMode.DeliveryOnly);
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Product> ServiceAsync(SalesTestFixture fixture, Tenant tenant, string name, decimal price, int minutes)
    {
        var category = await fixture.Context.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
        if (category is null)
        {
            category = new Category(tenant.Id, "Servicios", null);
            fixture.Context.Categories.Add(category);
        }

        var product = new Product(tenant.Id, category.Id, name, null, price, "MXN", preparationTimeMinutes: minutes, unit: UnitType.Service);
        fixture.Context.Products.Add(product);
        await fixture.Context.SaveChangesAsync();
        return product;
    }

    private static async Task SetVisitAsync(
        SalesTestFixture fixture, Tenant tenant, Product? service, HomeVisitChargePolicy policy = HomeVisitChargePolicy.Charged, bool enabled = true)
    {
        (await SettingsAsync(fixture, tenant)).UpdateHomeVisitRequests(enabled, service?.Id, policy);
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);

    /// <summary>A team member the business books online, who does <paramref name="services"/>.</summary>
    private static async Task<OnlineBookingStaff> StaffAsync(SalesTestFixture fixture, Tenant tenant, string firstName, string name, Guid[] services)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"{firstName.ToLowerInvariant()}@negocio.test",
            UserName = $"{firstName.ToLowerInvariant()}@negocio.test",
            FirstName = firstName,
            LastName = "Equipo",
            Role = UserRole.Staff,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        fixture.Context.TenantUsers.Add(new TenantUser(tenant.Id, user.Id, TenantUserRole.Staff, TenantUserStatus.Active));
        var staff = OnlineBookingStaff.Create(tenant.Id, user.Id, name, true, false, services, null);
        fixture.Context.OnlineBookingStaff.Add(staff);
        await fixture.Context.SaveChangesAsync();
        return staff;
    }

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

    private static async Task ConnectWhatsAppAsync(SalesTestFixture fixture, Guid tenantId)
    {
        var config = new WhatsAppConfig(tenantId);
        config.Update(true, "token", "123456", "waba", "+52 55 0000 0000");
        fixture.Context.WhatsAppConfigs.Add(config);
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<PublicHomeVisitDto?> PageAsync(SalesTestFixture fixture, string slug = Slug)
        => new GetPublicHomeVisitQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock, new PublicDirectoryCache(), NullLogger<GetPublicHomeVisitQueryHandler>.Instance)
            .Handle(new GetPublicHomeVisitQuery(slug), CancellationToken.None);

    private static Task<PublicHomeVisitCoverageDto?> CoverageAsync(SalesTestFixture fixture, string postalCode)
        => new GetPublicHomeVisitCoverageQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPublicHomeVisitCoverageQuery(Slug, postalCode), CancellationToken.None);

    private static Task<PublicBookingSlotsDto?> SlotsAsync(SalesTestFixture fixture, DateOnly? from = null, Guid? staffId = null)
        => new GetPublicHomeVisitSlotsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicHomeVisitSlotsQuery(Slug, from, staffId), CancellationToken.None);

    private static Task<Result<PublicAppointmentReceiptDto>> RequestAsync(
        SalesTestFixture fixture,
        DateTimeOffset startAt,
        string postalCode = "72000",
        string phone = "5522107765",
        Guid? key = null,
        string? email = null,
        Guid? staffId = null,
        decimal price = 50m,
        HomeVisitChargePolicy policy = HomeVisitChargePolicy.Charged)
        => new RequestPublicHomeVisitCommandHandler(
                fixture.Context, fixture.TenantService, fixture.CurrentUser, new PhoneNormalizationService(), fixture.Clock,
                new NoBookingLock(), Notices(fixture))
            .Handle(new RequestPublicHomeVisitCommand
            {
                Slug = Slug,
                StartAt = startAt,
                Address = new AppointmentVisitInput { PostalCode = postalCode, AddressLine = "Av. Juárez 123, Col. Centro", References = "Portón negro" },
                Problem = "Gotea el calentador",
                CustomerName = "Valeria Ruiz",
                CustomerPhone = phone,
                CustomerEmail = email,
                IdempotencyKey = key,
                StaffId = staffId,
                ExpectedChargePolicy = policy,
                ExpectedPrice = price
            }, CancellationToken.None);

    private static Task<Result<AppointmentDto>> AcceptAsync(SalesTestFixture fixture, Guid appointmentId)
        => new AppointmentRequestCommandHandler(fixture.Context, fixture.Clock, Notices(fixture))
            .Handle(new AcceptAppointmentRequestCommand(appointmentId), CancellationToken.None);

    private static AppointmentNotices Notices(SalesTestFixture fixture)
        => new(fixture.Context, new PhoneNormalizationService(), fixture.Clock);

    private sealed class NoBookingLock : IAppointmentBookingLock
    {
        public Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
