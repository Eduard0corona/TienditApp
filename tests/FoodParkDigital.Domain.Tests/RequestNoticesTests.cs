using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Telling the business of each request (Fase 29N, ADR 0068): a request online or of a visit says it arrived, with its kind
/// and whether it came from the team; each member chooses whether they get an email with each one, and until they choose
/// only the owners do.
/// </summary>
public class RequestNoticesTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly string Token = new('a', 43);
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 10, 0, 0, TimeSpan.FromHours(-6));

    [Fact]
    public void ARequest_ShouldSayItArrived_WithItsKindAndWhetherItCameFromTheTeam()
    {
        var online = Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", Start, Start.AddHours(1),
            null, null, Guid.NewGuid(), Token);
        var visit = Appointment.RequestHomeVisit(
            Business, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, Start, Start.AddHours(1), "Gotea", null,
            DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null),
            new DeliveryZone(Business, new DeliveryZoneFields("Centro", 0m, 0m, null, ["72000"], true)),
            HomeVisitChargePolicy.Free, null, Guid.NewGuid(), Token, fromTeam: true);
        var ofTheTeam = Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", Start, Start.AddHours(1),
            null, null, Guid.NewGuid(), Token, fromTeam: true);

        Assert.Equal(new AppointmentRequestedEvent(online.Id, Business, false, false), Assert.Single(online.DomainEvents), new IgnoringStamps());
        Assert.Equal(new AppointmentRequestedEvent(visit.Id, Business, true, true), Assert.Single(visit.DomainEvents), new IgnoringStamps());
        Assert.Equal(new AppointmentRequestedEvent(ofTheTeam.Id, Business, false, true), Assert.Single(ofTheTeam.DomainEvents), new IgnoringStamps());
        // Answering it says nothing more; one the team books in the agenda was never requested.
        online.Accept(Start.AddHours(-20));
        Assert.Single(online.DomainEvents);
        Assert.Empty(new Appointment(Business, "Valeria", "5522107765", "Corte", Start, Start.AddHours(1)).DomainEvents);
    }

    [Theory]
    [InlineData(TenantUserRole.Owner, true)]
    [InlineData(TenantUserRole.Admin, false)]
    [InlineData(TenantUserRole.Staff, false)]
    public void UntilAMemberChooses_OnlyTheOwnersShouldGetTheEmails(TenantUserRole role, bool gets)
    {
        var member = new TenantUser(Business, Guid.NewGuid(), role);

        Assert.Null(member.RequestEmails);
        Assert.Equal(gets, member.GetsRequestEmails);
    }

    [Fact]
    public void WhatAMemberChooses_ShouldHold_ThoughTheirRoleChanges()
    {
        var owner = new TenantUser(Business, Guid.NewGuid(), TenantUserRole.Owner);
        var staff = new TenantUser(Business, Guid.NewGuid(), TenantUserRole.Staff);
        var unchosen = new TenantUser(Business, Guid.NewGuid(), TenantUserRole.Owner);

        owner.ChooseRequestEmails(false);
        staff.ChooseRequestEmails(true);
        owner.SetRole(TenantUserRole.Admin);
        owner.SetRole(TenantUserRole.Owner);
        staff.SetRole(TenantUserRole.Admin);
        // Not chosen: it follows the role.
        unchosen.SetRole(TenantUserRole.Admin);

        Assert.Equal((false, false), (owner.RequestEmails, owner.GetsRequestEmails));
        Assert.Equal((true, true), (staff.RequestEmails, staff.GetsRequestEmails));
        Assert.Equal(((bool?)null, false), (unchosen.RequestEmails, unchosen.GetsRequestEmails));
    }

    /// <summary>Compares two request events by what they say, not by their id or when they were raised.</summary>
    private sealed class IgnoringStamps : IEqualityComparer<Common.DomainEvent>
    {
        public bool Equals(Common.DomainEvent? x, Common.DomainEvent? y)
            => x is not null && y is not null && x with { EventId = Guid.Empty, OccurredOn = default } == y with { EventId = Guid.Empty, OccurredOn = default };

        public int GetHashCode(Common.DomainEvent obj) => 0;
    }
}
