using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Messaging;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

/// <summary>The messaging worker with production wiring: claims, business scopes, retries and campaign states.</summary>
public class MessagingWorkerTests
{
    [Fact]
    public async Task DueCampaign_ShouldStartSendInItsBusinessAndComplete()
    {
        await using var host = CreateHost(out var whatsApp);
        var (tenantA, campaignA) = await SeedCampaignAsync(host, "a", ("+525511111111", "Ana"), ("+525522222222", "Beto"));
        var (tenantB, campaignB) = await SeedCampaignAsync(host, "b", ("+525533333333", "Carla"));
        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenantA);
            var context = services.GetRequiredService<ApplicationDbContext>();
            context.MessagingOptOuts.Add(new MessagingOptOut(tenantA, "+5215522222222", OptOutSource.CustomerKeyword, host.Clock.UtcNow));
            return await context.SaveChangesAsync();
        });
        var worker = host.Get<MessagingWorker>();

        var started = await worker.StartDueCampaignsAsync(CancellationToken.None);
        var dispatched = await worker.DispatchDueAsync(CancellationToken.None);
        var completed = await worker.CompleteFinishedCampaignsAsync(CancellationToken.None);

        Assert.Equal(2, started);
        Assert.Equal(2, dispatched);
        Assert.Equal(2, completed);
        Assert.Equal([(tenantA, "+525511111111"), (tenantB, "+525533333333")], whatsApp.Sent.OrderBy(s => s.Phone).ToList());
        var campaigns = await AllAsync(host, c => c.MessageCampaigns);
        Assert.All(campaigns, c => Assert.Equal(CampaignStatus.Completed, c.Status));
        Assert.Equal(1, campaigns.Single(c => c.Id == campaignA).ExcludedCount);
        var messages = await AllAsync(host, c => c.OutgoingMessages);
        Assert.All(messages, m => Assert.Equal(OutgoingMessageStatus.Sent, m.Status));
        Assert.Contains(messages, m => m.TenantId == tenantB && m.CampaignId == campaignB && m.ProviderMessageId != null);
    }

    [Fact]
    public async Task TransientFailures_ShouldBeRetriedWithBackoffUntilTheLimit()
    {
        await using var host = CreateHost(out var whatsApp, maxAttempts: 2);
        whatsApp.Transient = true;
        await SeedCampaignAsync(host, "a", ("+525511111111", "Ana"));
        var worker = host.Get<MessagingWorker>();
        await worker.StartDueCampaignsAsync(CancellationToken.None);

        await worker.DispatchDueAsync(CancellationToken.None);
        var afterFirst = Assert.Single(await AllAsync(host, c => c.OutgoingMessages));
        var nothingDue = await worker.DispatchDueAsync(CancellationToken.None);
        host.Clock.Advance(RetrySchedule.DelayAfter(1));
        await worker.DispatchDueAsync(CancellationToken.None);
        var afterSecond = Assert.Single(await AllAsync(host, c => c.OutgoingMessages));

        Assert.Equal(OutgoingMessageStatus.Pending, afterFirst.Status);
        Assert.Equal(1, afterFirst.Attempts);
        Assert.Equal(0, nothingDue);
        Assert.Equal(OutgoingMessageStatus.Failed, afterSecond.Status);
        Assert.Equal(2, afterSecond.Attempts);
        Assert.Equal(2, whatsApp.Sent.Count);
    }

    [Fact]
    public async Task CancelledCampaign_ShouldCancelItsPendingMessagesWithoutSending()
    {
        await using var host = CreateHost(out var whatsApp);
        var (tenant, campaignId) = await SeedCampaignAsync(host, "a", ("+525511111111", "Ana"), ("+525522222222", "Beto"));
        var worker = host.Get<MessagingWorker>();
        await worker.StartDueCampaignsAsync(CancellationToken.None);
        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var campaign = await context.MessageCampaigns.SingleAsync(c => c.Id == campaignId);
            campaign.Cancel(host.Clock.UtcNow);
            return await context.SaveChangesAsync();
        });

        await worker.DispatchDueAsync(CancellationToken.None);

        Assert.Empty(whatsApp.Sent);
        Assert.All(await AllAsync(host, c => c.OutgoingMessages), m => Assert.Equal(OutgoingMessageStatus.Cancelled, m.Status));
        Assert.Equal(0, await worker.CompleteFinishedCampaignsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreditReminders_ShouldBeQueuedOnlyWhereTurnedOn_AndSentInTheirBusiness()
    {
        await using var host = CreateHost(out var whatsApp);
        var on = await SeedOverdueAsync(host, "a", "+525511111111", remindersOn: true);
        await SeedOverdueAsync(host, "b", "+525522222222", remindersOn: false);
        var suspended = await SeedOverdueAsync(host, "c", "+525533333333", remindersOn: true);
        await host.InScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            (await context.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == suspended)).SetStatus(TenantStatus.Suspended);
            return await context.SaveChangesAsync();
        });
        var worker = host.Get<MessagingWorker>();

        var queued = await worker.QueueCreditRemindersAsync(CancellationToken.None);
        var again = await worker.QueueCreditRemindersAsync(CancellationToken.None);
        // The auditing interceptor of production stamps when it was queued: a reminder goes out only that business day.
        await host.InScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            foreach (var queuedMessage in await context.OutgoingMessages.IgnoreQueryFilters().ToListAsync())
                queuedMessage.CreatedAt = host.Clock.UtcNow;
            return await context.SaveChangesAsync();
        });
        var dispatched = await worker.DispatchDueAsync(CancellationToken.None);

        Assert.Equal((1, 0, 1), (queued, again, dispatched));
        Assert.Equal((on, "+525511111111"), Assert.Single(whatsApp.Sent));
        var message = Assert.Single(await AllAsync(host, c => c.OutgoingMessages));
        Assert.Equal((MessagePurpose.CreditReminder, OutgoingMessageStatus.Sent), (message.Purpose, message.Status));
    }

    [Fact]
    public async Task CreditNotices_ShouldBeQueuedWhereTurnedOn_EachOnItsOwn_NextToTheReminders()
    {
        await using var host = CreateHost(out _);
        // Business a has both on: the reminder to who owes something overdue, the notice to who has something due tomorrow.
        // Business b has only the notice: the one overdue gets nothing.
        var both = await SeedOverdueAsync(host, "a", "+525511111111", remindersOn: true, dueSoonPhone: "+525544444444");
        var noticeOnly = await SeedOverdueAsync(host, "b", "+525522222222", remindersOn: false, dueSoonPhone: "+525555555555");
        var worker = host.Get<MessagingWorker>();

        var queued = await worker.QueueCreditRemindersAsync(CancellationToken.None);
        var again = await worker.QueueCreditRemindersAsync(CancellationToken.None);

        Assert.Equal((3, 0), (queued, again));
        var messages = await AllAsync(host, c => c.OutgoingMessages);
        Assert.Equal(
            [(both, MessagePurpose.CreditReminder, "+525511111111"), (both, MessagePurpose.CreditDueSoon, "+525544444444"),
                (noticeOnly, MessagePurpose.CreditDueSoon, "+525555555555")],
            messages.Select(m => (m.TenantId, m.Purpose, m.Phone)).OrderBy(m => m.Phone));
    }

    private static OutboxTestHost CreateHost(out FakeWhatsApp whatsApp, int maxAttempts = 6)
    {
        var fake = new FakeWhatsApp();
        whatsApp = fake;
        return OutboxTestHost.Create(configure: services =>
        {
            services.Configure<BackgroundJobOptions>(options =>
            {
                options.Messaging.MaxAttempts = maxAttempts;
                options.Messaging.SendIntervalMilliseconds = 0;
            });
            services.AddSingleton<IWhatsAppService>(fake);
            services.AddScoped<IPhoneNormalizationService, PhoneNormalizationService>();
            services.AddScoped<MessagingConsentService>();
            services.AddScoped<CampaignAudienceResolver>();
            services.AddScoped<CampaignLauncher>();
            services.AddScoped<AppointmentReminderPlanner>();
            services.AddScoped<CreditReminderPlanner>();
            services.AddScoped<CreditDueSoonPlanner>();
            services.AddScoped<OutgoingMessageSender>();
            services.AddSingleton<MessagingSignal>();
            services.AddSingleton<MessagingWorker>();
        });
    }

    private static Task<(Guid TenantId, Guid CampaignId)> SeedCampaignAsync(
        OutboxTestHost host, string slug, params (string Phone, string Name)[] buyers)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Retail));
            var stateMachine = OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail);
            foreach (var (phone, name) in buyers)
            {
                var order = new Order(tenant.Id, null, null, true, $"V-{slug}-{phone[^4..]}", BusinessType.Retail, OrderType.Retail, name, phone, null, null);
                order.TransitionTo(OrderStatus.Delivered, stateMachine, "test");
                context.Orders.Add(order);
            }

            var template = new MessageTemplate(tenant.Id, "Promo", MessagePurpose.Campaign, "Hola {nombre}, hoy 2x1.");
            var campaign = new MessageCampaign(tenant.Id, "2x1", template.Id, CampaignAudienceKind.Buyers, 1, null, host.Clock.UtcNow);
            context.AddRange(template, campaign);
            await context.SaveChangesAsync();
            return (tenant.Id, campaign.Id);
        });

    /// <summary>
    /// A business with a 7-day term, WhatsApp connected and a customer who owes a sale on credit of 20 days ago (Fase 26C).
    /// </summary>
    /// <summary>
    /// A business with 7 days to pay and a sale on credit to <paramref name="phone"/> 20 days ago; with
    /// <paramref name="dueSoonPhone"/>, the notice two days before is on and that phone owes a sale of 6 days ago, due tomorrow.
    /// </summary>
    private static async Task<Guid> SeedOverdueAsync(OutboxTestHost host, string slug, string phone, bool remindersOn, string? dueSoonPhone = null)
    {
        var (tenantId, orders) = await host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var settings = new TenantSettings(tenant.Id, TenantCapability.Retail);
            settings.SetCreditTermDays(7);
            settings.UpdateCreditReminders(remindersOn, 7, null);
            settings.UpdateCreditDueSoon(dueSoonPhone is not null, 2, null);
            var config = new WhatsAppConfig(tenant.Id);
            config.Update(true, "token", $"phone-{slug}", "waba", "+52 55 0000 0000");
            var stateMachine = new RetailOrderStateMachine();
            var sales = new List<(Order Order, int DaysAgo)>();
            foreach (var (customerPhone, daysAgo) in new[] { (phone, 20), (dueSoonPhone, 6) })
            {
                if (customerPhone is null)
                    continue;
                var order = new Order(tenant.Id, null, null, true, $"V-{slug}-{sales.Count + 1}", BusinessType.Retail, OrderType.Retail, "Ana", customerPhone, null, null);
                order.AddItem(Guid.NewGuid(), "Jabón", 1, 100m);
                order.TransitionTo(OrderStatus.Confirmed, stateMachine, "cajero");
                order.SellOnCredit();
                order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: false, "cajero");
                sales.Add((order, daysAgo));
            }
            context.AddRange(tenant, settings, config);
            context.AddRange(sales.Select(s => s.Order));
            await context.SaveChangesAsync();
            return (tenant.Id, sales.Select(s => (s.Order.Id, s.DaysAgo)).ToList());
        });
        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenantId);
            var context = services.GetRequiredService<ApplicationDbContext>();
            foreach (var (orderId, daysAgo) in orders)
                (await context.Orders.SingleAsync(o => o.Id == orderId)).CreatedAt = host.Clock.UtcNow.AddDays(-daysAgo);
            return await context.SaveChangesAsync();
        });
        return tenantId;
    }

    private static Task<List<T>> AllAsync<T>(OutboxTestHost host, Func<ApplicationDbContext, IQueryable<T>> set)
        where T : class
        => host.InScopeAsync(services => set(services.GetRequiredService<ApplicationDbContext>())
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync());

    private sealed class FakeWhatsApp : IWhatsAppService
    {
        public List<(Guid TenantId, string Phone)> Sent { get; } = [];
        public bool Transient { get; set; }

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
            => SendTextMessageAsync(tenantId, phoneNumber, templateName, cancellationToken);

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            Sent.Add((tenantId, phoneNumber));
            return Task.FromResult(Transient
                ? MessageSendResult.Failed("Timeout", isTransient: true)
                : MessageSendResult.Sent($"wamid.{Sent.Count}"));
        }
    }
}
