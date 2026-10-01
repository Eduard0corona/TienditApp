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
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

// Uses SQLite because InMemory does not enforce the unique index on ExternalMessageId.
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

    private sealed class TestFixture : IAsyncDisposable
    {
        private const string CustomerPhone = "+5215511112222";

        private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>())
            .CreateMapper();

        private TestFixture(SqliteTestDatabase database, ApplicationDbContext context, Guid tenantId)
        {
            Database = database;
            Context = context;
            TenantId = tenantId;
        }

        public SqliteTestDatabase Database { get; }
        public ApplicationDbContext Context { get; }
        public Guid TenantId { get; }
        public RecordingNotificationService Notifier { get; } = new();
        public RecordingWhatsAppService WhatsApp { get; } = new();

        public static async Task<TestFixture> CreateAsync()
        {
            var database = await SqliteTestDatabase.CreateAsync();
            var context = database.CreateContext();

            var tenant = new Tenant("Tenant WhatsApp", "tenant-whatsapp", BusinessType.Food, "whatsapp@tenant.com");
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            return new TestFixture(database, context, tenant.Id);
        }

        public ProcessInboundMessageCommandHandler CreateHandler()
            => new(
                Context,
                new FixedAutoReplyService(),
                WhatsApp,
                Notifier,
                _mapper,
                NullLogger<ProcessInboundMessageCommandHandler>.Instance);

        public ProcessInboundMessageCommand Inbound(string externalMessageId) => new()
        {
            TenantId = TenantId,
            FromPhone = CustomerPhone,
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

        public Task<int> CountMessagesAsync(string externalMessageId)
            => Context.WhatsAppMessages
                .IgnoreQueryFilters()
                .CountAsync(m => m.ExternalMessageId == externalMessageId);

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

    private sealed class FixedAutoReplyService : IAiAgentAutoReplyService
    {
        public Task<string?> BuildReplyAsync(
            Guid tenantId, string incomingMessage, string? customerName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("Gracias por escribir.");
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
        public List<ChatMessageDto> Messages { get; } = [];

        public Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation) => Task.CompletedTask;
    }
}
