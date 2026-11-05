using FoodParkDigital.Application.Appointments.RequestNotices;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Telling the business of each request online (Fase 29N, ADR 0068): an email to each member who gets them and may answer
/// it, each one its own message in the outbox; what it says and what it never says; the live screens; and each person's own
/// choice.
/// </summary>
public class RequestNoticesTests
{
    // 2027-03-01 at 11:00 in Mexico City.
    private const string AppUrl = "https://app.tienditapp.test";

    [Fact]
    public async Task TheEmails_ShouldGoToEachMemberWhoGetsThem_AndMayAnswerTheRequests()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "avisos", "Estética Avisos");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var chosenOff = await MemberAsync(fixture, salon, "socia", TenantUserRole.Owner, choose: false);
        var adminOn = await MemberAsync(fixture, salon, "admin", TenantUserRole.Admin, choose: true);
        await MemberAsync(fixture, salon, "admin2", TenantUserRole.Admin);
        var staffOn = await MemberAsync(fixture, salon, "estilista", TenantUserRole.Staff, choose: true, permissions: [TenantPermission.ManageAppointments]);
        await MemberAsync(fixture, salon, "cajero", TenantUserRole.Staff, choose: true, permissions: [TenantPermission.ManageOrders]);
        await MemberAsync(fixture, salon, "invitada", TenantUserRole.Owner, status: TenantUserStatus.Invited);
        await MemberAsync(fixture, salon, "baja", TenantUserRole.Owner, status: TenantUserStatus.Disabled);
        await MemberAsync(fixture, salon, "sinconfirmar", TenantUserRole.Owner, emailConfirmed: false);
        await MemberAsync(fixture, salon, "inactiva", TenantUserRole.Owner, accountActive: false);
        // The owner of another business, and the same staff member there.
        var other = await SalonAsync(fixture, "otro", "Estética Otra");
        var otherOwner = await MemberAsync(fixture, other, "otra", TenantUserRole.Owner);
        fixture.Context.TenantUsers.Add(Membership(other, staffOn, TenantUserRole.Owner));
        await fixture.Context.SaveChangesAsync();
        var request = await RequestAsync(fixture, salon, Now.AddMinutes(-1));

        var queued = await QueueAsync(fixture, salon, request);

        Assert.Equal(
            new[] { owner, adminOn, staffOn }.Order(),
            queued.Select(q => q.Event.UserId).Order());
        Assert.All(queued, q => Assert.Equal((request.Id, (Guid?)salon.Id), (q.Event.AppointmentId, q.TenantId)));
        Assert.DoesNotContain(queued, q => q.Event.UserId == chosenOff || q.Event.UserId == otherOwner);
    }

    [Fact]
    public async Task NoEmail_ShouldGo_FromABusinessSuspended_OrWithoutTheModuleOfAppointments()
    {
        await using var fixture = Fixture();
        var suspended = await SalonAsync(fixture, "suspendida", "Estética Suspendida");
        await MemberAsync(fixture, suspended, "dueña", TenantUserRole.Owner);
        var ofSuspended = await RequestAsync(fixture, suspended, Now.AddMinutes(-1));
        suspended.SetStatus(TenantStatus.Suspended);
        var shop = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "tienda");
        await MemberAsync(fixture, shop, "dueño", TenantUserRole.Owner);
        var ofShop = await RequestAsync(fixture, shop, Now.AddMinutes(-1));
        await fixture.Context.SaveChangesAsync();

        Assert.Empty(await QueueAsync(fixture, suspended, ofSuspended));
        Assert.Empty(await QueueAsync(fixture, shop, ofShop));
    }

    [Fact]
    public async Task NoEmail_ShouldGo_ForARequestOfTheTeam_OneAnswered_OneOverADayOld_OrOneWhoseTimeCame()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "nada", "Estética Nada");
        await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var ofTheTeam = await RequestAsync(fixture, salon, Now.AddMinutes(-1), fromTeam: true);
        var answered = await RequestAsync(fixture, salon, Now.AddMinutes(-5));
        answered.Accept(Now.AddMinutes(-1));
        var overADay = await RequestAsync(fixture, salon, Now.AddDays(-1).AddMinutes(-1));
        var itsTimeCame = await RequestAsync(fixture, salon, Now.AddHours(-2), startAt: Now);
        // A day old to the minute, and its time a minute away: still worth it.
        var aDay = await RequestAsync(fixture, salon, Now.AddDays(-1));
        var aMinuteAway = await RequestAsync(fixture, salon, Now.AddHours(-2), startAt: Now.AddMinutes(1));
        await fixture.Context.SaveChangesAsync();

        foreach (var none in new[] { ofTheTeam, answered, overADay, itsTimeCame })
            Assert.Empty(await QueueAsync(fixture, salon, none));
        Assert.Single(await QueueAsync(fixture, salon, aDay));
        Assert.Single(await QueueAsync(fixture, salon, aMinuteAway));
    }

    [Fact]
    public async Task TheEmail_ShouldSayWhatWhenByWhomAndByWhen_InTheBusinessZone_AndNeverThePhoneOrTheNotes()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "texto", "Estética <Luna> & Sol");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        // Arrived at 10:00 for Thursday at 13:30: by tomorrow at 10:00.
        var thursday = new DateTimeOffset(2027, 3, 4, 13, 30, 0, TimeSpan.FromHours(-6));
        var request = Appointment.RequestOnline(
            salon.Id, "Ana <b>\"Leo\"</b>\nRuiz", "+525522107765", Guid.NewGuid(), "Corte & barba", thursday, thursday.AddHours(1),
            "Tengo el cabello muy largo", null, Guid.NewGuid(), new string('t', 43));
        request.CreatedAt = Now.AddHours(-1);
        fixture.Context.Appointments.Add(request);
        await fixture.Context.SaveChangesAsync();

        var sent = Assert.Single(await SendAsync(fixture, salon, request, owner));

        Assert.Equal($"dueña.{salon.Slug}@demo.com", sent.To);
        Assert.Equal("Nueva solicitud de cita en Estética <Luna> & Sol: Corte & barba, jueves 4 de marzo a la 1:30 p. m.", sent.Subject);
        Assert.Contains("<h2>Nueva solicitud de cita</h2>", sent.Body);
        Assert.Contains(
            // Encoded as the team's invitation is: the accents too.
            "<p><strong>Ana &lt;b&gt;&quot;Leo&quot;&lt;/b&gt; Ruiz</strong> pidió <strong>Corte &amp; barba</strong> en Est&#233;tica &lt;Luna&gt; &amp; Sol para el jueves 4 de marzo a la 1:30 p. m.</p>",
            sent.Body);
        Assert.Contains("<p>Respóndela antes del martes 2 de marzo a las 10:00 a. m. para que cuente como respondida a tiempo.</p>", sent.Body);
        Assert.Contains($"<a href='{AppUrl}/appointments'>Abrir la Agenda</a>", sent.Body);
        Assert.Contains($"de Est&#233;tica &lt;Luna&gt; &amp; Sol. Puedes dejar de recibirlos en <a href='{AppUrl}/profile'>Mi perfil</a>.", sent.Body);
        Assert.DoesNotContain("5522107765", sent.Body + sent.Subject);
        Assert.DoesNotContain("largo", sent.Body);
    }

    [Fact]
    public async Task TheEmailOfAVisit_ShouldSaySo_AndWithoutItsAddressOrItsLimitOnceItPassed()
    {
        await using var fixture = Fixture();
        var (trade, zone) = await TradeAsync(fixture, "plomeria", "Plomería Rápida");
        var owner = await MemberAsync(fixture, trade, "dueño", TenantUserRole.Owner);
        // Asked for an hour after it arrived, two hours ago, and moved by the business to tomorrow at 9:00: its limit passed.
        var asked = Now.AddHours(-1);
        var visit = Appointment.RequestHomeVisit(
            trade.Id, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, asked, asked.AddHours(1), "Gotea el calentador",
            null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null), zone, HomeVisitChargePolicy.Charged, null,
            Guid.NewGuid(), new string('v', 43));
        visit.CreatedAt = Now.AddHours(-2);
        var tomorrow = new DateTimeOffset(2027, 3, 2, 9, 0, 0, TimeSpan.FromHours(-6));
        visit.Update("Valeria", "+525522107765", HomeVisitRules.DefaultVisitName, tomorrow, tomorrow.AddHours(1), null, null);
        fixture.Context.Appointments.Add(visit);
        await fixture.Context.SaveChangesAsync();

        var sent = Assert.Single(await SendAsync(fixture, trade, visit, owner, appUrl: null));

        Assert.StartsWith("Nueva solicitud de visita en Plomería Rápida: ", sent.Subject);
        // Its service is the visit itself: not said twice.
        Assert.Contains("<p><strong>Valeria</strong> pidió una visita a domicilio a Plomer&#237;a R&#225;pida para el martes 2 de marzo a las 9:00 a. m.</p>", sent.Body);
        Assert.DoesNotContain("Respóndela antes", sent.Body);
        // Without the app's address configured, no links.
        Assert.Contains("<p>Respóndela desde la Agenda de TienditApp.</p>", sent.Body);
        Assert.Contains("Puedes dejar de recibirlos en Mi perfil.", sent.Body);
        Assert.DoesNotContain("href", sent.Body);
        Assert.DoesNotContain("Juárez", sent.Body);
        Assert.DoesNotContain("72000", sent.Body);
        Assert.DoesNotContain("calentador", sent.Body);
    }

    [Fact]
    public void TheEmailOfAVisit_ShouldNameItsService_WhenTheBusinessNamedItOtherwise()
    {
        var start = new DateTimeOffset(2027, 3, 2, 9, 0, 0, TimeSpan.FromHours(-6));

        var email = RequestEmail.Compose(
            "Plomería", "Valeria", "Revisión de fugas", homeVisit: true, start, start, Now, "America/Mexico_City", AppUrl);

        Assert.Equal("Nueva solicitud de visita en Plomería: Revisión de fugas, martes 2 de marzo a las 9:00 a. m.", email.Subject);
        Assert.Contains("<p><strong>Valeria</strong> pidió una visita a domicilio (Revisi&#243;n de fugas) a Plomer&#237;a para el martes 2 de marzo a las 9:00 a. m.</p>", email.HtmlBody);
    }

    [Fact]
    public async Task TheEmail_ShouldNotGo_WhenTheRequestWasAnswered_OrTheMemberStoppedGettingThem_SinceItWasQueued()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "despues", "Estética Después");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var admin = await MemberAsync(fixture, salon, "admin", TenantUserRole.Admin, choose: true);
        var answered = await RequestAsync(fixture, salon, Now.AddMinutes(-5));
        var waiting = await RequestAsync(fixture, salon, Now.AddMinutes(-5));

        answered.Decline(Now.AddMinutes(-1));
        (await fixture.Context.TenantUsers.SingleAsync(m => m.UserId == admin)).ChooseRequestEmails(false);
        await fixture.Context.SaveChangesAsync();

        Assert.Empty(await SendAsync(fixture, salon, answered, owner));
        Assert.Empty(await SendAsync(fixture, salon, waiting, admin));
        Assert.Single(await SendAsync(fixture, salon, waiting, owner));
        // A request of the team never gets one, though it were queued.
        var ofTheTeam = await RequestAsync(fixture, salon, Now.AddMinutes(-1), fromTeam: true);
        Assert.Empty(await SendAsync(fixture, salon, ofTheTeam, owner));
        // Nor one queued for a member of another business.
        var other = await SalonAsync(fixture, "otra", "Estética Otra");
        Assert.Empty(await SendAsync(fixture, other, waiting, owner));
    }

    [Fact]
    public async Task AFailureOfTheProvider_ShouldThrow_ForTheOutboxToRetryThatEmailAlone()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "falla", "Estética Falla");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var request = await RequestAsync(fixture, salon, Now.AddMinutes(-1));
        fixture.TenantService.SetTenant(salon.Id);

        var handler = new AppointmentRequestEmailHandler(
            fixture.Context, new MembersAccess(fixture.Context), new Emails(fail: true), new Links(AppUrl), fixture.Clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new AppointmentRequestEmailEvent(request.Id, owner), Context(salon), CancellationToken.None));
    }

    [Fact]
    public async Task TheLiveScreens_ShouldHearWhatWasAskedForAndWhen_AndNotWhoAsked()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "vivo", "Estética Vivo");
        var request = await RequestAsync(fixture, salon, Now.AddMinutes(-1), fromTeam: true);
        fixture.TenantService.SetTenant(salon.Id);
        var screens = new Screens();

        await new AppointmentRequestRealtimeNotifications(fixture.Context, screens, NullLogger<AppointmentRequestRealtimeNotifications>.Instance)
            .Handle(new AppointmentRequestedEvent(request.Id, salon.Id, HomeVisit: false, FromTeam: true), CancellationToken.None);
        // One of another business is never told to this one.
        await new AppointmentRequestRealtimeNotifications(fixture.Context, screens, NullLogger<AppointmentRequestRealtimeNotifications>.Instance)
            .Handle(new AppointmentRequestedEvent(request.Id, Guid.NewGuid(), HomeVisit: false, FromTeam: false), CancellationToken.None);
        // A screen that fails does not fail the request.
        await new AppointmentRequestRealtimeNotifications(fixture.Context, new Screens(fail: true), NullLogger<AppointmentRequestRealtimeNotifications>.Instance)
            .Handle(new AppointmentRequestedEvent(request.Id, salon.Id, HomeVisit: false, FromTeam: false), CancellationToken.None);

        var (tenantId, notice) = Assert.Single(screens.Heard);
        Assert.Equal(salon.Id, tenantId);
        Assert.Equal(new AppointmentRequestedNotice(request.Id, "Corte", request.StartAt, false, true), notice);
    }

    [Fact]
    public async Task EachPerson_ShouldChooseForThemselves_InTheBusinessTheyAreIn()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "elige", "Estética Elige");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var admin = await MemberAsync(fixture, salon, "admin", TenantUserRole.Admin, emailConfirmed: false);
        // The owner is also staff in another business, which keeps its own choice.
        var other = await SalonAsync(fixture, "otra", "Estética Otra");
        fixture.Context.TenantUsers.Add(Membership(other, owner, TenantUserRole.Staff));
        await fixture.Context.SaveChangesAsync();
        fixture.TenantService.SetTenant(salon.Id);

        var asOwner = Handler(fixture, owner);
        var asAdmin = Handler(fixture, admin);
        var before = (await asOwner.Handle(new GetRequestEmailsQuery(), CancellationToken.None)).Value;
        var adminBefore = (await asAdmin.Handle(new GetRequestEmailsQuery(), CancellationToken.None)).Value;
        var off = (await asOwner.Handle(new UpdateRequestEmailsCommand(false), CancellationToken.None)).Value;
        var adminOn = (await asAdmin.Handle(new UpdateRequestEmailsCommand(true), CancellationToken.None)).Value;

        Assert.Equal((true, $"dueña.{salon.Slug}@demo.com", true), (before.Enabled, before.Email, before.EmailConfirmed));
        Assert.Equal((false, false), (adminBefore.Enabled, adminBefore.EmailConfirmed));
        Assert.False(off.Enabled);
        Assert.True(adminOn.Enabled);
        var memberships = await fixture.Context.TenantUsers.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(false, memberships.Single(m => m.UserId == owner && m.TenantId == salon.Id).RequestEmails);
        Assert.Null(memberships.Single(m => m.UserId == owner && m.TenantId == other.Id).RequestEmails);
        Assert.Equal(true, memberships.Single(m => m.UserId == admin).RequestEmails);

        // Whoever is not on the team (the platform's admin acting in it) has nothing to choose.
        var stranger = Handler(fixture, Guid.NewGuid());
        Assert.Equal(RequestEmailsHandler.NotAMemberMessage, (await stranger.Handle(new GetRequestEmailsQuery(), CancellationToken.None)).Error);
        Assert.Equal(RequestEmailsHandler.NotAMemberMessage, (await stranger.Handle(new UpdateRequestEmailsCommand(true), CancellationToken.None)).Error);
    }

    private static RequestEmailsHandler Handler(SalesTestFixture fixture, Guid userId)
        => new(fixture.Context, fixture.TenantService, new SalesCurrentUser { UserId = userId.ToString() });

    /// <summary>The emails the outbox queued for <paramref name="request"/> after it arrived.</summary>
    private static async Task<List<(AppointmentRequestEmailEvent Event, Guid? TenantId)>> QueueAsync(
        SalesTestFixture fixture, Tenant tenant, Appointment request)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var outbox = new Outbox();
        await new AppointmentRequestedEmailsHandler(fixture.Context, new MembersAccess(fixture.Context), outbox, fixture.Clock)
            .HandleAsync(new AppointmentRequestedEvent(request.Id, tenant.Id, request.Visit is not null, request.FromTeam), Context(tenant), CancellationToken.None);
        return outbox.Added;
    }

    /// <summary>What the email of <paramref name="request"/> to <paramref name="userId"/> sent, run in <paramref name="tenant"/>.</summary>
    private static async Task<List<(string To, string Subject, string Body)>> SendAsync(
        SalesTestFixture fixture, Tenant tenant, Appointment request, Guid userId, string? appUrl = AppUrl)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails();
        await new AppointmentRequestEmailHandler(fixture.Context, new MembersAccess(fixture.Context), emails, new Links(appUrl), fixture.Clock)
            .HandleAsync(new AppointmentRequestEmailEvent(request.Id, userId), Context(tenant), CancellationToken.None);
        return emails.Sent;
    }

    private static OutboxEventContext Context(Tenant tenant) => new(Guid.NewGuid(), tenant.Id, null, null, Now, 1);

    /// <summary>A member of <paramref name="tenant"/> with an account of their own, and its id.</summary>
    private static async Task<Guid> MemberAsync(
        SalesTestFixture fixture,
        Tenant tenant,
        string name,
        TenantUserRole role,
        bool? choose = null,
        TenantPermission[]? permissions = null,
        TenantUserStatus status = TenantUserStatus.Active,
        bool emailConfirmed = true,
        bool accountActive = true)
    {
        var email = $"{name}.{tenant.Slug}@demo.com";
        var account = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            FirstName = name,
            LastName = "Equipo",
            Role = role == TenantUserRole.Staff ? UserRole.Staff : UserRole.TenantAdmin,
            IsActive = accountActive
        };
        var member = Membership(tenant, account.Id, role, status);
        if (permissions is not null)
            member.SetExplicitPermissions(permissions.Select(p => p.ToString()));
        if (choose is { } chosen)
            member.ChooseRequestEmails(chosen);
        fixture.Context.Users.Add(account);
        fixture.Context.TenantUsers.Add(member);
        await fixture.Context.SaveChangesAsync();
        return account.Id;
    }

    private static TenantUser Membership(Tenant tenant, Guid userId, TenantUserRole role, TenantUserStatus status = TenantUserStatus.Active)
        => new(tenant.Id, userId, role, status);

    /// <summary>The access of each member as the API reads it (TenantAccessService), from what the fixture saved.</summary>
    private sealed class MembersAccess(SalesDbContext context) : ITenantAccessService
    {
        public async Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
        {
            var member = await context.TenantUsers.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);
            var tenant = await context.Tenants.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == tenantId, cancellationToken);
            var settings = await context.TenantSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.TenantId == tenantId, cancellationToken);
            var capabilities = TenantCapabilityRules.NormalizeCapabilities(settings.Capabilities, tenant.BusinessType);
            if (member is not { Status: TenantUserStatus.Active })
                return new TenantAccessSnapshot(false, capabilities, []);
            var account = await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
            return new TenantAccessSnapshot(
                true,
                capabilities,
                TenantPermissionCatalog.ResolveEffectiveCodes(member.PermissionCodes, account.Role, member.Role),
                TenantSuspended: tenant.Status is TenantStatus.Suspended or TenantStatus.Inactive,
                IsManager: member.Role is TenantUserRole.Owner or TenantUserRole.Admin);
        }

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Outbox : IOutbox
    {
        public List<(AppointmentRequestEmailEvent Event, Guid? TenantId)> Added { get; } = [];

        public void Add(DomainEvent domainEvent, Guid? tenantId) => Added.Add(((AppointmentRequestEmailEvent)domainEvent, tenantId));
    }

    private sealed class Emails(bool fail = false) : IEmailService
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendEmailAsync(string to, string subject, string body)
        {
            if (fail)
                throw new InvalidOperationException("SMTP 421");
            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task SendPasswordResetEmailAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task SendTeamInvitationAsync(string email, string businessName, string invitationPath) => throw new NotSupportedException();

        public Task SendAccountAlreadyExistsAsync(string email, bool customerAccount) => throw new NotSupportedException();

        public Task SendUnfinishedAccountAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task<EmailSendOutcome> SendDocumentsAsync(DocumentEmail email, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Links(string? baseUrl) : IAppLinks
    {
        public string? BaseUrl => baseUrl;
    }

    private sealed class Screens(bool fail = false) : IAppointmentNotificationService
    {
        public List<(Guid TenantId, AppointmentRequestedNotice Notice)> Heard { get; } = [];

        public Task NotifyAppointmentRequested(Guid tenantId, AppointmentRequestedNotice notice)
        {
            if (fail)
                throw new InvalidOperationException("SignalR");
            Heard.Add((tenantId, notice));
            return Task.CompletedTask;
        }
    }
}
