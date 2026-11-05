using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Answering in time (Fase 29L, ADR 0066): the agenda says by when each request online still waiting is answered in time
/// (the time its customer asked for, or a day after it arrived, whichever comes first), and the bell counts as urgent those
/// to answer today to be in time, or already late.
/// </summary>
public class AnswerInTimeTests
{
    [Fact]
    public async Task TheAgenda_ShouldSayByWhenEachRequestIsAnsweredInTime()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        // For this afternoon: before then.
        var today = await RequestAsync(fixture, salon, Now.AddHours(-2), Now.AddHours(5));
        // For next week: within a day of arriving.
        var nextWeek = await RequestAsync(fixture, salon, Now.AddHours(-2), Now.AddDays(7));
        // Moved to next week: by the time its customer asked for.
        var moved = await RequestAsync(fixture, salon, Now.AddHours(-2), Now.AddHours(3));
        moved.Update("Valeria", "+525522107765", "Corte", Now.AddDays(7).AddHours(2), Now.AddDays(7).AddHours(3), null, null);
        // Accepted: nothing left to answer.
        var accepted = await RequestAsync(fixture, salon, Now.AddHours(-2), Now.AddDays(2));
        accepted.Accept(Now.AddHours(-1));
        await fixture.Context.SaveChangesAsync();

        var agenda = (await new GetAppointmentsQueryHandler(fixture.Context)
                .Handle(new GetAppointmentsQuery { PageSize = 50 }, CancellationToken.None))
            .Items.ToDictionary(a => a.Id, a => a.AnswerBy);

        Assert.Equal(Now.AddHours(5), agenda[today.Id]);
        Assert.Equal(Now.AddHours(22), agenda[nextWeek.Id]);
        Assert.Equal(Now.AddHours(3), agenda[moved.Id]);
        Assert.Null(agenda[accepted.Id]);
        // The answer of a command says the same as the agenda's list.
        foreach (var appointment in await fixture.Context.Appointments.ToListAsync())
            Assert.Equal(agenda[appointment.Id], AppointmentDto.From(appointment).AnswerBy);
    }

    [Fact]
    public async Task TheBell_ShouldCountAsUrgent_TheRequestsToAnswerTodayToBeInTime_OrAlreadyLate()
    {
        // 11:00 in Mexico City: its day ends at 06:00 UTC tomorrow.
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        // For this afternoon.
        await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddHours(3));
        // For next week, arrived yesterday: its day ends this afternoon.
        await RequestAsync(fixture, salon, Now.AddHours(-20), Now.AddDays(5));
        // For next week, arrived two days ago: already late, still to answer.
        await RequestAsync(fixture, salon, Now.AddDays(-2), Now.AddDays(5));
        // For next week, arrived an hour ago: in time until tomorrow at 10:00.
        await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddDays(5));

        var attention = await new GetAttentionQueryHandler(
                fixture.Context, TestAccess.For(fixture.CurrentUser, fixture.TenantService), fixture.CurrentUser, fixture.Clock)
            .Handle(new GetAttentionQuery(), CancellationToken.None);

        var requests = Assert.Single(attention.Items, item => item.Key == AttentionKeys.AppointmentRequests);
        Assert.Equal((4, 3), (requests.Count, requests.UrgentCount));
    }
}
