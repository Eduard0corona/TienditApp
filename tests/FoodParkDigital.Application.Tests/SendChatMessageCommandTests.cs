using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

// Uses SQLite so another request can commit between this handler's read and its write.
public class SendChatMessageCommandTests
{
    [Fact]
    public async Task Handle_ShouldKeepNewestMessageInSummary_WhenCustomerWritesWhileReplyIsSaved()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var conversationId = await fixture.StartConversationAsync(fixture.TenantId, "wamid.first");
        // The customer also chats with another tenant, whose conversation must stay untouched.
        var otherTenantConversationId = await fixture.StartConversationAsync(fixture.OtherTenantId, "wamid.other-tenant");

        // The webhook stores the customer's next message after the business's reply was added to the
        // conversation and before the reply is saved.
        fixture.Database.BeforeNextSave(() => fixture.StoreInboundFromWebhookAsync(conversationId, "wamid.concurrent"));

        var result = await fixture.CreateHandler().Handle(fixture.Reply(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(conversationId, result.Value.ConversationId);
        Assert.Single(fixture.WhatsApp.SentMessages);

        var conversation = await fixture.StoredConversationAsync(conversationId);
        Assert.Equal(new[] { "wamid.concurrent", "wamid.first" }, conversation.InboundMessageIds);
        Assert.Single(conversation.Messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal(2, conversation.UnreadCount);
        AssertSummarizesNewestMessage(conversation);

        // SignalR replaces the conversation in the business's list, so it must carry what was stored.
        Assert.Equal(2, Assert.Single(fixture.Notifier.Conversations).UnreadCount);

        var otherTenantConversation = await fixture.StoredConversationAsync(otherTenantConversationId);
        Assert.Equal(new[] { "wamid.other-tenant" }, otherTenantConversation.InboundMessageIds);
        Assert.Equal(1, otherTenantConversation.UnreadCount);
        AssertSummarizesNewestMessage(otherTenantConversation);
    }

    /// <summary>The conversation list previews the newest message, the one the chat shows last.</summary>
    private static void AssertSummarizesNewestMessage(StoredConversation conversation)
    {
        var newest = conversation.Messages.MaxBy(m => m.SentAt)!;
        Assert.Equal(newest.SentAt, conversation.LastMessageAt);
        Assert.Equal(newest.Body, conversation.LastMessageBody);
    }

    private sealed record StoredMessage(
        MessageDirection Direction, string? ExternalMessageId, string Body, DateTimeOffset SentAt);

    private sealed record StoredConversation(
        int UnreadCount, string? LastMessageBody, DateTimeOffset LastMessageAt, StoredMessage[] Messages)
    {
        public string[] InboundMessageIds => Messages
            .Where(m => m.Direction == MessageDirection.Inbound)
            .Select(m => m.ExternalMessageId!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private const string CustomerPhone = "+5215511112222";

        private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>())
            .CreateMapper();

        private TestFixture(SqliteTestDatabase database, ApplicationDbContext context, Guid tenantId, Guid otherTenantId)
        {
            Database = database;
            Context = context;
            TenantId = tenantId;
            OtherTenantId = otherTenantId;
        }

        public SqliteTestDatabase Database { get; }
        public ApplicationDbContext Context { get; }
        public Guid TenantId { get; }
        public Guid OtherTenantId { get; }
        public RecordingNotificationService Notifier { get; } = new();
        public RecordingWhatsAppService WhatsApp { get; } = new();

        public static async Task<TestFixture> CreateAsync()
        {
            var database = await SqliteTestDatabase.CreateAsync();
            var context = database.CreateContext();

            var tenant = new Tenant("Tenant WhatsApp", "tenant-whatsapp", BusinessType.Food, "whatsapp@tenant.com");
            var otherTenant = new Tenant("Otro Tenant", "otro-tenant", BusinessType.Retail, "otro@tenant.com");
            context.Tenants.AddRange(tenant, otherTenant);
            await context.SaveChangesAsync();

            // The business user's request: the global filters only see this tenant's conversations.
            database.SetTenant(tenant.Id);

            return new TestFixture(database, context, tenant.Id, otherTenant.Id);
        }

        public SendChatMessageCommandHandler CreateHandler()
            => new(Context, WhatsApp, Notifier, new TestCurrentTenantService(TenantId), _mapper);

        public SendChatMessageCommand Reply() => new()
        {
            PhoneNumber = CustomerPhone,
            Message = "Claro, ¿qué le gustaría pedir?"
        };

        public async Task<Guid> StartConversationAsync(Guid tenantId, string externalMessageId)
        {
            await using var otherContext = Database.CreateIndependentContext();
            var conversation = new WhatsAppConversation(tenantId, CustomerPhone, "Cliente");
            conversation.AddMessage(MessageDirection.Inbound, "Hola, quiero hacer un pedido", externalMessageId);
            otherContext.WhatsAppConversations.Add(conversation);
            await otherContext.SaveChangesAsync();
            return conversation.Id;
        }

        /// <summary>Stores an inbound message the way the webhook does, through its own context.</summary>
        public async Task StoreInboundFromWebhookAsync(Guid conversationId, string externalMessageId)
        {
            await using var webhookContext = Database.CreateIndependentContext();
            var conversation = await webhookContext.WhatsAppConversations
                .IgnoreQueryFilters()
                .SingleAsync(c => c.Id == conversationId);
            conversation.AddMessage(MessageDirection.Inbound, "¿Aceptan pago con tarjeta?", externalMessageId);
            await webhookContext.SaveChangesAsync();
        }

        /// <summary>Reads the committed conversation through a fresh context.</summary>
        public async Task<StoredConversation> StoredConversationAsync(Guid conversationId)
        {
            await using var context = Database.CreateIndependentContext();
            var conversation = await context.WhatsAppConversations
                .IgnoreQueryFilters()
                .Where(c => c.Id == conversationId)
                .Select(c => new
                {
                    c.UnreadCount,
                    c.LastMessageBody,
                    c.LastMessageAt,
                    Messages = c.Messages
                        .Select(m => new StoredMessage(m.Direction, m.ExternalMessageId, m.Body, m.SentAt))
                        .ToList()
                })
                .SingleAsync();

            return new StoredConversation(
                conversation.UnreadCount, conversation.LastMessageBody, conversation.LastMessageAt,
                conversation.Messages.ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class RecordingWhatsAppService : IWhatsAppService
    {
        public List<string> SentMessages { get; } = [];

        public Task<Result> SendTemplateMessageAsync(
            Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result> SendTextMessageAsync(
            Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class RecordingNotificationService : IWhatsAppNotificationService
    {
        public List<ConversationDto> Conversations { get; } = [];

        public Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message)
        {
            Conversations.Add(conversation);
            return Task.CompletedTask;
        }

        public Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation) => Task.CompletedTask;
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public TestCurrentTenantService(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
