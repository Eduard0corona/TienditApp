using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The requests that count (Fase 29M, ADR 0067): Explorar does not count those from the business's own team, and counts each
/// customer of a business once (of the requests of one phone, the last one decided); the agenda and the bell give a request
/// of the team no time to answer it in.
/// </summary>
public class RequestsThatCountTests
{
    [Fact]
    public async Task Explorar_ShouldNotCountTheRequestsOfTheTeam()
    {
        await using var fixture = Fixture();
        // Three answered at once, all from its own team: nothing to say.
        var ours = await SalonAsync(fixture, "propia", "Estética Propia");
        for (var n = 0; n < 3; n++)
            await AcceptedAsync(fixture, ours, TimeSpan.FromMinutes(5), fromTeam: true);
        // The same from three customers.
        var theirs = await SalonAsync(fixture, "clientes", "Estética Clientes");
        for (var n = 0; n < 3; n++)
            await AcceptedAsync(fixture, theirs, TimeSpan.FromMinutes(5));

        var cards = await CardsAsync(fixture);

        Assert.Null(cards["propia"].ResponseTime);
        Assert.Equal(ResponseTime.WithinAnHour, cards["clientes"].ResponseTime);
    }

    [Fact]
    public async Task Explorar_ShouldCountACustomerOnce_ByTheirLastRequestDecided()
    {
        await using var fixture = Fixture();
        // One phone sends three, all answered at once: one customer is not enough.
        var repeated = await SalonAsync(fixture, "repetida", "Estética Repetida");
        var same = NewCustomerPhone();
        for (var n = 0; n < 3; n++)
            await AcceptedAsync(fixture, repeated, TimeSpan.FromMinutes(5), arrivedDaysAgo: 3 + n, phone: same);
        // The same phone, before, in another business: there it is one of its three customers.
        var another = await SalonAsync(fixture, "otra", "Estética Otra");
        await AcceptedAsync(fixture, another, TimeSpan.FromMinutes(5), arrivedDaysAgo: 6, phone: same);
        await AcceptedAsync(fixture, another, TimeSpan.FromMinutes(5));
        await AcceptedAsync(fixture, another, TimeSpan.FromMinutes(5));
        // A regular answered at once, then let pass, and waiting since an hour ago for next week; and two more customers, one
        // answered at once, one in 13 hours. The regular counts by the one let pass: one of three within an hour, two within a
        // day. Counted by the first, two of three would be within an hour; with the one still waiting in its place, only two
        // would count.
        var regular = await SalonAsync(fixture, "habitual", "Estética Habitual");
        var phone = NewCustomerPhone();
        await AcceptedAsync(fixture, regular, TimeSpan.FromMinutes(5), arrivedDaysAgo: 5, phone: phone);
        await RequestAsync(fixture, regular, Now.AddDays(-4), Now.AddDays(-2), phone: phone);
        await RequestAsync(fixture, regular, Now.AddHours(-1), Now.AddDays(7), phone: phone);
        await AcceptedAsync(fixture, regular, TimeSpan.FromMinutes(5));
        await AcceptedAsync(fixture, regular, TimeSpan.FromHours(13));
        // Later, one of its own team with the regular's phone, answered at once: it does not take the place of theirs.
        await AcceptedAsync(fixture, regular, TimeSpan.FromMinutes(5), arrivedDaysAgo: 2, phone: phone, fromTeam: true);

        var cards = await CardsAsync(fixture);

        Assert.Null(cards["repetida"].ResponseTime);
        Assert.Equal(ResponseTime.WithinAnHour, cards["otra"].ResponseTime);
        Assert.Equal(ResponseTime.WithinADay, cards["habitual"].ResponseTime);
    }

    [Fact]
    public async Task ACustomer_ShouldBeThePhoneTheRequestArrivedWith_ThoughTheBusinessChangesIt()
    {
        await using var fixture = Fixture();
        // Two customers answered at once and one let pass, who arrived after them. The business then writes the first one's
        // phone on the one it let pass: still three customers, two of three within an hour.
        var edited = await SalonAsync(fixture, "editada", "Estética Editada");
        var first = NewCustomerPhone();
        await AcceptedAsync(fixture, edited, TimeSpan.FromMinutes(5), arrivedDaysAgo: 5, phone: first);
        await AcceptedAsync(fixture, edited, TimeSpan.FromMinutes(5), arrivedDaysAgo: 5);
        var lapsed = await RequestAsync(fixture, edited, Now.AddDays(-4), Now.AddDays(-2));
        lapsed.Update("Valeria", first, "Corte", lapsed.StartAt, lapsed.EndAt, null, null);
        // One phone written with and without the 1 of a Mexican mobile, and another customer: two customers, not enough.
        var written = await SalonAsync(fixture, "escrita", "Estética Escrita");
        var day = 3;
        foreach (var phone in new[] { "+5215533334444", "+525533334444", "+5215533334444" })
            await AcceptedAsync(fixture, written, TimeSpan.FromMinutes(5), arrivedDaysAgo: day++, phone: phone);
        await AcceptedAsync(fixture, written, TimeSpan.FromMinutes(5));
        await fixture.Context.SaveChangesAsync();

        var cards = await CardsAsync(fixture);

        Assert.Equal(ResponseTime.WithinAnHour, cards["editada"].ResponseTime);
        Assert.Null(cards["escrita"].ResponseTime);
    }

    [Fact]
    public async Task TheAgendaAndTheBell_ShouldGiveARequestOfTheTeamNoTimeToAnswerIt()
    {
        // 11:00 in Mexico City.
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        // For next week, arrived two days ago: late, unless it is of the team.
        var late = await RequestAsync(fixture, salon, Now.AddDays(-2), Now.AddDays(5));
        var lateOfTheTeam = await RequestAsync(fixture, salon, Now.AddDays(-2), Now.AddDays(5), fromTeam: true);
        // For this afternoon, of the team: urgent for today all the same.
        var todayOfTheTeam = await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddHours(4), fromTeam: true);

        var agenda = (await new GetAppointmentsQueryHandler(fixture.Context)
                .Handle(new GetAppointmentsQuery { PageSize = 50 }, CancellationToken.None))
            .Items.ToDictionary(a => a.Id);
        var attention = await new GetAttentionQueryHandler(
                fixture.Context, TestAccess.For(fixture.CurrentUser, fixture.TenantService), fixture.CurrentUser, fixture.Clock)
            .Handle(new GetAttentionQuery(), CancellationToken.None);

        Assert.Equal((Now.AddDays(-1), false), (agenda[late.Id].AnswerBy, agenda[late.Id].FromTeam));
        Assert.Equal(((DateTimeOffset?)null, true), (agenda[lateOfTheTeam.Id].AnswerBy, agenda[lateOfTheTeam.Id].FromTeam));
        Assert.Equal(((DateTimeOffset?)null, true), (agenda[todayOfTheTeam.Id].AnswerBy, agenda[todayOfTheTeam.Id].FromTeam));
        // The answer of a command says the same as the agenda's list.
        foreach (var appointment in await fixture.Context.Appointments.ToListAsync())
            Assert.Equal(
                (agenda[appointment.Id].AnswerBy, agenda[appointment.Id].FromTeam),
                (AppointmentDto.From(appointment).AnswerBy, AppointmentDto.From(appointment).FromTeam));
        var requests = Assert.Single(attention.Items, item => item.Key == AttentionKeys.AppointmentRequests);
        Assert.Equal((3, 2), (requests.Count, requests.UrgentCount));
    }

    /// <summary>
    /// A request the business accepted after its customer waited <paramref name="waited"/>, for two days after it arrived
    /// <paramref name="arrivedDaysAgo"/> days ago.
    /// </summary>
    private static async Task AcceptedAsync(
        SalesTestFixture fixture, Domain.Tenants.Entities.Tenant tenant, TimeSpan waited, int arrivedDaysAgo = 3, string? phone = null,
        bool fromTeam = false)
    {
        var arrivedAt = Now.AddDays(-arrivedDaysAgo);
        var request = await RequestAsync(fixture, tenant, arrivedAt, arrivedAt.AddDays(2), phone, fromTeam);
        request.Accept(arrivedAt + waited);
        await fixture.Context.SaveChangesAsync();
    }
}
