using FoodParkDigital.Application.Appointments.RequestNotices;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;
using static FoodParkDigital.Application.Tests.Support.NoticeEmailsSupport;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Reminding a business of its requests online still waiting an hour before their limit to be answered in time (Fase 29U, ADR
/// 0075): which requests are due, who gets the reminder, what it says, and when it is no longer sent.
/// </summary>
public class AnswerReminderNoticesTests
{
    // Now: 2027-03-01 at 11:00 in Mexico City (17:00 UTC).

    [Fact]
    public async Task ThePlanner_ShouldRemindOnlyTheRequestsDue_OnceAndInItsOwnBusiness()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "recuerda", "Estética Recuerda");
        // For two days from now: its limit is a day after it arrived. Due: arrived 23 h 10 min ago.
        var due = await RequestAsync(fixture, salon, Now.AddHours(-23).AddMinutes(-10));
        // Not yet: arrived 22 hours ago. Too late: arrived over a day ago.
        var notYet = await RequestAsync(fixture, salon, Now.AddHours(-22));
        var late = await RequestAsync(fixture, salon, Now.AddHours(-25));
        // For 90 minutes after it arrived: its limit came too soon, the email of its arrival is enough.
        var soon = await RequestAsync(fixture, salon, Now.AddMinutes(-45), startAt: Now.AddMinutes(45));
        // Answered, and one of its own team, both due by their time.
        var answered = await RequestAsync(fixture, salon, Now.AddHours(-23).AddMinutes(-10));
        answered.Accept(Now.AddMinutes(-5));
        var team = await RequestAsync(fixture, salon, Now.AddHours(-23).AddMinutes(-10), fromTeam: true);
        // Another business's request, due too: not this business's pass.
        var other = await SalonAsync(fixture, "otra", "Estética Otra");
        var theirs = await RequestAsync(fixture, other, Now.AddHours(-23).AddMinutes(-10));
        await fixture.Context.SaveChangesAsync();

        fixture.TenantService.SetTenant(salon.Id);
        var planner = new AnswerReminderPlanner(fixture.Context, fixture.Clock);
        Assert.Equal(1, await planner.RemindDueAsync(CancellationToken.None));
        await fixture.Context.SaveChangesAsync();

        var reminded = await fixture.Context.Appointments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.AnswerReminderSentAt != null).Select(a => a.Id).ToListAsync();
        Assert.Equal([due.Id], reminded);
        Assert.Equal(Now, (await fixture.Context.Appointments.AsNoTracking().SingleAsync(a => a.Id == due.Id)).AnswerReminderSentAt);
        Assert.DoesNotContain(theirs.Id, reminded);
        Assert.All(new[] { notYet, late, soon, answered, team }, a => Assert.DoesNotContain(a.Id, reminded));
        // Once: the next pass reminds nothing more.
        Assert.Equal(0, await planner.RemindDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheReminders_ShouldGoToEachMemberWhoGetsTheEmailsOfRequests_WhileTheRequestStillWaits()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "avisos", "Estética Avisos");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        await MemberAsync(fixture, salon, "socia", TenantUserRole.Owner, choose: false);
        var adminOn = await MemberAsync(fixture, salon, "admin", TenantUserRole.Admin, choose: true);
        await MemberAsync(fixture, salon, "cajero", TenantUserRole.Staff, choose: true, permissions: [TenantPermission.ManageOrders]);
        var other = await SalonAsync(fixture, "otro", "Estética Otra");
        await MemberAsync(fixture, other, "otra", TenantUserRole.Owner);
        var request = await RequestAsync(fixture, salon, Now.AddHours(-23).AddMinutes(-10));

        var queued = await QueueAsync(fixture, salon, request);

        Assert.Equal(new[] { owner, adminOn }.Order(), queued.Select(q => q.Event.UserId).Order());
        Assert.All(queued, q => Assert.Equal((request.Id, (Guid?)salon.Id), (q.Event.AppointmentId, q.TenantId)));

        // Answered in the meantime, or its limit passed: nobody is reminded, and an email already queued is not sent.
        request.Accept(Now);
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await QueueAsync(fixture, salon, request));
        Assert.Empty(await SendAsync(fixture, salon, request, owner));
        var lapsed = await RequestAsync(fixture, salon, Now.AddHours(-24));
        Assert.Empty(await QueueAsync(fixture, salon, lapsed));
        Assert.Empty(await SendAsync(fixture, salon, lapsed, owner));
    }

    [Fact]
    public async Task TheReminder_ShouldSayWhatWasAskedAndByWhen_InTheBusinessZone_AndNeverThePhoneOrTheNotes()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "texto", "Estética <Luna> & Sol");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        // Arrived yesterday at 11:50 for Thursday at 13:30: by today at 11:50, in 50 minutes (Mexico City).
        var thursday = new DateTimeOffset(2027, 3, 4, 13, 30, 0, TimeSpan.FromHours(-6));
        var request = Appointment.RequestOnline(
            salon.Id, "Ana <b>\"Leo\"</b>\nRuiz", "+525522107765", Guid.NewGuid(), "Corte & barba", thursday, thursday.AddHours(1),
            "Tengo el cabello muy largo", null, Guid.NewGuid(), new string('t', 43));
        request.CreatedAt = Now.AddHours(-23).AddMinutes(-10);
        fixture.Context.Appointments.Add(request);
        await fixture.Context.SaveChangesAsync();

        var sent = Assert.Single(await SendAsync(fixture, salon, request, owner));

        Assert.Equal($"dueña.{salon.Slug}@demo.com", sent.To);
        Assert.Equal("Responde a tiempo la solicitud de cita en Estética <Luna> & Sol: Corte & barba, jueves 4 de marzo a la 1:30 p. m.", sent.Subject);
        Assert.Contains("<h2>Una solicitud de cita sigue sin respuesta</h2>", sent.Body);
        Assert.Contains(
            "<p><strong>Ana &lt;b&gt;&quot;Leo&quot;&lt;/b&gt; Ruiz</strong> pidió <strong>Corte &amp; barba</strong> en Est&#233;tica &lt;Luna&gt; &amp; Sol para el jueves 4 de marzo a la 1:30 p. m.</p>",
            sent.Body);
        Assert.Contains("<p>Respóndela antes del lunes 1 de marzo a las 11:50 a. m. para que cuente como respondida a tiempo.</p>", sent.Body);
        Assert.Contains($"<a href='{AppUrl}/appointments'>Abrir la Agenda</a>", sent.Body);
        Assert.Contains("un recordatorio de las que siguen sin respuesta", sent.Body);
        Assert.DoesNotContain("5522107765", sent.Body + sent.Subject);
        Assert.DoesNotContain("largo", sent.Body);
    }

    /// <summary>The reminders the outbox queued for <paramref name="request"/> once it was reminded.</summary>
    private static async Task<List<(AppointmentAnswerReminderEmailEvent Event, Guid? TenantId)>> QueueAsync(
        SalesTestFixture fixture, Tenant tenant, Appointment request)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var outbox = new Outbox<AppointmentAnswerReminderEmailEvent>();
        await new AppointmentAnswerDueSoonEmailsHandler(fixture.Context, new MembersAccess(fixture.Context), outbox, fixture.Clock)
            .HandleAsync(new AppointmentAnswerDueSoonEvent(request.Id, tenant.Id), Context(tenant), CancellationToken.None);
        return outbox.Added;
    }

    /// <summary>What the reminder of <paramref name="request"/> to <paramref name="userId"/> sent, run in <paramref name="tenant"/>.</summary>
    private static async Task<List<(string To, string Subject, string Body)>> SendAsync(
        SalesTestFixture fixture, Tenant tenant, Appointment request, Guid userId)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails();
        await new AppointmentAnswerReminderEmailHandler(fixture.Context, new MembersAccess(fixture.Context), emails, new Links(AppUrl), fixture.Clock)
            .HandleAsync(new AppointmentAnswerReminderEmailEvent(request.Id, userId), Context(tenant), CancellationToken.None);
        return emails.Sent;
    }
}
