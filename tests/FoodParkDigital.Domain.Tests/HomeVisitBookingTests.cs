using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// A visit to quote requested online (Fase 29B, ADR 0053 points 11 and 12): an online request «Por confirmar» at the
/// customer's address that keeps how the business charged it then, and the free times of a visit, which also need the
/// trip before and after it free of other appointments.
/// </summary>
public class HomeVisitBookingTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly TimeZoneInfo Cdmx = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    private static readonly string Token = new('a', 43);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", "Portón negro");
    private static readonly TimeSpan Trip = TimeSpan.FromMinutes(30);

    // Monday 19 October 2026; Mexico City is UTC-6 all year.
    private static readonly DateOnly Monday = new(2026, 10, 19);

    private static DateTimeOffset Local(string time) => new(Monday.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(-6));

    private static DeliveryZone Zone(decimal fee = 80m, string[]? postalCodes = null, bool isActive = true, Guid? tenantId = null)
        => new(tenantId ?? Business, new DeliveryZoneFields("Centro", fee, 0m, null, postalCodes ?? ["72000", "72005"], isActive));

    private static Appointment Visit(
        HomeVisitChargePolicy policy = HomeVisitChargePolicy.Charged,
        DeliveryZone? zone = null,
        string problem = "Gotea el calentador",
        string? email = null,
        Guid? service = null)
        => Appointment.RequestHomeVisit(
            Business, "Valeria", "+525522107765", service, service is null ? HomeVisitRules.DefaultVisitName : "Visita y diagnóstico",
            Local("10:00"), Local("11:00"), problem, email, Address, zone ?? Zone(), policy, null, Guid.NewGuid(), Token);

    private static BusinessSchedule Weekdays() => BusinessSchedule.Create(OpeningHours.Create(
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(14, 0)))), [])!;

    private static List<string> Times(IEnumerable<DateTimeOffset> starts)
        => starts.Select(start => TimeZoneInfo.ConvertTime(start, Cdmx).ToString("HH:mm")).ToList();

    [Fact]
    public void RequestHomeVisit_ShouldHoldItsTime_AtTheCustomersAddress_WithHowItIsCharged()
    {
        var service = Guid.NewGuid();

        var appointment = Visit(service: service, email: "  valeria@correo.mx ");

        Assert.Equal((AppointmentStatus.Requested, AppointmentChannel.Online, Token), (appointment.Status, appointment.Channel, appointment.TrackingToken));
        Assert.Equal((service, "Visita y diagnóstico", "Gotea el calentador"), (appointment.ServiceProductId, appointment.ServiceName, appointment.Notes));
        Assert.Equal("valeria@correo.mx", appointment.CustomerEmail);
        var visit = appointment.Visit!;
        Assert.Equal((Business, appointment.Id), (visit.TenantId, visit.AppointmentId));
        Assert.Equal(("72000", "Av. Juárez 123, Col. Centro", "Portón negro", "Centro", 80m, HomeVisitChargePolicy.Charged),
            (visit.PostalCode, visit.AddressLine, visit.References, visit.ZoneName, visit.Fee, visit.ChargePolicy));
    }

    [Theory]
    [InlineData(HomeVisitChargePolicy.Charged, 80)]
    [InlineData(HomeVisitChargePolicy.Free, 0)]
    [InlineData(HomeVisitChargePolicy.CreditedIfHired, 80)]
    public void RequestHomeVisit_ShouldCharge_WhatGoingThereAdds_NothingSinCosto(HomeVisitChargePolicy policy, decimal fee)
    {
        var appointment = Visit(policy);

        Assert.Equal((fee, policy), (appointment.Visit!.Fee, appointment.Visit.ChargePolicy));
    }

    [Fact]
    public void RequestHomeVisit_WithoutAVisitService_ShouldBeAVisitAnyway()
    {
        var appointment = Visit();

        Assert.Null(appointment.ServiceProductId);
        Assert.Equal(HomeVisitRules.DefaultVisitName, appointment.ServiceName);
        Assert.Null(appointment.CustomerEmail);
    }

    [Fact]
    public void RequestHomeVisit_ShouldRefuse_APostalCodeItsZoneDoesNotReach()
    {
        Assert.Equal(HomeVisitRules.NotCoveredMessage("72000"),
            Assert.Throws<DomainException>(() => Visit(zone: Zone(postalCodes: ["72001"]))).Message);
        Assert.Equal(HomeVisitRules.NotCoveredMessage("72000"),
            Assert.Throws<DomainException>(() => Visit(zone: Zone(isActive: false))).Message);
        // Another business's zone never reaches it.
        Assert.Equal(HomeVisitRules.NotCoveredMessage("72000"),
            Assert.Throws<DomainException>(() => Visit(zone: Zone(tenantId: Guid.NewGuid()))).Message);
    }

    [Theory]
    [InlineData("   ", HomeVisitRules.ProblemRequiredMessage)]
    [InlineData(null, HomeVisitRules.ProblemTooLongMessage)]
    public void RequestHomeVisit_ShouldNeed_WhatHappens(string? problem, string message)
        => Assert.Equal(message, Assert.Throws<DomainException>(() => Visit(problem: problem ?? new string('a', 501))).Message);

    [Fact]
    public void RequestHomeVisit_ShouldRefuse_AnEmailItCannotWriteTo_OrAnEmptyService()
    {
        Assert.Equal(InvoiceEmailAddress.InvalidMessage,
            Assert.Throws<DomainException>(() => Visit(email: "Valeria <valeria@correo.mx>")).Message);
        Assert.Throws<ArgumentException>(() => Visit(service: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Visit((HomeVisitChargePolicy)7));
    }

    [Fact]
    public void AVisitSinCosto_ShouldStayFree_WhereverItMoves()
    {
        var free = Visit(HomeVisitChargePolicy.Free);
        var charged = Visit(HomeVisitChargePolicy.Charged);
        var elsewhere = DeliveryAddress.ForVisit("72005", "Calle 5 de Mayo 8", null);

        Assert.True(free.ArrangeVisit(elsewhere, Zone(fee: 120m), Local("08:00")));
        Assert.True(charged.ArrangeVisit(elsewhere, Zone(fee: 120m), Local("08:00")));

        Assert.Equal((0m, HomeVisitChargePolicy.Free), (free.Visit!.Fee, free.Visit.ChargePolicy));
        Assert.Equal((120m, HomeVisitChargePolicy.Charged), (charged.Visit!.Fee, charged.Visit.ChargePolicy));
    }

    [Fact]
    public void AVisitTheTeamArranges_ShouldKeepNoChargePolicy()
    {
        var appointment = new Appointment(Business, "Valeria", "5522107765", "Revisión", Local("10:00"), Local("11:00"));

        appointment.ArrangeVisit(Address, Zone(), Local("08:00"));

        Assert.Equal((80m, (HomeVisitChargePolicy?)null), (appointment.Visit!.Fee, appointment.Visit.ChargePolicy));
        Assert.Null(appointment.CustomerEmail);
    }

    [Fact]
    public void Slots_OfAVisit_ShouldLeaveTheTripFree_BeforeAndAfter()
    {
        BookedInterval[] booked = [new(Local("10:00"), Local("11:00"))];
        var hour = TimeSpan.FromHours(1);

        var atTheBusiness = BookingSlots.FreeStarts(Weekdays(), Cdmx, Monday, hour, booked, 1, Local("00:00"));
        var visit = BookingSlots.FreeStarts(Weekdays(), Cdmx, Monday, hour, booked, 1, Local("00:00"), Trip);

        Assert.Equal(["09:00", "11:00", "11:30", "12:00", "12:30", "13:00"], Times(atTheBusiness));
        // 9:00 ends at 10:00 and 11:00 starts when the other ends: neither leaves the half hour of the trip.
        Assert.Equal(["11:30", "12:00", "12:30", "13:00"], Times(visit));
        Assert.True(BookingSlots.IsFree(Weekdays(), Cdmx, Local("11:30"), hour, booked, 1, Local("00:00"), around: Trip));
        Assert.False(BookingSlots.IsFree(Weekdays(), Cdmx, Local("11:00"), hour, booked, 1, Local("00:00"), around: Trip));
        // With room for two at once, the other appointment does not take the only one.
        Assert.Contains("09:00", Times(BookingSlots.FreeStarts(Weekdays(), Cdmx, Monday, hour, booked, 2, Local("00:00"), Trip)));
    }

    [Fact]
    public void StaffSlots_OfAVisit_ShouldLeaveEachPersonsTripFree()
    {
        var ana = Guid.NewGuid();
        var luis = Guid.NewGuid();
        StaffCalendar[] staff =
        [
            new(ana, null, [new BookedInterval(Local("10:00"), Local("11:00"))]),
            new(luis, null, [new BookedInterval(Local("12:30"), Local("13:00"))])
        ];
        var hour = TimeSpan.FromHours(1);

        var slots = StaffSlots.FreeStarts(Weekdays(), Cdmx, Monday, hour, staff, [], Local("00:00"), Trip);

        var at = slots.ToDictionary(slot => TimeZoneInfo.ConvertTime(slot.StartAt, Cdmx).ToString("HH:mm"), slot => slot.FreeStaff);
        Assert.Equal([luis], at["09:00"]);
        Assert.Equal([luis], at["11:00"]);
        Assert.Equal([luis], at["10:30"]);
        Assert.Equal([ana], at["11:30"]);
        Assert.Equal([ana], at["13:00"]);
        Assert.Equal([luis], StaffSlots.FreeAt(Weekdays(), Cdmx, Local("09:00"), hour, staff, [], Local("00:00"), around: Trip));
        // An appointment nobody attends yet takes someone, the trip around it too.
        Assert.DoesNotContain(
            StaffSlots.FreeStarts(Weekdays(), Cdmx, Monday, hour, staff, [new BookedInterval(Local("11:30"), Local("12:00"))], Local("00:00"), Trip),
            slot => slot.StartAt == Local("11:00"));
    }
}
