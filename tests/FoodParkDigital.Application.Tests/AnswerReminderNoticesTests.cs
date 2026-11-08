using FoodParkDigital.Application.Appointments.RequestNotices;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Orders.HomeDelivery;
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
    public async Task ThePlanner_ShouldRemindAsLongBeforeAsItsBusinessPicked_AndNothingWhenItPickedNone()
    {
        await using var fixture = Fixture();
        // Another business wants none (Fase 30B, ADR 0082): each pass reads its own business's choice.
        var other = await SalonAsync(fixture, "ninguno", "Estética Ninguno");
        await ReminderAsync(fixture, other, 0);
        var salon = await SalonAsync(fixture, "anticipa", "Estética Anticipa");
        await ReminderAsync(fixture, salon, 120);
        // For two days from now: its limit is a day after it arrived. Two hours before it: arrived 22 h 10 min ago.
        var due = await RequestAsync(fixture, salon, Now.AddHours(-22).AddMinutes(-10));
        var notYet = await RequestAsync(fixture, salon, Now.AddHours(-21).AddMinutes(-50));
        await fixture.Context.SaveChangesAsync();

        fixture.TenantService.SetTenant(salon.Id);
        var planner = new AnswerReminderPlanner(fixture.Context, fixture.Clock);
        Assert.Equal(1, await planner.RemindDueAsync(CancellationToken.None));
        await fixture.Context.SaveChangesAsync();
        Assert.Equal([due.Id], await RemindedAsync(fixture));

        Assert.DoesNotContain(notYet.Id, await RemindedAsync(fixture));

        // It picks none: one due by the hour of before, or by any lead, is not reminded.
        await ReminderAsync(fixture, salon, 0);
        await RequestAsync(fixture, salon, Now.AddHours(-23).AddMinutes(-10));
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(0, await planner.RemindDueAsync(CancellationToken.None));
        await fixture.Context.SaveChangesAsync();
        Assert.Equal([due.Id], await RemindedAsync(fixture));
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

    [Fact]
    public async Task TheReminderOfAVisit_ShouldSaySo_ByTheTimeItsCustomerAskedFor_ThoughTheBusinessMovedIt()
    {
        await using var fixture = Fixture();
        var (trade, zone) = await TradeAsync(fixture, "plomeria", "Plomería Rápida");
        var owner = await MemberAsync(fixture, trade, "dueño", TenantUserRole.Owner);
        // Arrived 4 h 10 min ago for 5 hours after it arrived: by today at 11:50, in 50 minutes (Mexico City). The business
        // moved it to tomorrow at 9:00; by that time its limit would be a day after it arrived, tomorrow at 6:50.
        var arrived = Now.AddHours(-4).AddMinutes(-10);
        var asked = arrived.AddHours(5);
        var visit = Appointment.RequestHomeVisit(
            trade.Id, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, asked, asked.AddHours(1), "Gotea el calentador",
            null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null), zone, HomeVisitChargePolicy.Charged, null,
            Guid.NewGuid(), new string('v', 43));
        visit.CreatedAt = arrived;
        var tomorrow = new DateTimeOffset(2027, 3, 2, 9, 0, 0, TimeSpan.FromHours(-6));
        visit.Update("Valeria", "+525522107765", HomeVisitRules.DefaultVisitName, tomorrow, tomorrow.AddHours(1), null, null);
        fixture.Context.Appointments.Add(visit);
        await fixture.Context.SaveChangesAsync();

        fixture.TenantService.SetTenant(trade.Id);
        Assert.Equal(1, await new AnswerReminderPlanner(fixture.Context, fixture.Clock).RemindDueAsync(CancellationToken.None));
        await fixture.Context.SaveChangesAsync();
        Assert.Equal([owner], (await QueueAsync(fixture, trade, visit)).Select(q => q.Event.UserId));
        var sent = Assert.Single(await SendAsync(fixture, trade, visit, owner));

        Assert.Equal("Responde a tiempo la solicitud de visita en Plomería Rápida: Visita a domicilio, martes 2 de marzo a las 9:00 a. m.", sent.Subject);
        Assert.Contains("<h2>Una solicitud de visita sigue sin respuesta</h2>", sent.Body);
        Assert.Contains("<p><strong>Valeria</strong> pidió una visita a domicilio a Plomer&#237;a R&#225;pida para el martes 2 de marzo a las 9:00 a. m.</p>", sent.Body);
        Assert.Contains("<p>Respóndela antes del lunes 1 de marzo a las 11:50 a. m. para que cuente como respondida a tiempo.</p>", sent.Body);
        Assert.DoesNotContain("Juárez", sent.Body);
        Assert.DoesNotContain("72000", sent.Body);
        Assert.DoesNotContain("calentador", sent.Body);
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

    private static async Task ReminderAsync(SalesTestFixture fixture, Tenant tenant, int minutes)
    {
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.SetAnswerReminder(minutes);
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<List<Guid>> RemindedAsync(SalesTestFixture fixture)
        => fixture.Context.Appointments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.AnswerReminderSentAt != null).Select(a => a.Id).ToListAsync();
}
