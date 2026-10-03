using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

// Uses SQLite so another request can commit between this handler's read and its write.
public class MarkConversationReadCommandTests
{
    [Fact]
    public async Task Handle_ShouldMarkConversationAsItIsNow_WhenMessageIsStoredBeforeThisSave()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var conversationId = await fixture.StartConversationAsync(fixture.TenantId, "wamid.first");

        // The webhook stores the customer's next message after the conversation was loaded and before
        // it is marked read. The open chat receives that message over SignalR, as it did before
        // conversations had a concurrency token, so the read covers it.
        fixture.Database.BeforeNextSave(() => fixture.StoreInboundFromWebhookAsync(conversationId, "wamid.concurrent"));

        var result = await fixture.CreateHandler().Handle(
            new MarkConversationReadCommand(conversationId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var conversation = await fixture.StoredConversationAsync(conversationId);
        Assert.Equal(new[] { "wamid.concurrent", "wamid.first" }, conversation.InboundMessageIds);
        Assert.Equal(0, conversation.UnreadCount);
    }

    [Fact]
    public async Task Handle_ShouldNotMarkConversationOfAnotherTenant()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var otherTenantConversationId = await fixture.StartConversationAsync(fixture.OtherTenantId, "wamid.other-tenant");

        var result = await fixture.CreateHandler().Handle(
            new MarkConversationReadCommand(otherTenantConversationId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, (await fixture.StoredConversationAsync(otherTenantConversationId)).UnreadCount);
    }

    private sealed record StoredConversation(int UnreadCount, string[] InboundMessageIds);

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

        public MarkConversationReadCommandHandler CreateHandler() => new(Context);

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
                    InboundMessageIds = c.Messages
                        .Where(m => m.Direction == MessageDirection.Inbound)
                        .Select(m => m.ExternalMessageId!)
                        .ToList()
                })
                .SingleAsync();

            return new StoredConversation(
                conversation.UnreadCount, conversation.InboundMessageIds.Order(StringComparer.Ordinal).ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
