using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

// Uses SQLite because InMemory does not enforce the unique indexes on ExternalMessageId and
// (TenantId, CustomerPhone), nor generate the conversation's rowversion.
public class ProcessInboundMessageCommandTests
{
    [Fact]
    public async Task Handle_ShouldStoreNotifyAndAutoReply_WhenMessageIsNew()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.new"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, await fixture.CountMessagesAsync("wamid.new"));
        Assert.Equal(2, fixture.Notifier.Messages.Count);
        Assert.Single(fixture.WhatsApp.SentMessages);
    }

    [Fact]
    public async Task Handle_ShouldIgnoreRetry_WhenMessageWasAlreadyStored()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.CreateHandler().Handle(fixture.Inbound("wamid.retry"), CancellationToken.None);
        fixture.ClearSideEffects();

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.retry"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProcessInboundMessageCommandHandler.DuplicateMessageError, result.Error);
        Assert.Equal(1, await fixture.CountMessagesAsync("wamid.retry"));
        Assert.Empty(fixture.Notifier.Messages);
        Assert.Empty(fixture.WhatsApp.SentMessages);
    }

    [Fact]
    public async Task Handle_ShouldTreatUniqueViolationAsDuplicate_WhenConcurrentDeliveryStoresMessageFirst()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var conversationId = await fixture.SeedConversationAsync();

        // A concurrent delivery commits the same message after this handler's duplicate
        // check but before its insert, so only the unique index can detect it.
        fixture.Database.BeforeNextSave(() => fixture.StoreFromConcurrentDeliveryAsync(conversationId, "wamid.race"));

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.race"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProcessInboundMessageCommandHandler.DuplicateMessageError, result.Error);
        Assert.Equal(1, await fixture.CountMessagesAsync("wamid.race"));
        Assert.Empty(fixture.Notifier.Messages);
        Assert.Empty(fixture.WhatsApp.SentMessages);

        // The same scoped DbContext keeps serving the rest of the webhook payload.
        var next = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.next"), CancellationToken.None);

        Assert.True(next.IsSuccess, next.Error);
        Assert.Equal(1, await fixture.CountMessagesAsync("wamid.next"));
    }

    [Fact]
    public async Task Handle_ShouldStoreMessageInWinningConversation_WhenConcurrentFirstMessageStartsItFirst()
    {
        await using var fixture = await TestFixture.CreateAsync();
        // The customer also chats with another tenant, whose conversation must stay untouched.
        await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.OtherTenantId, "wamid.other-tenant");

        // Another first message from this new number starts the conversation after this handler
        // found none, so only the unique index on (TenantId, CustomerPhone) detects the race.
        var winnerId = Guid.Empty;
        fixture.Database.BeforeNextSave(async () =>
            winnerId = await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.TenantId, "wamid.first"));

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.second"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(winnerId, result.Value.ConversationId);

        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(winnerId, conversation.Id);
        Assert.Equal(new[] { "wamid.first", "wamid.second" }, conversation.InboundMessageIds);
        Assert.Equal(2, conversation.UnreadCount);

        var otherTenantConversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.OtherTenantId));
        Assert.Equal(new[] { "wamid.other-tenant" }, otherTenantConversation.InboundMessageIds);

        // The inbound message and the auto-reply are announced in the conversation that stores them.
        Assert.Equal(2, fixture.Notifier.Messages.Count);
        Assert.All(fixture.Notifier.Messages, message => Assert.Equal(winnerId, message.ConversationId));
        Assert.Single(fixture.WhatsApp.SentMessages);
    }

    [Fact]
    public async Task Handle_ShouldTreatAsDuplicate_WhenConcurrentDeliveryStartsConversationWithSameMessage()
    {
        await using var fixture = await TestFixture.CreateAsync();

        // Meta redelivers the first message of a new number while the original delivery is still
        // being processed, so both deliveries try to start the conversation with it.
        fixture.Database.BeforeNextSave(() =>
            fixture.StartConversationFromConcurrentDeliveryAsync(fixture.TenantId, "wamid.first"));

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.first"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProcessInboundMessageCommandHandler.DuplicateMessageError, result.Error);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.first" }, conversation.InboundMessageIds);
        Assert.Empty(fixture.Notifier.Messages);
        Assert.Empty(fixture.WhatsApp.SentMessages);
    }

    [Fact]
    public async Task Handle_ShouldTreatAsDuplicate_WhenSameMessageIsStoredWhileJoiningWinningConversation()
    {
        await using var fixture = await TestFixture.CreateAsync();

        // Another first message starts the conversation, and a redelivery of this message stores it
        // there right before this handler joins that conversation.
        fixture.Database.BeforeNextSave(async () =>
        {
            var winnerId = await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.TenantId, "wamid.first");
            fixture.Database.BeforeNextSave(() => fixture.StoreFromConcurrentDeliveryAsync(winnerId, "wamid.second"));
        });

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProcessInboundMessageCommandHandler.DuplicateMessageError, result.Error);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.first", "wamid.second" }, conversation.InboundMessageIds);
        Assert.Empty(fixture.Notifier.Messages);
        Assert.Empty(fixture.WhatsApp.SentMessages);
    }

    [Fact]
    public async Task Handle_ShouldRethrowAndKeepContextUsable_WhenStartingConversationFailsWithoutConcurrentDelivery()
    {
        await using var fixture = await TestFixture.CreateAsync();

        // Neither a duplicate nor a lost race: the failure must surface instead of being retried or swallowed.
        fixture.Database.CommandFaults.FailNext(
            command => command.CommandText.Contains("INSERT INTO \"WhatsAppConversations\"", StringComparison.Ordinal),
            () => new SqliteException("disk I/O error", 10));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => fixture.CreateHandler().Handle(fixture.Inbound("wamid.failed"), CancellationToken.None));

        Assert.True(fixture.Database.CommandFaults.Fired);
        Assert.Empty(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Empty(fixture.Notifier.Messages);
        Assert.Empty(fixture.WhatsApp.SentMessages);

        // Nothing staged by the failed attempt stays tracked for the rest of the webhook payload.
        var next = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.next"), CancellationToken.None);

        Assert.True(next.IsSuccess, next.Error);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.next" }, conversation.InboundMessageIds);
    }

    [Fact]
    public async Task Handle_ShouldRethrowAndKeepContextUsable_WhenDatabaseIsUnavailableDuringSave()
    {
        await using var fixture = await TestFixture.CreateAsync();

        // The execution strategy gives up on an outage with an exception that is not a DbUpdateException.
        fixture.Database.BeforeNextSave(() => throw new RetryLimitExceededException(
            "Simulated database outage.", new TimeoutException()));

        await Assert.ThrowsAsync<RetryLimitExceededException>(
            () => fixture.CreateHandler().Handle(fixture.Inbound("wamid.outage"), CancellationToken.None));

        Assert.Empty(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Empty(fixture.Notifier.Messages);
        Assert.Empty(fixture.WhatsApp.SentMessages);

        // Nothing staged by the failed attempt stays tracked for the rest of the webhook payload.
        var next = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.next"), CancellationToken.None);

        Assert.True(next.IsSuccess, next.Error);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.next" }, conversation.InboundMessageIds);
    }

    [Fact]
    public async Task Handle_ShouldKeepConcurrentMessageInSummary_WhenAnotherDeliveryStoresItBeforeThisSave()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.AutoReply.Reply = null;
        var conversationId = await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.TenantId, "wamid.first");
        // The customer also chats with another tenant, whose conversation must stay untouched.
        await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.OtherTenantId, "wamid.other-tenant");

        // Meta delivers the customer's next messages in parallel requests: another one is stored after
        // this handler loaded the conversation and before it saves.
        fixture.Database.BeforeNextSave(() =>
            fixture.AddMessageFromConcurrentDeliveryAsync(conversationId, "wamid.concurrent"));

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.handled"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.concurrent", "wamid.first", "wamid.handled" }, conversation.InboundMessageIds);
        Assert.Equal(3, conversation.UnreadCount);
        AssertSummarizesNewestMessage(conversation);

        // SignalR replaces the conversation in the business's list, so it must carry what was stored.
        Assert.Equal(3, Assert.Single(fixture.Notifier.Conversations).UnreadCount);

        var otherTenantConversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.OtherTenantId));
        Assert.Equal(new[] { "wamid.other-tenant" }, otherTenantConversation.InboundMessageIds);
        Assert.Equal(1, otherTenantConversation.UnreadCount);
    }

    [Fact]
    public async Task Handle_ShouldCountOnlyThisMessageAsUnread_WhenConversationIsMarkedReadBeforeThisSave()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.AutoReply.Reply = null;
        var conversationId = await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.TenantId, "wamid.first");

        // The business opens the chat and marks it read after this handler loaded the conversation
        // and before it saves.
        fixture.Database.BeforeNextSave(() => fixture.MarkReadFromConcurrentRequestAsync(conversationId));

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.handled"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(1, conversation.UnreadCount);
        Assert.Equal(1, Assert.Single(fixture.Notifier.Conversations).UnreadCount);
    }

    [Fact]
    public async Task Handle_ShouldKeepNewestMessageInSummary_WhenCustomerWritesAgainWhileAutoReplyIsSaved()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var conversationId = await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.TenantId, "wamid.first");

        // The inbound message is saved first; the customer's next message is stored after the
        // auto-reply was added to the conversation and before it is saved.
        fixture.Database.BeforeNextSave(() =>
        {
            fixture.Database.BeforeNextSave(() =>
                fixture.AddMessageFromConcurrentDeliveryAsync(conversationId, "wamid.concurrent"));
            return Task.CompletedTask;
        });

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.handled"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.concurrent", "wamid.first", "wamid.handled" }, conversation.InboundMessageIds);
        Assert.Single(conversation.Messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal(3, conversation.UnreadCount);
        AssertSummarizesNewestMessage(conversation);

        Assert.Single(fixture.WhatsApp.SentMessages);
        Assert.Equal(2, fixture.Notifier.Conversations.Count);
        Assert.Equal(3, fixture.Notifier.Conversations[^1].UnreadCount);
    }

    [Fact]
    public async Task Handle_ShouldCountMessageStoredByAnotherDelivery_WhenSamePayloadAddsItsNextMessage()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.AutoReply.Reply = null;

        // The first message of the payload leaves the conversation tracked by the request's context.
        var first = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.first"), CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error);

        // A parallel delivery stores another message before the payload's next message is handled.
        await fixture.AddMessageFromConcurrentDeliveryAsync(first.Value.ConversationId, "wamid.concurrent");

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.second"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.concurrent", "wamid.first", "wamid.second" }, conversation.InboundMessageIds);
        Assert.Equal(3, conversation.UnreadCount);
        AssertSummarizesNewestMessage(conversation);
        Assert.Equal(3, fixture.Notifier.Conversations[^1].UnreadCount);
    }

    [Fact]
    public async Task Handle_ShouldRethrowAndKeepContextUsable_WhenEveryAttemptFindsConversationChanged()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.AutoReply.Reply = null;
        var conversationId = await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.TenantId, "wamid.first");

        // Another request changes the conversation right before every attempt to save this message.
        fixture.ChangeConversationBeforeNextSaves(conversationId, WhatsAppConversationConcurrency.MaxSaveAttempts);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => fixture.CreateHandler().Handle(fixture.Inbound("wamid.contended"), CancellationToken.None));

        // Nothing was stored or announced, so the webhook answers 500 and Meta redelivers the message.
        Assert.Equal(0, await fixture.CountMessagesAsync("wamid.contended"));
        Assert.Empty(fixture.Notifier.Messages);

        // Nothing staged by the failed attempts stays tracked to be saved with the rest of the webhook
        // payload, such as another customer's message.
        var next = await fixture.CreateHandler().Handle(
            fixture.Inbound("wamid.other-customer", TestFixture.OtherCustomerPhone), CancellationToken.None);

        Assert.True(next.IsSuccess, next.Error);
        var otherCustomerConversation = Assert.Single(
            await fixture.StoredConversationsAsync(fixture.TenantId, TestFixture.OtherCustomerPhone));
        Assert.Equal(new[] { "wamid.other-customer" }, otherCustomerConversation.InboundMessageIds);

        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.first" }, conversation.InboundMessageIds);
        Assert.Equal(1, conversation.UnreadCount);
    }

    [Fact]
    public async Task Handle_ShouldKeepInboundMessageAndContextUsable_WhenEveryAttemptToRecordAutoReplyFindsConversationChanged()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var conversationId = await fixture.StartConversationFromConcurrentDeliveryAsync(fixture.TenantId, "wamid.first");

        // The inbound message is saved; then another request changes the conversation right before
        // every attempt to record the auto-reply.
        fixture.Database.BeforeNextSave(() =>
        {
            fixture.ChangeConversationBeforeNextSaves(conversationId, WhatsAppConversationConcurrency.MaxSaveAttempts);
            return Task.CompletedTask;
        });

        var result = await fixture.CreateHandler().Handle(fixture.Inbound("wamid.handled"), CancellationToken.None);

        // The inbound message is stored and announced; the reply was sent but could not be recorded.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(fixture.Notifier.Messages);
        Assert.Single(fixture.WhatsApp.SentMessages);

        var conversation = Assert.Single(await fixture.StoredConversationsAsync(fixture.TenantId));
        Assert.Equal(new[] { "wamid.first", "wamid.handled" }, conversation.InboundMessageIds);
        Assert.DoesNotContain(conversation.Messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal(2, conversation.UnreadCount);

        // Nothing staged by the failed attempts stays tracked to be saved with the rest of the webhook
        // payload, such as another customer's message.
        fixture.AutoReply.Reply = null;
        var next = await fixture.CreateHandler().Handle(
            fixture.Inbound("wamid.other-customer", TestFixture.OtherCustomerPhone), CancellationToken.None);

        Assert.True(next.IsSuccess, next.Error);
        var otherCustomerConversation = Assert.Single(
            await fixture.StoredConversationsAsync(fixture.TenantId, TestFixture.OtherCustomerPhone));
        Assert.Equal(new[] { "wamid.other-customer" }, otherCustomerConversation.InboundMessageIds);
        Assert.DoesNotContain(
            (await fixture.StoredConversationsAsync(fixture.TenantId)).Single().Messages,
            m => m.Direction == MessageDirection.Outbound);
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
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private const string CustomerPhone = "+5215511112222";
        public const string OtherCustomerPhone = "+5215533334444";


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
        public StubAutoReplyService AutoReply { get; } = new();

        public static async Task<TestFixture> CreateAsync()
        {
            var database = await SqliteTestDatabase.CreateAsync();
            var context = database.CreateContext();

            var tenant = new Tenant("Tenant WhatsApp", "tenant-whatsapp", BusinessType.Food, "whatsapp@tenant.com");
            var otherTenant = new Tenant("Otro Tenant", "otro-tenant", BusinessType.Retail, "otro@tenant.com");
            context.Tenants.AddRange(tenant, otherTenant);
            await context.SaveChangesAsync();
            // The webhook processes each message in the scope of the business it was sent to.
            database.SetTenant(tenant.Id);

            return new TestFixture(database, context, tenant.Id, otherTenant.Id);
        }

        public ProcessInboundMessageCommandHandler CreateHandler()
            => new(
                Context,
                Database.TenantService,
                AutoReply,
                WhatsApp,
                Notifier,
                NullLogger<ProcessInboundMessageCommandHandler>.Instance);

        public ProcessInboundMessageCommand Inbound(string externalMessageId, string fromPhone = CustomerPhone) => new()
        {
            TenantId = TenantId,
            FromPhone = fromPhone,
            ProfileName = "Cliente",
            Body = "Hola, quiero hacer un pedido",
            ExternalMessageId = externalMessageId
        };

        public async Task<Guid> SeedConversationAsync()
        {
            var conversation = new WhatsAppConversation(TenantId, CustomerPhone, "Cliente");
            Context.WhatsAppConversations.Add(conversation);
            await Context.SaveChangesAsync();
            return conversation.Id;
        }

        public async Task StoreFromConcurrentDeliveryAsync(Guid conversationId, string externalMessageId)
        {
            await using var concurrentContext = Database.CreateIndependentContext();
            concurrentContext.WhatsAppMessages.Add(
                new WhatsAppMessage(conversationId, MessageDirection.Inbound, "Hola, quiero hacer un pedido", externalMessageId));
            await concurrentContext.SaveChangesAsync();
        }

        public async Task<Guid> StartConversationFromConcurrentDeliveryAsync(Guid tenantId, string externalMessageId)
        {
            await using var concurrentContext = Database.CreateIndependentContext();
            var conversation = new WhatsAppConversation(tenantId, CustomerPhone, "Cliente");
            conversation.AddMessage(MessageDirection.Inbound, "Hola, ¿tienen servicio a domicilio?", externalMessageId);
            concurrentContext.WhatsAppConversations.Add(conversation);
            await concurrentContext.SaveChangesAsync();
            return conversation.Id;
        }

        public async Task AddMessageFromConcurrentDeliveryAsync(Guid conversationId, string externalMessageId)
        {
            await using var concurrentContext = Database.CreateIndependentContext();
            var conversation = await concurrentContext.WhatsAppConversations
                .IgnoreQueryFilters()
                .SingleAsync(c => c.Id == conversationId);
            conversation.AddMessage(MessageDirection.Inbound, "¿Aceptan pago con tarjeta?", externalMessageId);
            await concurrentContext.SaveChangesAsync();
        }

        public async Task MarkReadFromConcurrentRequestAsync(Guid conversationId)
        {
            await using var concurrentContext = Database.CreateIndependentContext();
            var conversation = await concurrentContext.WhatsAppConversations
                .IgnoreQueryFilters()
                .SingleAsync(c => c.Id == conversationId);
            conversation.MarkAsRead();
            await concurrentContext.SaveChangesAsync();
        }

        /// <summary>Makes another request change the conversation right before each of the next saves.</summary>
        public void ChangeConversationBeforeNextSaves(Guid conversationId, int saves)
        {
            if (saves == 0)
                return;

            Database.BeforeNextSave(async () =>
            {
                await using var concurrentContext = Database.CreateIndependentContext();
                var conversation = await concurrentContext.WhatsAppConversations
                    .IgnoreQueryFilters()
                    .SingleAsync(c => c.Id == conversationId);
                conversation.UpdateCustomerName($"Cliente {saves}");
                await concurrentContext.SaveChangesAsync();

                ChangeConversationBeforeNextSaves(conversationId, saves - 1);
            });
        }

        public Task<int> CountMessagesAsync(string externalMessageId)
            => Context.WhatsAppMessages
                .IgnoreQueryFilters()
                .CountAsync(m => m.ExternalMessageId == externalMessageId);

        /// <summary>Reads the customer's committed conversations through a fresh context.</summary>
        public async Task<StoredConversation[]> StoredConversationsAsync(Guid tenantId, string customerPhone = CustomerPhone)
        {
            await using var context = Database.CreateIndependentContext();
            var conversations = await context.WhatsAppConversations
                .IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.CustomerPhone == customerPhone)
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

        public void ClearSideEffects()
        {
            Notifier.Messages.Clear();
            WhatsApp.SentMessages.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class StubAutoReplyService : IAiAgentAutoReplyService
    {
        /// <summary>The reply the AI agent builds; <c>null</c> when the tenant has no auto-reply.</summary>
        public string? Reply { get; set; } = "Gracias por escribir.";

        public Task<string?> BuildReplyAsync(
            Guid tenantId, string incomingMessage, string? customerName, CancellationToken cancellationToken = default)
            => Task.FromResult(Reply);
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
        public List<ChatMessageDto> Messages { get; } = [];
        public List<ConversationDto> Conversations { get; } = [];

        public Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message)
        {
            Messages.Add(message);
            Conversations.Add(conversation);
            return Task.CompletedTask;
        }

        public Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation) => Task.CompletedTask;
    }
}
