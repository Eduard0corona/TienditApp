using FoodParkDigital.Application.AiAgents;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The automatic replies answer by whole words, invite to the online menu instead of promising an order, ask for
/// data only when greeting, and stay quiet while a person handles the chat or the customer sends a photo.
/// </summary>
public class AutoRepliesAndChatTests
{
    private const string WhatsAppPhone = "+5215512345678";

    [Fact]
    public void Keywords_ShouldBeWholeWordsWithoutAccents()
    {
        var words = AutoReplyKeywords.Words("¿Tienen CHILAQUILES? Quiero ver el menú, buenas tardes!");

        Assert.False(AutoReplyKeywords.Contains(words, "hi"));
        Assert.True(AutoReplyKeywords.Contains(words, "menu"));
        Assert.True(AutoReplyKeywords.Contains(words, "buenas tardes"));
        Assert.False(AutoReplyKeywords.Contains(words, "buenas noches"));
    }

    [Fact]
    public async Task Greeting_ShouldAskForTheName_AndOtherRepliesShouldNot()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await AddAgentAsync(fixture, tenant, products: "Tacos $25, aguas $15.");
        var replies = AutoReplies(fixture);

        var greeting = await replies.BuildReplyAsync(tenant.Id, "Hola!", customerName: null);
        var prices = await replies.BuildReplyAsync(tenant.Id, "¿cuánto cuestan los tacos?", customerName: null);

        Assert.EndsWith("¿Me compartes tu nombre?", greeting);
        Assert.DoesNotContain("nombre", prices);
        Assert.StartsWith("Tacos $25, aguas $15.", prices);
    }

    [Fact]
    public async Task CatalogQuestion_ShouldLinkTheOnlineMenu_WhenItTakesOrders()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await AddAgentAsync(fixture, tenant);
        var settings = await fixture.Context.TenantSettings.SingleAsync();
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        await fixture.Context.SaveChangesAsync();

        var reply = await AutoReplies(fixture, appUrl: "https://app.tienditapp.mx/").BuildReplyAsync(tenant.Id, "me pasas el menu", null);

        Assert.Equal("Mira el menú y haz tu pedido aquí: https://app.tienditapp.mx/m/demo", reply);
    }

    [Fact]
    public async Task CatalogQuestion_WithoutOnlineMenu_ShouldOfferAPersonInsteadOfPromisingAnOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await AddAgentAsync(fixture, tenant);

        var reply = await AutoReplies(fixture, appUrl: "https://app.tienditapp.mx").BuildReplyAsync(tenant.Id, "Quiero hacer un pedido", null);

        Assert.Equal("Escríbenos qué te gustaría y te ayudamos con tu pedido.", reply);
    }

    [Fact]
    public async Task Thanks_ShouldGetTheClosingMessage()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var agent = await AddAgentAsync(fixture, tenant);

        var reply = await AutoReplies(fixture).BuildReplyAsync(tenant.Id, "Ok, muchas gracias 👍", null);

        Assert.Equal(agent.ClosingMessage, reply);
    }

    [Fact]
    public async Task PersonWritingInTheChat_ShouldPauseTheBot_UntilResumed()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var whatsApp = new FakeWhatsApp();
        var sent = await SendFromChatAsync(fixture, whatsApp, "5512345678", "Hola, ¿en qué te ayudo?");
        Assert.True(sent.IsSuccess, sent.Error);

        await ReceiveAsync(fixture, whatsApp, tenant.Id, "wamid.in.1", "¿tienen pan?");
        var quietReplies = whatsApp.Texts.Count;

        var resumed = await new SetConversationAutoRepliesCommandHandler(fixture.Context, new Notifications(), fixture.TenantService, fixture.Clock)
            .Handle(new SetConversationAutoRepliesCommand(sent.Value!.ConversationId, Paused: false), CancellationToken.None);
        await ReceiveAsync(fixture, whatsApp, tenant.Id, "wamid.in.2", "¿tienen pan?");

        Assert.Equal(1, quietReplies);
        Assert.True(resumed.IsSuccess, resumed.Error);
        Assert.Null(resumed.Value!.AutoRepliesPausedUntil);
        Assert.Equal(2, whatsApp.Texts.Count);
    }

    [Fact]
    public async Task CustomerAnsweringFromWhatsApp_ShouldJoinTheConversationTheBusinessStarted()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var whatsApp = new FakeWhatsApp();
        var sent = await SendFromChatAsync(fixture, whatsApp, "55 1234 5678", "Tu pedido está listo.");

        // WhatsApp reports the same Mexican mobile with a 1 after the country code.
        var received = await ReceiveAsync(fixture, whatsApp, tenant.Id, "wamid.in.1", "Gracias, voy para allá");

        Assert.Equal("+525512345678", whatsApp.Phones[0]);
        Assert.Equal(sent.Value!.ConversationId, received.Value!.ConversationId);
        Assert.Equal(1, await fixture.Context.WhatsAppConversations.CountAsync());
    }

    [Fact]
    public async Task ChatMessagesAndReplies_ShouldKeepTheirWhatsAppIds_ForTheTicks()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var whatsApp = new FakeWhatsApp();

        await ReceiveAsync(fixture, whatsApp, tenant.Id, "wamid.in.1", "hola");
        await SendFromChatAsync(fixture, whatsApp, WhatsAppPhone, "Con gusto te atiendo.");

        var outbound = await fixture.Context.WhatsAppMessages
            .Where(m => m.Direction == MessageDirection.Outbound)
            .Select(m => m.ExternalMessageId)
            .ToListAsync();
        Assert.Equal(2, outbound.Count);
        Assert.All(outbound, id => Assert.StartsWith("wamid.out.", id));
    }

    [Fact]
    public async Task PhotoFromTheCustomer_ShouldBeKeptWithoutAnAutomaticReply()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var whatsApp = new FakeWhatsApp();

        var received = await ReceiveAsync(fixture, whatsApp, tenant.Id, "wamid.in.1", "📷 Foto: así quiero el pastel", isMedia: true);

        Assert.True(received.IsSuccess, received.Error);
        Assert.Empty(whatsApp.Texts);
        Assert.Equal("📷 Foto: así quiero el pastel", (await fixture.Context.WhatsAppMessages.SingleAsync()).Body);
    }

    private static AiAgentAutoReplyService AutoReplies(SalesTestFixture fixture, string? appUrl = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(appUrl is null ? [] : new Dictionary<string, string?> { ["AppUrl"] = appUrl })
            .Build();
        return new AiAgentAutoReplyService(fixture.Context, configuration);
    }

    private static async Task<AiAgent> AddAgentAsync(SalesTestFixture fixture, Tenant tenant, string? products = null)
    {
        var agent = new AiAgent(tenant.Id, "Respuestas", "Atención");
        agent.UpdateGeneral("Respuestas", "Atención", "Amable", "Español", "Amigable", tenant.Name, null, products);
        agent.UpdateAiConfiguration("keywords", 0.4m, 800, false, strictMode: true);
        fixture.Context.AiAgents.Add(agent);
        await fixture.Context.SaveChangesAsync();
        return agent;
    }

    private static Task<Result<ChatMessageDto>> SendFromChatAsync(SalesTestFixture fixture, FakeWhatsApp whatsApp, string phone, string text)
        => new SendChatMessageCommandHandler(
                fixture.Context,
                whatsApp,
                new Notifications(),
                fixture.TenantService,
                NullLogger<SendChatMessageCommandHandler>.Instance,
                new PhoneNormalizationService(),
                fixture.Clock)
            .Handle(new SendChatMessageCommand { PhoneNumber = phone, Message = text }, CancellationToken.None);

    private static Task<Result<ChatMessageDto>> ReceiveAsync(
        SalesTestFixture fixture, FakeWhatsApp whatsApp, Guid tenantId, string messageId, string body, bool isMedia = false)
        => new ProcessInboundMessageCommandHandler(
                fixture.Context,
                new FixedReply(),
                whatsApp,
                new Notifications(),
                NullLogger<ProcessInboundMessageCommandHandler>.Instance,
                clock: fixture.Clock)
            .Handle(new ProcessInboundMessageCommand
            {
                TenantId = tenantId,
                FromPhone = WhatsAppPhone,
                ProfileName = "Luis",
                Body = body,
                ExternalMessageId = messageId,
                IsMedia = isMedia
            }, CancellationToken.None);

    private sealed class FakeWhatsApp : IWhatsAppService
    {
        public List<string> Texts { get; } = [];
        public List<string> Phones { get; } = [];

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
            => Task.FromResult(MessageSendResult.Sent());

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            Texts.Add(message);
            Phones.Add(phoneNumber);
            return Task.FromResult(MessageSendResult.Sent($"wamid.out.{Texts.Count}"));
        }
    }

    private sealed class FixedReply : IAiAgentAutoReplyService
    {
        public Task<string?> BuildReplyAsync(Guid tenantId, string incomingMessage, string? customerName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("Sí, tenemos pan.");
    }

    private sealed class Notifications : IWhatsAppNotificationService
    {
        public Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message) => Task.CompletedTask;

        public Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation) => Task.CompletedTask;
    }
}
