using FoodParkDigital.Application.Appointments.RequestNotices;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Quotes.ReplyNotices;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Quotes.Events;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// What the customers answer online, through the real outbox (Fase 29O, ADR 0069): cancelling an appointment and answering a
/// quote from its link store their event with the change, and the dispatcher queues each owner's email once and sends it once,
/// though it runs again.
/// </summary>
public class CustomerAnswerEmailsOutboxTests
{
    private static readonly string[] Owners = ["dueña@demo.com", "socia@demo.com"];

    [Fact]
    public async Task ACancellationByItsCustomer_ShouldReachEachOwnerOnce()
    {
        var emails = new Emails();
        await using var host = Host(emails);
        var (business, appointmentId, _) = await SeedAsync(host);
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        // The request itself was told to both (Fase 29N).
        Assert.Equal(2, emails.Sent.Count);

        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(business);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var appointment = await context.Appointments.SingleAsync(a => a.Id == appointmentId);
            appointment.CancelByCustomer(host.Clock.UtcNow);
            return await context.SaveChangesAsync();
        });
        for (var i = 0; i < 3; i++)
            await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        var cancellations = emails.Sent.Where(e => e.Subject.StartsWith("Solicitud de cita cancelada en Estética Luna: ")).ToList();
        Assert.Equal(Owners.Order(), cancellations.Select(e => e.To).Order());
        Assert.Equal(2, await MessagesOfAsync(host, new AppointmentCancellationEmailEvent(Guid.Empty, Guid.Empty, false)));
        Assert.All(await host.MessagesAsync(), message => Assert.NotNull(message.ProcessedAt));
    }

    [Fact]
    public async Task AnAnswerFromTheLink_ShouldReachEachOwnerOnce_AndTheSameAnswerAgainNobody()
    {
        var emails = new Emails();
        await using var host = Host(emails);
        var (business, _, quoteId) = await SeedAsync(host);
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        emails.Sent.Clear();

        // The customer accepts, and taps again: the second answer changes nothing.
        for (var tap = 0; tap < 2; tap++)
        {
            await host.InScopeAsync(async services =>
            {
                services.GetRequiredService<ICurrentTenantService>().SetTenant(business);
                var context = services.GetRequiredService<ApplicationDbContext>();
                var quote = await context.Quotes.SingleAsync(q => q.Id == quoteId);
                quote.ReplyFromLink(true, DateOnly.FromDateTime(host.Clock.UtcNow.UtcDateTime), "Los espero el jueves", host.Clock.UtcNow);
                return await context.SaveChangesAsync();
            });
        }
        for (var i = 0; i < 3; i++)
            await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(Owners.Order(), emails.Sent.Select(e => e.To).Order());
        Assert.All(emails.Sent, e => Assert.Equal("Cotización aceptada en Estética Luna: COT-000001 por $1,160.00", e.Subject));
        Assert.Equal(1, await MessagesOfAsync(host, new QuoteAnsweredFromLinkEvent(Guid.Empty, Guid.Empty, true)));
        Assert.Equal(2, await MessagesOfAsync(host, new QuoteReplyEmailEvent(Guid.Empty, Guid.Empty)));
        Assert.All(await host.MessagesAsync(), message => Assert.NotNull(message.ProcessedAt));
    }

    private static OutboxTestHost Host(Emails emails)
        => OutboxTestHost.Create(maxAttempts: 5, configure: services =>
        {
            services.AddScoped<IOutbox, OutboxWriter>();
            services.AddSingleton<ITenantAccessService>(new Managers());
            services.AddSingleton<IEmailService>(emails);
            services.AddSingleton<IAppLinks>(new Links());
            services.AddScoped<IOutboxEventHandler<AppointmentRequestedEvent>, AppointmentRequestedEmailsHandler>();
            services.AddScoped<IOutboxEventHandler<AppointmentRequestEmailEvent>, AppointmentRequestEmailHandler>();
            services.AddScoped<IOutboxEventHandler<AppointmentCancelledByCustomerEvent>, AppointmentCancelledEmailsHandler>();
            services.AddScoped<IOutboxEventHandler<AppointmentCancellationEmailEvent>, AppointmentCancellationEmailHandler>();
            services.AddScoped<IOutboxEventHandler<QuoteAnsweredFromLinkEvent>, QuoteAnsweredEmailsHandler>();
            services.AddScoped<IOutboxEventHandler<QuoteReplyEmailEvent>, QuoteReplyEmailHandler>();
        });

    /// <summary>A salon with two owners whose emails are confirmed, a request online that just arrived and a quote sent.</summary>
    private static async Task<(Guid Business, Guid AppointmentId, Guid QuoteId)> SeedAsync(OutboxTestHost host)
    {
        var tenant = new Tenant("Estética Luna", "luna", BusinessType.Service, "luna@demo.com");
        var business = tenant.Id;
        var start = host.Clock.UtcNow.AddDays(2);
        var request = Appointment.RequestOnline(
            business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
            new string('t', 43));
        var quote = Quote.Start(business, Folios.QuoteNumber(1), new QuoteCustomer(null, "Valeria", "+525522107765", null), null, true, 15,
            host.Clock.UtcNow);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], host.Clock.UtcNow);
        quote.Send(DateOnly.FromDateTime(host.Clock.UtcNow.UtcDateTime), new string('q', 43), null, host.Clock.UtcNow);
        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(business);
            var context = services.GetRequiredService<ApplicationDbContext>();
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(business, TenantCapability.Service | TenantCapability.Appointments));
            foreach (var email in Owners)
            {
                var account = new ApplicationUser
                {
                    Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, FirstName = "Dueña", LastName = "Luna",
                    Role = UserRole.TenantAdmin, IsActive = true
                };
                context.Users.Add(account);
                context.TenantUsers.Add(new TenantUser(business, account.Id, TenantUserRole.Owner));
            }
            request.CreatedAt = host.Clock.UtcNow;
            context.Appointments.Add(request);
            context.Quotes.Add(quote);
            // SQL Server writes the row versions; the in-memory store needs them.
            context.Entry(request).Property("Version").CurrentValue = new byte[8];
            context.Entry(quote).Property("Version").CurrentValue = new byte[8];
            return await context.SaveChangesAsync();
        });
        return (business, request.Id, quote.Id);
    }

    private static Task<int> MessagesOfAsync(OutboxTestHost host, Domain.Common.DomainEvent sample)
        => host.InScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().OutboxMessages
            .CountAsync(m => m.Type == OutboxEventSerializer.TypeNameOf(sample)));

    private sealed class Emails : IEmailService
    {
        public List<(string To, string Subject)> Sent { get; } = [];

        public Task SendEmailAsync(string to, string subject, string body)
        {
            Sent.Add((to, subject));
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task SendPasswordResetEmailAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task SendTeamInvitationAsync(string email, string businessName, string invitationPath) => throw new NotSupportedException();

        public Task SendAccountAlreadyExistsAsync(string email, bool customerAccount) => throw new NotSupportedException();

        public Task SendUnfinishedAccountAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task<EmailSendOutcome> SendDocumentsAsync(DocumentEmail email, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Every member manages the business, which has services and appointments.</summary>
    private sealed class Managers : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Service | TenantCapability.Appointments, [], IsManager: true));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class Links : IAppLinks
    {
        public string? BaseUrl => "https://app.tienditapp.test";
    }
}
