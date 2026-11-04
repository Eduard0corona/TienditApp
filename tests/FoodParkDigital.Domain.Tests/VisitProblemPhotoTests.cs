using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.HomeDelivery;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The photos of what happens a customer sends from the link of the visit they requested online (Fase 29H, ADR 0062): up to
/// five, while the visit is «Por confirmar» or agendada and before its time; never for a booking at the business, a visit
/// the team arranged or the job of a quote. A photo never changes the appointment.
/// </summary>
public class VisitProblemPhotoTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly string Token = new('a', 43);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null);
    private static readonly DateTimeOffset At = new(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(-6));
    private static readonly DateTimeOffset Before = At.AddDays(-1);

    private static DeliveryZone Zone() => new(Business, new DeliveryZoneFields("Centro", 80m, 0m, null, ["72000"], true));

    private static Appointment Requested()
        => Appointment.RequestHomeVisit(
            Business, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, At, At.AddHours(1),
            "Gotea el calentador", null, Address, Zone(), HomeVisitChargePolicy.Charged, null, Guid.NewGuid(), Token);

    private static string Key() => $"orders/{Business}/visit-problem/202610/{Guid.NewGuid()}.webp";

    [Fact]
    public void AddProblemPhoto_ShouldTakeTheNextPlace_OfThisVisit_WithoutChangingTheAppointment()
    {
        var visit = Requested();
        var key = Key();

        var photo = visit.AddProblemPhoto(0, key, Before);
        var next = visit.AddProblemPhoto(1, Key(), Before);

        Assert.Equal((Business, visit.Id, 0, key, Before), (photo.TenantId, photo.AppointmentId, photo.Position, photo.FileKey, photo.AddedAt));
        Assert.Equal(1, next.Position);
        Assert.NotEqual(photo.Id, next.Id);
        Assert.Equal((AppointmentStatus.Requested, "Gotea el calentador", (DateTimeOffset?)null), (visit.Status, visit.Notes, visit.UpdatedAt));
    }

    [Fact]
    public void AddProblemPhoto_ShouldTakeFive_AndRefuseTheSixth()
    {
        var visit = Requested();

        var positions = Enumerable.Range(0, HomeVisitRules.MaxProblemPhotos).Select(n => visit.AddProblemPhoto(n, Key(), Before).Position).ToList();
        var sixth = Assert.Throws<DomainException>(() => visit.AddProblemPhoto(5, Key(), Before));

        Assert.Equal([0, 1, 2, 3, 4], positions);
        Assert.Equal(HomeVisitRules.ProblemPhotosFullMessage, sixth.Message);
        Assert.Equal(HomeVisitRules.ProblemPhotosFullMessage, visit.ProblemPhotoRefusal(5, Before));
        Assert.Null(visit.ProblemPhotoRefusal(4, Before));
    }

    [Fact]
    public void AddProblemPhoto_ShouldBeTaken_WhileItWaits_OrOnceAgendada_BeforeItsTime()
    {
        var accepted = Requested();
        accepted.Accept(Before);
        var confirmed = Requested();
        confirmed.Accept(Before);
        confirmed.ChangeStatus(AppointmentStatus.Confirmed);

        Assert.Equal(0, Requested().AddProblemPhoto(0, Key(), At.AddMinutes(-1)).Position);
        Assert.Equal(0, accepted.AddProblemPhoto(0, Key(), Before).Position);
        Assert.Equal(0, confirmed.AddProblemPhoto(0, Key(), Before).Position);
    }

    [Fact]
    public void AddProblemPhoto_ShouldBeRefused_FromItsTime_AndOnceClosedOrInService()
    {
        var declined = Requested();
        declined.Decline(Before);
        var cancelled = Requested();
        cancelled.CancelByCustomer(Before);
        var inService = Requested();
        inService.Accept(Before);
        inService.ChangeStatus(AppointmentStatus.InService);
        var done = Requested();
        done.Accept(Before);
        done.ChangeStatus(AppointmentStatus.Completed);
        var missed = Requested();
        missed.Accept(Before);
        missed.ChangeStatus(AppointmentStatus.NoShow);

        foreach (var (visit, now) in new[] { (Requested(), At), (Requested(), At.AddHours(2)), (declined, Before), (cancelled, Before), (inService, Before), (done, Before), (missed, Before) })
        {
            var refused = Assert.Throws<DomainException>(() => visit.AddProblemPhoto(0, Key(), now));
            Assert.Equal(HomeVisitRules.ProblemPhotosClosedMessage, refused.Message);
        }
    }

    [Fact]
    public void AddProblemPhoto_ShouldBeOnlyForAVisitRequestedOnline()
    {
        var atTheBusiness = Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte", At, At.AddHours(1), null, null, Guid.NewGuid(), Token);
        var arrangedByTheTeam = new Appointment(Business, "Valeria", "+525522107765", "Visita", At, At.AddHours(1));
        arrangedByTheTeam.ArrangeVisit(Address, Zone(), Before);
        var job = Appointment.ForWork(Business, Guid.NewGuid(), "Valeria", "+525522107765", "Cambio de calentador", At, At.AddHours(2), Requested().Visit);
        var movedToTheBusiness = Requested();
        movedToTheBusiness.MoveToBusiness(Before);

        foreach (var appointment in new[] { atTheBusiness, arrangedByTheTeam, job, movedToTheBusiness })
        {
            var refused = Assert.Throws<DomainException>(() => appointment.AddProblemPhoto(0, Key(), Before));
            Assert.Equal(HomeVisitRules.ProblemPhotosNotAVisitMessage, refused.Message);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddProblemPhoto_ShouldNeedTheKeyOfItsFile(string key)
    {
        Assert.Throws<ArgumentException>(() => Requested().AddProblemPhoto(0, key, Before));
        Assert.Throws<ArgumentException>(() => Requested().AddProblemPhoto(0, new string('k', AppointmentProblemPhoto.MaxFileKeyLength + 1), Before));
        Assert.Throws<ArgumentOutOfRangeException>(() => Requested().ProblemPhotoRefusal(-1, Before));
    }
}
