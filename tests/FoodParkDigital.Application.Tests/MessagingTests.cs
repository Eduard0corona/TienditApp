using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Templates, audiences, reminders and opt-outs of WhatsApp messaging.</summary>
public class MessagingTests
{
    [Fact]
    public void Dates_ShouldBeWrittenInSpanishInTheBusinessTimeZone()
    {
        // 18:30 UTC is 12:30 in Mexico City (UTC-6, no daylight saving time).
        var local = MessageValues.ToLocal(new DateTimeOffset(2026, 10, 5, 18, 30, 0, TimeSpan.Zero), "America/Mexico_City");

        Assert.Equal("lunes 5 de octubre", MessageValues.FormatDate(local));
        Assert.Equal("12:30 p. m.", MessageValues.FormatTime(local));
        Assert.Equal("9:05 a. m.", MessageValues.FormatTime(new DateTimeOffset(2026, 1, 1, 9, 5, 0, TimeSpan.Zero)));
        Assert.Equal("Ana", MessageValues.FirstName("  Ana María López "));
        Assert.Equal("cliente", MessageValues.FirstName(null));
    }

    [Theory]
    [InlineData("BAJA", MessagingKeyword.OptOut)]
    [InlineData(" baja. ", MessagingKeyword.OptOut)]
    [InlineData("¡Stop!", MessagingKeyword.OptOut)]
    [InlineData("Darme de baja", MessagingKeyword.OptOut)]
    [InlineData("ALTA", MessagingKeyword.OptIn)]
    [InlineData("quiero cancelar mi pedido", MessagingKeyword.None)]
    [InlineData("dame de baja el pedido 12", MessagingKeyword.None)]
    [InlineData("", MessagingKeyword.None)]
    public void Keywords_ShouldOnlyCountWhenTheMessageIsTheKeyword(string body, MessagingKeyword expected)
    {
        Assert.Equal(expected, MessagingKeywords.Parse(body));
    }

    [Fact]
    public async Task Buyers_ShouldBeDeduplicatedAndLeaveOutOptOuts()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        await AddOrdersAsync(fixture, tenant, ("+525511111111", "Ana", 2), ("+525522222222", "Beto", 1), ("+525533333333", "Carla", 3));
        fixture.Context.MessagingOptOuts.Add(new MessagingOptOut(tenant.Id, "+5215533333333", OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();

        var audience = await Resolver(fixture).ResolveAsync(CampaignAudienceKind.Buyers, 2, null, SalesTestFixture.Now, CancellationToken.None);

        var recipient = Assert.Single(audience.Recipients);
        Assert.Equal("+525511111111", recipient.Phone);
        Assert.Equal("Ana", recipient.Name);
        Assert.Equal(1, audience.ExcludedCount);
        Assert.False(audience.Truncated);
    }

    [Fact]
    public async Task AllCustomers_ShouldJoinRegisteredCustomersAndOrderPhones()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        await AddOrdersAsync(fixture, tenant, ("+525511111111", "Ana compra", 1));
        fixture.Context.Customers.Add(new Customer(tenant.Id, "Ana López", "55 1111 1111", null));
        fixture.Context.Customers.Add(new Customer(tenant.Id, "Diego", "5544444444", null));
        fixture.Context.Customers.Add(new Customer(tenant.Id, "Sin teléfono", null, null));
        await fixture.Context.SaveChangesAsync();

        var audience = await Resolver(fixture).ResolveAsync(CampaignAudienceKind.AllCustomers, 1, null, SalesTestFixture.Now, CancellationToken.None);

        Assert.Equal(2, audience.Recipients.Count);
        Assert.Contains(audience.Recipients, r => r.Phone == "+525511111111" && r.Name == "Ana López");
        Assert.Contains(audience.Recipients, r => r.Phone == "+525544444444" && r.Name == "Diego");
    }

    [Fact]
    public async Task Launch_ShouldQueueOneRenderedMessagePerRecipient()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        await AddOrdersAsync(fixture, tenant, ("+525511111111", "Ana López", 1), ("+525522222222", "Beto", 1));
        var template = new MessageTemplate(tenant.Id, "Promo", MessagePurpose.Campaign, "Hola {nombre}, hoy 2x1 en {negocio}.", "promo_2x1");
        var campaign = new MessageCampaign(tenant.Id, "2x1", template.Id, CampaignAudienceKind.Buyers, 1, null, SalesTestFixture.Now);
        fixture.Context.AddRange(template, campaign);
        await fixture.Context.SaveChangesAsync();

        var queued = await new CampaignLauncher(fixture.Context, Resolver(fixture), fixture.Clock).LaunchAsync(campaign, CancellationToken.None);
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(2, queued);
        Assert.Equal(CampaignStatus.Sending, campaign.Status);
        Assert.Equal(2, campaign.RecipientCount);
        var messages = await fixture.Context.OutgoingMessages.OrderBy(m => m.Phone).ToListAsync();
        Assert.Equal("Hola Ana, hoy 2x1 en Negocio demo.", messages[0].Body);
        Assert.Equal(["Ana", "Negocio demo"], messages[0].GetParameters());
        Assert.Equal("promo_2x1", messages[0].ProviderTemplateName);
        Assert.All(messages, m => Assert.StartsWith($"campaign:{campaign.Id:N}:", m.DeduplicationKey));
    }

    [Fact]
    public async Task Reminders_ShouldBeQueuedOncePerAppointmentWithinTheWindow()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        var settings = await fixture.Context.TenantSettings.SingleAsync();
        settings.UpdateAppointmentReminders(enabled: true, hoursBefore: 24, templateId: null);
        var tomorrow = new Appointment(tenant.Id, "Luis Pérez", "55 1234 5678", "Corte", SalesTestFixture.Now.AddHours(20), SalesTestFixture.Now.AddHours(21));
        var nextWeek = new Appointment(tenant.Id, "Mara", "5512345679", "Tinte", SalesTestFixture.Now.AddDays(7), SalesTestFixture.Now.AddDays(7).AddHours(1));
        var soon = new Appointment(tenant.Id, "Iván", "5512345670", "Barba", SalesTestFixture.Now.AddMinutes(10), SalesTestFixture.Now.AddMinutes(40));
        fixture.Context.Appointments.AddRange(tomorrow, nextWeek, soon);
        await fixture.Context.SaveChangesAsync();
        var planner = new AppointmentReminderPlanner(fixture.Context, new PhoneNormalizationService(), fixture.Clock);

        var first = await planner.QueueDueAsync(settings, CancellationToken.None);
        await fixture.Context.SaveChangesAsync();
        var second = await planner.QueueDueAsync(settings, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var reminder = await fixture.Context.OutgoingMessages.SingleAsync();
        Assert.Equal(tomorrow.Id, reminder.AppointmentId);
        Assert.Equal("+525512345678", reminder.Phone);
        // 2026-10-05 13:00 UTC is 07:00 in Mexico City.
        Assert.Contains("Hola Luis, te recordamos tu cita de Corte el lunes 5 de octubre a las 7:00 a. m. en Negocio demo.", reminder.Body);
    }

    [Fact]
    public async Task Sender_ShouldSkipOptOutsAndChangedAppointmentsAndUseApprovedTemplates()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        var appointment = new Appointment(tenant.Id, "Luis", "5512345678", "Corte", SalesTestFixture.Now.AddHours(20), SalesTestFixture.Now.AddHours(21));
        fixture.Context.Appointments.Add(appointment);
        await fixture.Context.SaveChangesAsync();
        var whatsApp = new FakeWhatsApp();
        var sender = new OutgoingMessageSender(fixture.Context, whatsApp, new MessagingConsentService(fixture.Context, fixture.Clock), fixture.Clock);
        var reminder = new OutgoingMessage(tenant.Id, MessagePurpose.AppointmentReminder, "+525512345678", "Luis", "Hola Luis", "cita", "es_MX",
            ["Luis"], SalesTestFixture.Now, AppointmentReminderPlanner.Key(appointment), appointmentId: appointment.Id);
        var promo = new OutgoingMessage(tenant.Id, MessagePurpose.Campaign, "+525599999999", null, "Hola cliente", null, "es_MX", [], SalesTestFixture.Now);

        var sent = await sender.SendAsync(reminder, CancellationToken.None);
        var text = await sender.SendAsync(promo, CancellationToken.None);
        appointment.Update("Luis", "5512345678", "Corte", SalesTestFixture.Now.AddHours(30), SalesTestFixture.Now.AddHours(31), null, null);
        await fixture.Context.SaveChangesAsync();
        var rescheduled = await sender.SendAsync(reminder, CancellationToken.None);
        fixture.Context.MessagingOptOuts.Add(new MessagingOptOut(tenant.Id, "+5215599999999", OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();
        var optedOut = await sender.SendAsync(promo, CancellationToken.None);

        Assert.Equal(OutgoingSendDecision.Sent, sent.Decision);
        Assert.Equal("wamid.1", sent.ProviderMessageId);
        Assert.Equal(OutgoingSendDecision.Sent, text.Decision);
        Assert.Equal(OutgoingSendDecision.Skipped, rescheduled.Decision);
        Assert.Equal(OutgoingSendDecision.Skipped, optedOut.Decision);
        Assert.Equal(OutgoingMessageSender.OptedOutReason, optedOut.Reason);
        Assert.Equal([("template:cita", "+525512345678"), ("text", "+525599999999")], whatsApp.Sent);
    }

    [Fact]
    public async Task CreateCampaign_ShouldNeedWhatsAppAndAnActiveCampaignTemplate()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var template = new MessageTemplate(tenant.Id, "Promo", MessagePurpose.Campaign, "Hola {nombre}");
        fixture.Context.MessageTemplates.Add(template);
        await fixture.Context.SaveChangesAsync();
        var signal = new CountingSignal();
        var handler = new CreateCampaignCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock, signal);
        var command = new CreateCampaignCommand { Name = "Promo", TemplateId = template.Id, AudienceKind = CampaignAudienceKind.AllCustomers };

        var withoutWhatsApp = await handler.Handle(command, CancellationToken.None);
        var config = new WhatsAppConfig(tenant.Id);
        config.Update(true, "token", "123456", "waba", "+52 55 0000 0000");
        fixture.Context.WhatsAppConfigs.Add(config);
        await fixture.Context.SaveChangesAsync();
        var past = await handler.Handle(command with { ScheduledAt = SalesTestFixture.Now.AddHours(-1) }, CancellationToken.None);
        var created = await handler.Handle(command, CancellationToken.None);

        Assert.False(withoutWhatsApp.IsSuccess);
        Assert.Equal(MessagingDefaults.WhatsAppNotConfigured, withoutWhatsApp.Error);
        Assert.False(past.IsSuccess);
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(CampaignStatus.Scheduled, created.Value.Status);
        Assert.Equal(1, signal.Count);
        Assert.Contains(await fixture.Context.AuditLogs.ToListAsync(), a => a.Action == "CampaignScheduled");
    }

    [Fact]
    public async Task Baja_ShouldOptOutConfirmAndSkipTheAutomaticAnswer_AndAltaShouldUndoIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var whatsApp = new FakeWhatsApp();
        var autoReply = new CountingAutoReply();
        var handler = new ProcessInboundMessageCommandHandler(
            fixture.Context,
            autoReply,
            whatsApp,
            new NullNotifications(),
            fixture.Mapper,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessInboundMessageCommandHandler>.Instance,
            new MessagingConsentService(fixture.Context, fixture.Clock));

        var baja = await handler.Handle(Inbound(tenant.Id, "wamid.in.1", "Baja"), CancellationToken.None);
        var optOut = await fixture.Context.MessagingOptOuts.SingleAsync();
        var question = await handler.Handle(Inbound(tenant.Id, "wamid.in.2", "¿Tienen pan?"), CancellationToken.None);
        var alta = await handler.Handle(Inbound(tenant.Id, "wamid.in.3", "ALTA"), CancellationToken.None);

        Assert.True(baja.IsSuccess && question.IsSuccess && alta.IsSuccess);
        Assert.Equal("+525512345678", optOut.Phone);
        Assert.Equal(OptOutSource.CustomerKeyword, optOut.Source);
        Assert.False(optOut.IsActive);
        Assert.Equal(1, autoReply.Calls);
        Assert.Equal(3, whatsApp.Sent.Count);
        var conversation = await fixture.Context.WhatsAppConversations.Include(c => c.Messages).SingleAsync();
        Assert.Contains(conversation.Messages, m => m.Body == MessagingDefaults.OptOutReply);
        Assert.Contains(conversation.Messages, m => m.Body == MessagingDefaults.OptInReply);
        Assert.Equal(2, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "MessagingOptOut" || a.Action == "MessagingOptIn"));
    }

    private static ProcessInboundMessageCommand Inbound(Guid tenantId, string id, string body) => new()
    {
        TenantId = tenantId,
        FromPhone = "+5215512345678",
        ProfileName = "Ana",
        Body = body,
        ExternalMessageId = id
    };

    private static CampaignAudienceResolver Resolver(SalesTestFixture fixture)
        => new(fixture.Context, new PhoneNormalizationService());

    private static async Task AddOrdersAsync(SalesTestFixture fixture, Tenant tenant, params (string Phone, string Name, int Count)[] customers)
    {
        foreach (var (phone, name, count) in customers)
        {
            for (var i = 0; i < count; i++)
            {
                var order = new Order(tenant.Id, null, null, true, $"V-{phone[^4..]}-{i}", BusinessType.Retail, OrderType.Retail, name, phone, null, null);
                order.TransitionTo(OrderStatus.Delivered, Domain.Orders.StateMachines.OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail), "test");
                fixture.Context.Orders.Add(order);
            }
        }

        await fixture.Context.SaveChangesAsync();
    }

    private sealed class FakeWhatsApp : IWhatsAppService
    {
        public List<(string Kind, string Phone)> Sent { get; } = [];

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
        {
            Sent.Add(($"template:{templateName}", phoneNumber));
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Sent.Count}"));
        }

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            Sent.Add(("text", phoneNumber));
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Sent.Count}"));
        }
    }

    private sealed class CountingAutoReply : IAiAgentAutoReplyService
    {
        public int Calls { get; private set; }

        public Task<string?> BuildReplyAsync(Guid tenantId, string incomingMessage, string? customerName, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<string?>("Sí, tenemos pan.");
        }
    }

    private sealed class NullNotifications : IWhatsAppNotificationService
    {
        public Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message) => Task.CompletedTask;

        public Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation) => Task.CompletedTask;
    }

    private sealed class CountingSignal : IOutgoingMessageSignal
    {
        public int Count { get; private set; }

        public void Notify() => Count++;
    }
}
