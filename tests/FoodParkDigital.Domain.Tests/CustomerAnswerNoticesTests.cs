using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Quotes.Events;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Telling the business what its customers answer online (Fase 29O, ADR 0069): an appointment its customer cancels says so,
/// with its kind, whether it came from the team and whether it still waited for an answer; a quote the customer accepts or
/// rejects from its link says so once. Each member chooses whether they get an email with each answer to a quote, and until
/// they choose only the owners do.
/// </summary>
public class CustomerAnswerNoticesTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly string Token = new('a', 43);
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 10, 0, 0, TimeSpan.FromHours(-6));
    private static readonly DateTimeOffset Before = Start.AddHours(-20);
    private static readonly DateOnly Today = new(2026, 11, 5);
    private static readonly QuoteCustomer Valeria = new(null, "Valeria Ruiz", "+525522107765", null);

    [Fact]
    public void ACancellationByItsCustomer_ShouldSaySo_WithItsKindWhetherItCameFromTheTeamAndWhetherItWaitedForAnAnswer()
    {
        var request = Online();
        var accepted = Online();
        accepted.Accept(Before);
        var visit = Appointment.RequestHomeVisit(
            Business, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, Start, Start.AddHours(1), "Gotea", null,
            DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null),
            new DeliveryZone(Business, new DeliveryZoneFields("Centro", 0m, 0m, null, ["72000"], true)),
            HomeVisitChargePolicy.Free, null, Guid.NewGuid(), Token, fromTeam: true);

        request.CancelByCustomer(Before);
        accepted.CancelByCustomer(Before);
        visit.CancelByCustomer(Before);

        Assert.Equal(new AppointmentCancelledByCustomerEvent(request.Id, Business, false, false, true), request.DomainEvents.Last(), new IgnoringStamps());
        Assert.Equal(new AppointmentCancelledByCustomerEvent(accepted.Id, Business, false, false, false), accepted.DomainEvents.Last(), new IgnoringStamps());
        Assert.Equal(new AppointmentCancelledByCustomerEvent(visit.Id, Business, true, true, true), visit.DomainEvents.Last(), new IgnoringStamps());
        Assert.All([request, accepted, visit], a => Assert.Equal(2, a.DomainEvents.Count));
    }

    [Fact]
    public void ACancellationThatIsNotItsCustomers_OrIsRefused_ShouldSayNothing()
    {
        var declined = Online();
        declined.Decline(Before);
        var started = Online();
        var booked = new Appointment(Business, "Valeria", "5522107765", "Corte", Start, Start.AddHours(1));

        Assert.Throws<DomainException>(() => started.CancelByCustomer(Start));
        Assert.Throws<DomainException>(() => booked.CancelByCustomer(Before));

        // Only the request each one was says anything.
        Assert.IsType<AppointmentRequestedEvent>(Assert.Single(declined.DomainEvents));
        Assert.IsType<AppointmentRequestedEvent>(Assert.Single(started.DomainEvents));
        Assert.Empty(booked.DomainEvents);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAnswerFromTheLink_ShouldTellTheBusiness_OnceWhenItChangesTheQuote(bool accepted)
    {
        var quote = Sent();

        Assert.True(quote.ReplyFromLink(accepted, Today, "Los espero el jueves", Before));
        // The same answer again changes nothing, and says nothing more.
        Assert.False(quote.ReplyFromLink(accepted, Today, null, Before));

        Assert.Equal(new QuoteAnsweredFromLinkEvent(quote.Id, Business, accepted), Assert.Single(quote.DomainEvents), new IgnoringStamps());
    }

    [Fact]
    public void AnAnswerTheBusinessRecords_OrOneRefused_ShouldSayNothing()
    {
        var byPhone = Sent();
        var rejectedByPhone = Sent();
        var expired = Sent(validityDays: 1);

        byPhone.Accept(Today, QuoteReplyChannel.Phone, "Por teléfono", Before);
        rejectedByPhone.Reject(Today, QuoteReplyChannel.WhatsApp, null, Before);
        Assert.Throws<DomainException>(() => expired.ReplyFromLink(true, Today.AddDays(5), null, Before));
        Assert.Throws<DomainException>(() => byPhone.ReplyFromLink(false, Today, null, Before));

        Assert.Empty(byPhone.DomainEvents);
        Assert.Empty(rejectedByPhone.DomainEvents);
        Assert.Empty(expired.DomainEvents);
    }

    [Theory]
    [InlineData(TenantUserRole.Owner, true)]
    [InlineData(TenantUserRole.Admin, false)]
    [InlineData(TenantUserRole.Staff, false)]
    public void UntilAMemberChooses_OnlyTheOwnersShouldGetTheEmailsOfTheAnswersToQuotes(TenantUserRole role, bool gets)
    {
        var member = new TenantUser(Business, Guid.NewGuid(), role);

        Assert.Null(member.QuoteReplyEmails);
        Assert.Equal(gets, member.GetsQuoteReplyEmails);
    }

    [Fact]
    public void WhatAMemberChoosesForTheAnswersToQuotes_ShouldHold_ApartFromTheAgendasAndThoughTheirRoleChanges()
    {
        var owner = new TenantUser(Business, Guid.NewGuid(), TenantUserRole.Owner);
        var staff = new TenantUser(Business, Guid.NewGuid(), TenantUserRole.Staff);

        owner.ChooseQuoteReplyEmails(false);
        staff.ChooseQuoteReplyEmails(true);
        owner.SetRole(TenantUserRole.Admin);
        owner.SetRole(TenantUserRole.Owner);
        staff.SetRole(TenantUserRole.Admin);

        Assert.Equal((false, false), (owner.QuoteReplyEmails, owner.GetsQuoteReplyEmails));
        Assert.Equal((true, true), (staff.QuoteReplyEmails, staff.GetsQuoteReplyEmails));
        // The agenda's notices are another choice: the owner still gets them, the staff member still does not.
        Assert.Equal(((bool?)null, true), (owner.RequestEmails, owner.GetsRequestEmails));
        Assert.Equal(((bool?)null, false), (staff.RequestEmails, staff.GetsRequestEmails));
    }

    private static Appointment Online()
        => Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", Start, Start.AddHours(1),
            null, null, Guid.NewGuid(), Token);

    private static Quote Sent(int validityDays = 15)
    {
        var quote = Quote.Start(Business, Folios.QuoteNumber(7), Valeria, null, true, validityDays, Before);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Before);
        quote.Send(Today, Token, null, Before);
        return quote;
    }

    /// <summary>Compares two events by what they say, not by their id or when they were raised.</summary>
    private sealed class IgnoringStamps : IEqualityComparer<DomainEvent>
    {
        public bool Equals(DomainEvent? x, DomainEvent? y)
            => x is not null && y is not null && x with { EventId = Guid.Empty, OccurredOn = default } == y with { EventId = Guid.Empty, OccurredOn = default };

        public int GetHashCode(DomainEvent obj) => 0;
    }
}
