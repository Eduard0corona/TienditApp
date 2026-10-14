using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.BackgroundJobs;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// An appointment and its notice are saved together in SQL Server (the notice points to the appointment it is about),
/// and the worker skips a notice that a newer one of the same appointment replaced.
/// </summary>
public class AppointmentNoticeSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task NoticeShouldBeSavedWithTheAppointment_AndAnOlderOneShouldGiveWayToTheNewest()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedAsync(database);
        var clock = new MutableClock { UtcNow = Now };
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);

        Guid appointmentId;
        await using (var context = database.CreateContext(tenantId))
        {
            var created = await new CreateAppointmentCommandHandler(
                    context, tenant, new Receptionist(), clock, new AppointmentNotices(context, new PhoneNormalizationService(), clock))
                .Handle(new CreateAppointmentCommand
                {
                    CustomerName = "Luis",
                    CustomerPhone = "5512345678",
                    ServiceName = "Corte",
                    StartAt = Now.AddDays(1),
                    EndAt = Now.AddDays(1).AddHours(1)
                }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            appointmentId = created.Value!.Id;
        }

        // The business confirms it a minute later: a second notice, saved with the new status.
        clock.Advance(TimeSpan.FromMinutes(1));
        await using (var context = database.CreateContext(tenantId))
        {
            var confirmed = await new UpdateAppointmentStatusCommandHandler(context, new AppointmentNotices(context, new PhoneNormalizationService(), clock))
                .Handle(new UpdateAppointmentStatusCommand { AppointmentId = appointmentId, NewStatus = AppointmentStatus.Confirmed }, CancellationToken.None);
            Assert.True(confirmed.IsSuccess, confirmed.Error);
        }

        await using (var context = database.CreateContext(tenantId))
        {
            var notices = await context.OutgoingMessages.AsNoTracking()
                .Where(m => m.Purpose == MessagePurpose.AppointmentNotice)
                .OrderBy(m => m.NextAttemptAt)
                .ToListAsync();
            Assert.Equal(2, notices.Count);
            Assert.All(notices, n => Assert.Equal(appointmentId, n.AppointmentId));
            // This context has no audit interceptor: stamp when each one was queued, as production does.
            notices[0].CreatedAt = Now;
            notices[1].CreatedAt = Now.AddMinutes(1);
            context.OutgoingMessages.UpdateRange(notices);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenantId))
        {
            var whatsApp = new CountingWhatsApp();
            var sender = new OutgoingMessageSender(context, whatsApp, new MessagingConsentService(context, tenant, clock), clock);
            var notices = await context.OutgoingMessages.AsNoTracking()
                .Where(m => m.Purpose == MessagePurpose.AppointmentNotice)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            var booked = await sender.SendAsync(notices[0], CancellationToken.None);
            var latest = await sender.SendAsync(notices[1], CancellationToken.None);

            Assert.Equal(OutgoingSendDecision.Skipped, booked.Decision);
            Assert.Equal(OutgoingMessageSender.NoticeSupersededReason, booked.Reason);
            Assert.Equal(OutgoingSendDecision.Sent, latest.Decision);
            Assert.Equal(1, whatsApp.Sent);
        }
    }

    private static async Task<Guid> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Barbería Centro", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments, false);
        var whatsApp = new WhatsAppConfig(tenant.Id);
        whatsApp.Update(true, "token", $"pn-{slug}", "waba", "+52 55 0000 0000");
        await using var context = database.CreateContext(tenant.Id);
        context.Tenants.Add(tenant);
        context.TenantSettings.Add(settings);
        context.WhatsAppConfigs.Add(whatsApp);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private sealed class Receptionist : ICurrentUserService
    {
        public string? UserId => "7b1e6f0a-3c2d-4e5f-8a9b-1c2d3e4f5a6b";
        public string? UserName => "recepcion@demo.com";
        public string? Role => "TENANT_STAFF";
        public string? TenantRole => "Staff";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class CountingWhatsApp : IWhatsAppService
    {
        public int Sent { get; private set; }

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName, string languageCode,
            string[] parameters, CancellationToken cancellationToken = default) => Task.FromResult(MessageSendResult.Sent());

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            Sent++;
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Sent}"));
        }
    }
}
