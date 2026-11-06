using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Answering in time (Fase 29L, ADR 0066): the agenda says by when each request online still waiting is answered in time
/// (the time its customer asked for, or a day after it arrived, whichever comes first), the bell counts as urgent those for
/// today and those to answer today to be in time, or already late, and Explorar counts as the slowest those the agenda says
/// are late.
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
        // Arrived before Fase 29J kept the time asked for: by its own time, this evening.
        var older = await RequestAsync(fixture, salon, Now.AddHours(-2), Now.AddHours(6));
        fixture.Context.Entry(older).Property(nameof(Appointment.RequestedStartAt)).CurrentValue = null;
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
        Assert.Equal(Now.AddHours(6), agenda[older.Id]);
        Assert.Null(agenda[accepted.Id]);
        // The answer of a command says the same as the agenda's list.
        foreach (var appointment in await fixture.Context.Appointments.ToListAsync())
            Assert.Equal(agenda[appointment.Id], AppointmentDto.From(appointment).AnswerBy);
    }

    [Fact]
    public async Task TheRequestsToConfirm_ShouldReadByWhenEachRunsOut_AndThoseOfTheTeamLastByTheirTime()
    {
        // Fase 29P (ADR 0070): «Por confirmar» in the agenda opens with the one to answer first.
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "orden", "Estética Orden");
        var nextWeek = await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddDays(5));
        var thisAfternoon = await RequestAsync(fixture, salon, Now.AddHours(-2), Now.AddHours(5));
        var late = await RequestAsync(fixture, salon, Now.AddDays(-2), Now.AddDays(6));
        // Arrived after the one of yesterday, but runs out before it: in three hours, against a day after it arrived, in four.
        var yesterday = await RequestAsync(fixture, salon, Now.AddHours(-20), Now.AddDays(4));
        var inThreeHours = await RequestAsync(fixture, salon, Now.AddMinutes(-30), Now.AddHours(3));
        var teamTomorrow = await RequestAsync(fixture, salon, Now.AddDays(-3), Now.AddHours(20), fromTeam: true);
        var teamThisAfternoon = await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddHours(2), fromTeam: true);
        var accepted = await RequestAsync(fixture, salon, Now.AddHours(-2), Now.AddHours(1));
        accepted.Accept(Now.AddHours(-1));
        await fixture.Context.SaveChangesAsync();
        var handler = new GetAppointmentsQueryHandler(fixture.Context);

        var requests = await handler.Handle(
            new GetAppointmentsQuery { Status = Domain.Appointments.Enums.AppointmentStatus.Requested, PageSize = 50 }, CancellationToken.None);
        var all = await handler.Handle(new GetAppointmentsQuery { PageSize = 50 }, CancellationToken.None);

        // Not by when each arrived (late, yesterday, thisAfternoon, nextWeek, inThreeHours), nor by its time.
        Assert.Equal(
            [late.Id, inThreeHours.Id, yesterday.Id, thisAfternoon.Id, nextWeek.Id, teamThisAfternoon.Id, teamTomorrow.Id],
            requests.Items.Select(a => a.Id));
        // The rest of the agenda still reads by time.
        Assert.Equal(
            [accepted.Id, teamThisAfternoon.Id, inThreeHours.Id, thisAfternoon.Id, teamTomorrow.Id, yesterday.Id, nextWeek.Id, late.Id],
            all.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task TheAgenda_ShouldBreakTiesByTimeAndThenById_SoThatAPageNeverRepeatsOrSkipsOne()
    {
        // Fase 29P (ADR 0070). Each list is added in the reverse of the order it should read in.
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "empates", "Estética Empates");
        // Arrived together for after tomorrow, with the same limit (a day after arriving): by their time, not their id.
        var later = Waiting(salon.Id, Now.AddDays(3).AddHours(2), TiedId(1));
        var sooner = Waiting(salon.Id, Now.AddDays(3), TiedId(2));
        fixture.Context.Appointments.AddRange(later, sooner);
        // Three at the same time tomorrow and three at the same time yesterday: by their id.
        foreach (var n in new[] { 6, 5, 4 })
            fixture.Context.Appointments.Add(Booked(salon.Id, Now.AddDays(1), TiedId(n)));
        foreach (var n in new[] { 9, 8, 7 })
            fixture.Context.Appointments.Add(Booked(salon.Id, Now.AddDays(-1), TiedId(n)));
        await fixture.Context.SaveChangesAsync();
        var handler = new GetAppointmentsQueryHandler(fixture.Context);

        var requests = await handler.Handle(
            new GetAppointmentsQuery { Status = Domain.Appointments.Enums.AppointmentStatus.Requested, FromDate = Now, PageSize = 50 },
            CancellationToken.None);

        Assert.Equal([sooner.Id, later.Id], requests.Items.Select(a => a.Id));
        // One per page: each once, by id, in the agenda and in the past.
        Assert.Equal([TiedId(4), TiedId(5), TiedId(6)],
            await OnePerPageAsync(handler, new GetAppointmentsQuery { FromDate = Now.AddHours(12), ToDate = Now.AddDays(2) }));
        Assert.Equal([TiedId(7), TiedId(8), TiedId(9)], await OnePerPageAsync(handler, new GetAppointmentsQuery { ToDate = Now }));
    }

    [Fact]
    public async Task TheBell_ShouldCountAsUrgent_TheRequestsForToday_ToAnswerTodayToBeInTime_OrAlreadyLate()
    {
        // 11:00 in Mexico City: its day ends a tick before 06:00 UTC tomorrow.
        await using var fixture = Fixture();
        var endOfToday = new DateTimeOffset(2027, 3, 2, 6, 0, 0, TimeSpan.Zero).AddTicks(-1);
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        // For this afternoon.
        await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddHours(3));
        // For next week, arrived yesterday: its day ends this afternoon.
        await RequestAsync(fixture, salon, Now.AddHours(-20), Now.AddDays(5));
        // For next week, arrived two days ago: already late, still to answer.
        await RequestAsync(fixture, salon, Now.AddDays(-2), Now.AddDays(5));
        // For next week, arrived an hour ago: in time until tomorrow at 10:00.
        await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddDays(5));
        // Asked for this afternoon, moved to next week: by the time its customer asked for.
        var movedLater = await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddHours(3));
        movedLater.Update("Valeria", "+525522107765", "Corte", Now.AddDays(5), Now.AddDays(5).AddHours(1), null, null);
        // Asked for next week, moved to this afternoon: for today, though its limit is tomorrow.
        var movedToday = await RequestAsync(fixture, salon, Now.AddHours(-1), Now.AddDays(5));
        movedToday.Update("Valeria", "+525522107765", "Corte", Now.AddHours(4), Now.AddHours(5), null, null);
        // Its day ends as today does; a minute later, tomorrow.
        await RequestAsync(fixture, salon, endOfToday.AddDays(-1), Now.AddDays(5));
        await RequestAsync(fixture, salon, endOfToday.AddDays(-1).AddMinutes(1), Now.AddDays(5));
        await fixture.Context.SaveChangesAsync();

        var attention = await new GetAttentionQueryHandler(
                fixture.Context, TestAccess.For(fixture.CurrentUser, fixture.TenantService), fixture.CurrentUser, fixture.Clock)
            .Handle(new GetAttentionQuery(), CancellationToken.None);

        var requests = Assert.Single(attention.Items, item => item.Key == AttentionKeys.AppointmentRequests);
        Assert.Equal((8, 6), (requests.Count, requests.UrgentCount));
    }

    [Fact]
    public async Task Explorar_ShouldCountAsTheSlowest_ExactlyTheRequestsTheAgendaSaysAreLate()
    {
        await using var fixture = Fixture();
        // Two quick answers each, the late one also one in 13 hours, and two requests still waiting: at their limit now, or a
        // minute before it.
        var late = await SalonAsync(fixture, "atrasada", "Estética Atrasada");
        var inTime = await SalonAsync(fixture, "a-tiempo", "Estética a Tiempo");
        foreach (var salon in new[] { late, inTime })
        {
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(10));
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));
        }
        await AnsweredAsync(fixture, late, TimeSpan.FromHours(13));
        // By the time asked for, and by a day after arriving.
        Appointment[] waiting =
        [
            await RequestAsync(fixture, late, Now.AddHours(-2), Now),
            await RequestAsync(fixture, late, Now.AddDays(-1), Now.AddDays(5)),
            await RequestAsync(fixture, inTime, Now.AddHours(-2), Now.AddMinutes(1)),
            await RequestAsync(fixture, inTime, Now.AddDays(-1).AddMinutes(1), Now.AddDays(5)),
        ];

        Assert.Equal(
            [Now, Now, Now.AddMinutes(1), Now.AddMinutes(1)],
            waiting.Select(a => AppointmentDto.From(a).AnswerBy!.Value));
        var cards = await CardsAsync(fixture);
        // Both late ones count as the slowest: two of five within an hour, three within a day. Without either, two of four
        // would be within an hour.
        Assert.Equal(ResponseTime.WithinADay, cards["atrasada"].ResponseTime);
        // Neither one in time counts yet: two answers say nothing. Either one counted, two of three would be within an hour.
        Assert.Null(cards["a-tiempo"].ResponseTime);
    }

    /// <summary>
    /// An id that reads in the same place by <paramref name="n"/> in .NET and in SQL Server: they differ only in their last bytes,
    /// the ones SQL Server compares first.
    /// </summary>
    internal static Guid TiedId(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    /// <summary>A request online still waiting, that arrived an hour ago, with its id.</summary>
    private static Appointment Waiting(Guid tenantId, DateTimeOffset startAt, Guid id)
    {
        var request = Appointment.RequestOnline(
            tenantId, "Valeria", NewCustomerPhone(), Guid.NewGuid(), "Corte", startAt, startAt.AddHours(1), null, null, Guid.NewGuid(),
            (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43]);
        request.CreatedAt = Now.AddHours(-1);
        return WithId(request, id);
    }

    /// <summary>An appointment the business booked, with its id.</summary>
    private static Appointment Booked(Guid tenantId, DateTimeOffset startAt, Guid id)
        => WithId(new Appointment(tenantId, "Valeria", "5522107765", "Corte", startAt, startAt.AddHours(1)), id);

    private static Appointment WithId(Appointment appointment, Guid id)
    {
        typeof(Domain.Common.BaseEntity).GetProperty(nameof(Domain.Common.BaseEntity.Id))!.SetValue(appointment, id);
        return appointment;
    }

    /// <summary>Reads <paramref name="query"/> one appointment per page, to its end.</summary>
    private static async Task<List<Guid>> OnePerPageAsync(GetAppointmentsQueryHandler handler, GetAppointmentsQuery query)
    {
        var ids = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var read = await handler.Handle(query with { PageNumber = page, PageSize = 1 }, CancellationToken.None);
            if (read.Items.Count == 0)
                return ids;
            ids.AddRange(read.Items.Select(a => a.Id));
        }
    }
}
