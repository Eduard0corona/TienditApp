using FoodParkDigital.Application.Appointments.RequestNotices;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// The emails of a request online through the real outbox dispatcher (Fase 29N, ADR 0068): the email of each member is queued
/// once, with the record that the request was handled, and each one is sent and retried on its own. Another handler of the
/// same event that fails does not queue them again, and a member's email that fails does not send the others' again.
/// </summary>
public class RequestEmailsOutboxTests
{
    [Fact]
    public async Task EachMembersEmail_ShouldBeQueuedOnce_AndSentAndRetriedOnItsOwn()
    {
        var emails = new FlakyEmails(failFirstTo: "socia@demo.com");
        await using var host = OutboxTestHost.Create(maxAttempts: 5, configure: services =>
        {
            services.AddScoped<IOutbox, OutboxWriter>();
            services.AddSingleton<ITenantAccessService>(new Managers());
            services.AddSingleton<IEmailService>(emails);
            services.AddSingleton<IAppLinks>(new Links());
            services.AddScoped<IOutboxEventHandler<AppointmentRequestedEvent>, AppointmentRequestedEmailsHandler>();
            services.AddSingleton<FailsOnce>();
            services.AddScoped<IOutboxEventHandler<AppointmentRequestedEvent>>(sp => sp.GetRequiredService<FailsOnce>());
            services.AddScoped<IOutboxEventHandler<AppointmentRequestEmailEvent>, AppointmentRequestEmailHandler>();
        });
        await SeedAsync(host, "dueña@demo.com", "socia@demo.com");

        // The request: its own handler queues both emails; the other handler of the event fails, so the event stays pending.
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        // The two emails: the owner's goes out, her partner's fails.
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        Assert.Equal(["dueña@demo.com"], emails.Sent);
        Assert.Equal(2, await EmailMessagesAsync(host));

        // Later, both retries: the event runs only the handler that failed, so nothing is queued again; the partner's email
        // goes out, and the owner's is not sent again.
        host.Clock.Advance(TimeSpan.FromMinutes(10));
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(["dueña@demo.com", "socia@demo.com"], emails.Sent);
        Assert.Equal(2, await EmailMessagesAsync(host));
        Assert.Equal(2, host.Get<FailsOnce>().Runs);
        Assert.All(await host.MessagesAsync(), message => Assert.NotNull(message.ProcessedAt));
    }

    /// <summary>A salon with two owners whose emails are confirmed, and a request online that just arrived.</summary>
    private static Task SeedAsync(OutboxTestHost host, params string[] owners)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant("Estética Luna", "luna", BusinessType.Service, "luna@demo.com");
            var business = tenant.Id;
            services.GetRequiredService<ICurrentTenantService>().SetTenant(business);
            var context = services.GetRequiredService<ApplicationDbContext>();
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(business, TenantCapability.Service | TenantCapability.Appointments));
            foreach (var email in owners)
            {
                var account = new ApplicationUser
                {
                    Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, FirstName = "Dueña", LastName = "Luna",
                    Role = UserRole.TenantAdmin, IsActive = true
                };
                context.Users.Add(account);
                context.TenantUsers.Add(new TenantUser(business, account.Id, TenantUserRole.Owner));
            }
            var start = host.Clock.UtcNow.AddDays(2);
            var request = Appointment.RequestOnline(
                business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
                new string('t', 43));
            request.CreatedAt = host.Clock.UtcNow;
            context.Appointments.Add(request);
            // SQL Server writes the row version; the in-memory store needs one.
            context.Entry(request).Property("Version").CurrentValue = new byte[8];
            return await context.SaveChangesAsync();
        });

    private static Task<int> EmailMessagesAsync(OutboxTestHost host)
        => host.InScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().OutboxMessages
            .CountAsync(m => m.Type == OutboxEventSerializer.TypeNameOf(new AppointmentRequestEmailEvent(Guid.Empty, Guid.Empty))));

    /// <summary>Another handler of the event, which fails the first time.</summary>
    private sealed class FailsOnce : IOutboxEventHandler<AppointmentRequestedEvent>
    {
        public int Runs { get; private set; }

        public Task HandleAsync(AppointmentRequestedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
            => ++Runs == 1 ? throw new InvalidOperationException("Primera vez.") : Task.CompletedTask;
    }

    private sealed class FlakyEmails(string failFirstTo) : IEmailService
    {
        private bool _failed;

        public List<string> Sent { get; } = [];

        public Task SendEmailAsync(string to, string subject, string body)
        {
            if (to == failFirstTo && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("SMTP 421");
            }
            Sent.Add(to);
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task SendPasswordResetEmailAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task SendTeamInvitationAsync(string email, string businessName, string invitationPath) => throw new NotSupportedException();

        public Task SendAccountAlreadyExistsAsync(string email, bool customerAccount) => throw new NotSupportedException();

        public Task SendUnfinishedAccountAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task<EmailSendOutcome> SendDocumentsAsync(DocumentEmail email, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Every member manages the business, which has appointments.</summary>
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
