using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

// Uses SQLite because InMemory does not enforce the unique index on (TenantId, CustomerPhone), nor
// generate the conversation's rowversion.
public class SendChatMessageCommandTests
{
    private const string SentText = "Hola, ¿le ayudamos con su pedido?";

    [Fact]
    public async Task Handle_ShouldStoreSentMessageInWinningConversation_WhenWebhookStartsItFirst()
    {
        await using var fixture = await TestFixture.CreateAsync();
        // The customer also chats with another tenant, whose conversation must stay untouched.
        var otherTenantConversationId = await fixture.StartConversationFromWebhookAsync(
            fixture.OtherTenantId, "wamid.other-tenant");

        // The customer's first message reaches the webhook after this handler sent its message and found
        // no conversation for the number, so only the unique index on (TenantId, CustomerPhone) detects the race.
        var winnerId = Guid.Empty;
        fixture.Database.BeforeNextSave(async () =>
            winnerId = await fixture.StartConversationFromWebhookAsync(fixture.TenantId, "wamid.first"));

        var result = await fixture.CreateHandler().Handle(fixture.Send(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(winnerId, result.Value.ConversationId);
        // Recording the message in the winner's conversation must not send it again.
        Assert.Equal(new[] { SentText }, fixture.WhatsApp.SentMessages);

        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(winnerId, conversation.Id);
        Assert.Equal(new[] { "wamid.first" }, conversation.InboundMessageIds);
        Assert.Equal(new[] { SentText }, conversation.OutboundBodies);
        Assert.Equal(1, conversation.UnreadCount);

        // SignalR announces the message with the conversation as it was stored.
        var (notifiedConversation, notifiedMessage) = Assert.Single(fixture.Notifier.Notifications);
        Assert.Equal(winnerId, notifiedConversation.Id);
        Assert.Equal(1, notifiedConversation.UnreadCount);
        Assert.Equal(winnerId, notifiedMessage.ConversationId);

        var otherTenantConversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.OtherTenantId));
        Assert.Equal(otherTenantConversationId, otherTenantConversation.Id);
        Assert.Equal(new[] { "wamid.other-tenant" }, otherTenantConversation.InboundMessageIds);
        Assert.Empty(otherTenantConversation.OutboundBodies);
    }

    [Fact]
    public async Task Handle_ShouldKeepNewestMessageInSummary_WhenCustomerWritesWhileSentMessageIsSaved()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var conversationId = await fixture.StartConversationFromWebhookAsync(fixture.TenantId, "wamid.first");
        // The customer also chats with another tenant, whose conversation must stay untouched.
        var otherTenantConversationId = await fixture.StartConversationFromWebhookAsync(
            fixture.OtherTenantId, "wamid.other-tenant");

        // The webhook stores the customer's next message after the sent message was added to the
        // conversation and before it is saved.
        fixture.Database.BeforeNextSave(() => fixture.ReceiveFromWebhookAsync(
            fixture.TenantId, "wamid.concurrent", "¿Aceptan pago con tarjeta?"));

        var result = await fixture.CreateHandler().Handle(fixture.Send(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(conversationId, result.Value.ConversationId);
        Assert.Equal(new[] { SentText }, fixture.WhatsApp.SentMessages);

        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.concurrent", "wamid.first" }, conversation.InboundMessageIds);
        Assert.Equal(new[] { SentText }, conversation.OutboundBodies);
        Assert.Equal(2, conversation.UnreadCount);
        AssertSummarizesNewestMessage(conversation);

        // SignalR replaces the conversation in the business's list, so it must carry what was stored.
        var (notifiedConversation, _) = Assert.Single(fixture.Notifier.Notifications);
        Assert.Equal(2, notifiedConversation.UnreadCount);

        var otherTenantConversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.OtherTenantId));
        Assert.Equal(otherTenantConversationId, otherTenantConversation.Id);
        Assert.Equal(new[] { "wamid.other-tenant" }, otherTenantConversation.InboundMessageIds);
        Assert.Empty(otherTenantConversation.OutboundBodies);
        Assert.Equal(1, otherTenantConversation.UnreadCount);
    }

    [Fact]
    public async Task Handle_ShouldRethrow_WhenStartingConversationFailsWithoutConcurrentRequest()
    {
        await using var fixture = await TestFixture.CreateAsync();

        // No other request started the conversation: the failure must surface instead of being retried.
        fixture.Database.CommandFaults.FailNext(
            command => command.CommandText.Contains("INSERT INTO \"WhatsAppConversations\"", StringComparison.Ordinal),
            () => new SqliteException("disk I/O error", 10));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => fixture.CreateHandler().Handle(fixture.Send(), CancellationToken.None));

        Assert.True(fixture.Database.CommandFaults.Fired);
        Assert.Empty(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Single(fixture.WhatsApp.SentMessages);
        Assert.Empty(fixture.Notifier.Notifications);
    }

    [Fact]
    public async Task Handle_ShouldRethrow_WhenStoringInWinningConversationFailsToo()
    {
        await using var fixture = await TestFixture.CreateAsync();

        // The webhook starts the conversation first, and storing the message in it fails as well: the
        // handler joins the winner's conversation only once.
        var winnerId = Guid.Empty;
        fixture.Database.BeforeNextSave(async () =>
        {
            winnerId = await fixture.StartConversationFromWebhookAsync(fixture.TenantId, "wamid.first");
            fixture.Database.BeforeNextSave(() =>
            {
                fixture.Database.CommandFaults.FailNext(
                    command => command.CommandText.Contains("INSERT INTO \"WhatsAppMessages\"", StringComparison.Ordinal),
                    () => new SqliteException("disk I/O error", 10));
                return Task.CompletedTask;
            });
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => fixture.CreateHandler().Handle(fixture.Send(), CancellationToken.None));

        Assert.True(fixture.Database.CommandFaults.Fired);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(winnerId, conversation.Id);
        Assert.Equal(new[] { "wamid.first" }, conversation.InboundMessageIds);
        Assert.Empty(conversation.OutboundBodies);
        Assert.Single(fixture.WhatsApp.SentMessages);
        Assert.Empty(fixture.Notifier.Notifications);
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
        Guid Id, int UnreadCount, string? LastMessageBody, DateTimeOffset LastMessageAt, StoredMessage[] Messages)
    {
        public string[] InboundMessageIds => Messages
            .Where(m => m.Direction == MessageDirection.Inbound)
            .Select(m => m.ExternalMessageId!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        public string[] OutboundBodies => Messages
            .Where(m => m.Direction == MessageDirection.Outbound)
            .Select(m => m.Body)
            .ToArray();
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private const string CustomerPhone = "+5215511112222";


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
            => new(
                Context,
                WhatsApp,
                Notifier,
                new TestCurrentTenantService(TenantId),
                NullLogger<SendChatMessageCommandHandler>.Instance,
                new FoodParkDigital.Infrastructure.Services.PhoneNormalizationService());

        public SendChatMessageCommand Send() => new()
        {
            PhoneNumber = CustomerPhone,
            Message = SentText
        };

        /// <summary>
        /// Delivers the customer's first message to <paramref name="tenantId"/> through the webhook's
        /// handler, in its own context like a separate request.
        /// </summary>
        public Task<Guid> StartConversationFromWebhookAsync(Guid tenantId, string externalMessageId)
            => ReceiveFromWebhookAsync(tenantId, externalMessageId, "Hola, quiero hacer un pedido");

        /// <summary>
        /// Delivers a message from the customer to <paramref name="tenantId"/> through the webhook's
        /// handler, in its own context like a separate request, and returns its conversation.
        /// </summary>
        public async Task<Guid> ReceiveFromWebhookAsync(Guid tenantId, string externalMessageId, string body)
        {
            await using var webhookContext = Database.CreateIndependentContext();
            var webhook = new ProcessInboundMessageCommandHandler(
                webhookContext,
                new NoAutoReplyService(),
                new RecordingWhatsAppService(),
                new RecordingNotificationService(),
                NullLogger<ProcessInboundMessageCommandHandler>.Instance);

            var result = await webhook.Handle(new ProcessInboundMessageCommand
            {
                TenantId = tenantId,
                FromPhone = CustomerPhone,
                ProfileName = "Cliente",
                Body = body,
                ExternalMessageId = externalMessageId
            }, CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error);
            return result.Value.ConversationId;
        }

        /// <summary>Reads the customer's committed conversations through a fresh context.</summary>
        public async Task<StoredConversation[]> StoredConversationsAsync(Guid tenantId)
        {
            await using var context = Database.CreateIndependentContext();
            var conversations = await context.WhatsAppConversations
                .IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.CustomerPhone == CustomerPhone)
                .Select(c => new
                {
                    c.Id,
                    c.UnreadCount,
                    c.LastMessageBody,
                    c.LastMessageAt,
                    Messages = c.Messages
                        .Select(m => new StoredMessage(m.Direction, m.ExternalMessageId, m.Body, m.SentAt))
                        .ToList()
                })
                .ToListAsync();

            return conversations
                .Select(c => new StoredConversation(
                    c.Id, c.UnreadCount, c.LastMessageBody, c.LastMessageAt, c.Messages.ToArray()))
                .ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class NoAutoReplyService : IAiAgentAutoReplyService
    {
        public Task<string?> BuildReplyAsync(
            Guid tenantId, string incomingMessage, string? customerName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class RecordingWhatsAppService : IWhatsAppService
    {
        public List<string> SentMessages { get; } = [];

        public Task<MessageSendResult> SendTemplateMessageAsync(
            Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MessageSendResult.Sent());

        public Task<MessageSendResult> SendTextMessageAsync(
            Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.FromResult(MessageSendResult.Sent());
        }
    }

    private sealed class RecordingNotificationService : IWhatsAppNotificationService
    {
        public List<(ConversationDto Conversation, ChatMessageDto Message)> Notifications { get; } = [];

        public Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message)
        {
            Notifications.Add((conversation, message));
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
