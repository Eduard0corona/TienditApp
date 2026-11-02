using FoodParkDigital.Domain.Appointments;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.HomeDelivery;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The appointments that do the job of a service order that came from an accepted quote (Fase 29F, ADR 0053 point 17): agendadas
/// by the team at the place of the visit, with nothing to charge to go there, and never charged with a sale of their own.
/// </summary>
public class AppointmentWorkTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly Guid WorkOrder = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", "Portón negro");

    private static DeliveryZone Zone(decimal fee = 80m, string[]? postalCodes = null, bool isActive = true)
        => new(Business, new DeliveryZoneFields("Centro", fee, 0m, null, postalCodes ?? ["72000", "72005"], isActive));

    /// <summary>The visit the quote came from, requested online and charged «Se descuenta si contratas».</summary>
    private static Appointment QuotedVisit(DeliveryZone? zone = null)
        => Appointment.RequestHomeVisit(
            Business, "Valeria Ruiz", "+525522107765", null, HomeVisitRules.DefaultVisitName, Start.AddDays(-7), Start.AddDays(-7).AddHours(1),
            "Gotea el calentador", null, Address, zone ?? Zone(), HomeVisitChargePolicy.CreditedIfHired, null, Guid.NewGuid(),
            new string('a', 43));

    private static Appointment Work(AppointmentVisit? at = null, Guid? staff = null, string? staffName = null)
        => Appointment.ForWork(
            Business, WorkOrder, "Valeria Ruiz", "+525522107765", AppointmentWorkRules.ServiceName("COT-000012"),
            Start, Start.AddHours(3), at, staffName, staff, "Llevar la escalera");

    [Fact]
    public void ForWork_ShouldBeAgendadaByTheTeam_ForItsOrder()
    {
        var staff = Guid.NewGuid();

        var appointment = Work(staff: staff, staffName: "Luis");

        Assert.Equal((AppointmentStatus.Scheduled, AppointmentChannel.Business), (appointment.Status, appointment.Channel));
        Assert.Equal((WorkOrder, true), (appointment.WorkOrderId, appointment.IsWork));
        Assert.Equal(("Trabajo de la cotización COT-000012", "Llevar la escalera"), (appointment.ServiceName, appointment.Notes));
        Assert.Equal((staff, "Luis", (Guid?)null), (appointment.StaffUserId, appointment.StaffName, appointment.ServiceProductId));
        Assert.Equal((Start, Start.AddHours(3)), (appointment.StartAt, appointment.EndAt));
        Assert.Null(appointment.Visit);
        Assert.Null(appointment.TrackingToken);
    }

    [Fact]
    public void ForWork_ShouldHappenWhereTheCustomerWasVisited_WithNothingToChargeToGoThere()
    {
        var visit = QuotedVisit(Zone(fee: 120m)).Visit!;

        var appointment = Work(visit);

        var place = appointment.Visit!;
        Assert.NotSame(visit, place);
        Assert.Equal((appointment.Id, Business), (place.AppointmentId, place.TenantId));
        Assert.Equal(("72000", "Av. Juárez 123, Col. Centro", "Portón negro", "Centro"),
            (place.PostalCode, place.AddressLine, place.References, place.ZoneName));
        Assert.Equal((0m, (HomeVisitChargePolicy?)null), (place.Fee, place.ChargePolicy));
        Assert.True(appointment.IsHomeVisit);
    }

    [Fact]
    public void ForWork_ShouldNeedItsOrder_AndAVisitOfItsBusiness()
    {
        var otherBusiness = Guid.NewGuid();
        var elsewhere = Appointment.RequestHomeVisit(
            otherBusiness, "Valeria Ruiz", "+525522107765", null, HomeVisitRules.DefaultVisitName, Start, Start.AddHours(1),
            "Gotea", null, Address, new DeliveryZone(otherBusiness, new DeliveryZoneFields("Otra", 0m, 0m, null, ["72000"], true)),
            HomeVisitChargePolicy.Free, null, Guid.NewGuid(), new string('b', 43));

        Assert.Throws<ArgumentException>(() => Appointment.ForWork(
            Business, Guid.Empty, "Valeria Ruiz", "+525522107765", "Trabajo", Start, Start.AddHours(1), null));
        Assert.Throws<ArgumentException>(() => Appointment.ForWork(
            Business, WorkOrder, "Valeria Ruiz", "+525522107765", "Trabajo", Start, Start.AddHours(1), elsewhere.Visit));
        Assert.Equal("La fecha de fin debe ser mayor a la fecha de inicio.", Assert.Throws<DomainException>(() => Appointment.ForWork(
            Business, WorkOrder, "Valeria Ruiz", "+525522107765", "Trabajo", Start, Start, null)).Message);
    }

    [Fact]
    public void AWorkAppointment_ShouldNeverBeChargedWithASaleOfItsOwn()
    {
        var appointment = Work(QuotedVisit().Visit);

        Assert.Equal(AppointmentWorkRules.ChargedInItsOrderMessage,
            Assert.Throws<DomainException>(() => appointment.RecordSale(Guid.NewGuid())).Message);
        Assert.Equal(AppointmentWorkRules.ChargedInItsOrderMessage,
            Assert.Throws<DomainException>(() => appointment.RecordSale(Guid.NewGuid(), previousSaleCancelled: true)).Message);
        Assert.Equal((AppointmentStatus.Scheduled, (Guid?)null), (appointment.Status, appointment.SaleOrderId));
    }

    [Fact]
    public void AWorkAppointmentMovedElsewhere_ShouldStillChargeNothingToGoThere_ButNeedAZoneThatReachesIt()
    {
        var appointment = Work(QuotedVisit().Visit);
        var elsewhere = DeliveryAddress.ForVisit("72005", "Calle 5 de Mayo 40", null);

        Assert.True(appointment.ArrangeVisit(elsewhere, Zone(fee: 150m), Start.AddDays(-1)));

        Assert.Equal(("72005", 0m), (appointment.Visit!.PostalCode, appointment.Visit.Fee));
        Assert.Equal(HomeVisitRules.NotCoveredMessage("72100"), Assert.Throws<DomainException>(() => appointment.ArrangeVisit(
            DeliveryAddress.ForVisit("72100", "Otra calle 1", null), Zone(fee: 150m), Start.AddDays(-1))).Message);
    }

    [Fact]
    public void AWorkAppointmentArrangedLater_ShouldChargeNothingToGoThere_WhileAnyOtherChargesTheZone()
    {
        var work = Work();
        var other = new Appointment(Business, "Valeria Ruiz", "+525522107765", "Revisión", Start, Start.AddHours(1));

        work.ArrangeVisit(Address, Zone(fee: 150m), Start.AddDays(-1));
        other.ArrangeVisit(Address, Zone(fee: 150m), Start.AddDays(-1));

        Assert.Equal((0m, 150m), (work.Visit!.Fee, other.Visit!.Fee));
        Assert.False(other.IsWork);
    }

    [Fact]
    public void AWorkAppointment_ShouldLastUpTo12Hours_WhenAgendadoAndWhenMoved_WhileAnyOtherLastsWhatItLasts()
    {
        var work = Work();
        var other = new Appointment(Business, "Valeria Ruiz", "+525522107765", "Retiro", Start, Start.AddHours(13));

        Assert.Equal(AppointmentWorkRules.TooLongMessage, Assert.Throws<DomainException>(() => Appointment.ForWork(
            Business, WorkOrder, "Valeria Ruiz", "+525522107765", "Trabajo", Start, Start.AddHours(12).AddMinutes(1), null)).Message);
        Assert.Equal(AppointmentWorkRules.TooLongMessage, Assert.Throws<DomainException>(() => work.Update(
            work.CustomerName, work.CustomerPhone, work.ServiceName, Start, Start.AddHours(12).AddMinutes(1), null, null)).Message);
        work.Update(work.CustomerName, work.CustomerPhone, work.ServiceName, Start, Start.AddHours(12), null, null);
        other.Update(other.CustomerName, other.CustomerPhone, other.ServiceName, Start, Start.AddHours(14), null, null);

        Assert.Equal((Start.AddHours(12), Start.AddHours(14)), (work.EndAt, other.EndAt));
    }

    [Fact]
    public void AWorkAppointment_ShouldMoveConfirmAndClose_LikeAnyAppointmentOfTheTeam()
    {
        var appointment = Work(QuotedVisit().Visit);

        appointment.ChangeStatus(AppointmentStatus.Confirmed);
        appointment.ChangeStatus(AppointmentStatus.Completed);

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Throws<DomainException>(() => appointment.ChangeStatus(AppointmentStatus.Scheduled));
    }
}
