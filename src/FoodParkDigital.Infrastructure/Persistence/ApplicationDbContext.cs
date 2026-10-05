using System.Linq.Expressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Dining.Entities;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Reporting.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IApplicationDbContext
{
    private readonly ICurrentTenantService _tenantService;
    private readonly IMediator _mediator;
    private bool _isDispatchingEvents;
    // Events saved inside a transaction, published when it commits (PublishCommittedEventsAsync).
    private readonly List<DomainEvent> _eventsAwaitingCommit = [];
    private IDbContextTransaction? _eventsTransaction;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator)
        : base(options)
    {
        _tenantService = tenantService;
        _mediator = mediator;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(cancellationToken);

        // Realtime handlers (SignalR) run in-process once the change is committed, best-effort. Durable side effects
        // (audit, customer messages) come from the copies OutboxSaveChangesInterceptor stored in the same
        // transaction, dispatched in the background. The flag prevents recursion when a handler saves.
        if (_isDispatchingEvents)
            return result;

        var entities = ChangeTracker
            .Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = entities.SelectMany(e => e.DomainEvents).ToList();
        entities.ForEach(e => e.ClearDomainEvents());

        if (Database.CurrentTransaction is { } transaction)
        {
            // Not committed yet: a screen reloading now would not find it. Events of an earlier transaction that
            // never committed (rolled back, or an attempt being retried) are dropped.
            if (!ReferenceEquals(transaction, _eventsTransaction))
            {
                _eventsAwaitingCommit.Clear();
                _eventsTransaction = transaction;
            }

            _eventsAwaitingCommit.AddRange(domainEvents);
            return result;
        }

        await PublishAsync(domainEvents, cancellationToken);
        return result;
    }

    public async Task PublishCommittedEventsAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(transaction, _eventsTransaction))
            return;

        var domainEvents = _eventsAwaitingCommit.ToList();
        _eventsAwaitingCommit.Clear();
        _eventsTransaction = null;
        await PublishAsync(domainEvents, cancellationToken);
    }

    private async Task PublishAsync(IReadOnlyList<DomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        if (domainEvents.Count == 0)
            return;

        _isDispatchingEvents = true;
        try
        {
            foreach (var domainEvent in domainEvents)
            {
                try
                {
                    await _mediator.Publish(domainEvent, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // The change is committed: a failed screen refresh must not turn it into an error.
                    // Screens also reload when SignalR reconnects and on their polling fallback.
                    LogRealtimeFailure(ex, domainEvent);
                }
            }
        }
        finally
        {
            _isDispatchingEvents = false;
        }
    }

    private Guid CurrentTenantId => _tenantService.TenantId;

    private void LogRealtimeFailure(Exception exception, DomainEvent domainEvent)
    {
        try
        {
            this.GetService<ILoggerFactory>()
                .CreateLogger<ApplicationDbContext>()
                .LogError(
                    LogRedaction.RedactDatabaseMessages(exception),
                    "Realtime handler failed for {EventType} {EventId}",
                    domainEvent.GetType().Name,
                    domainEvent.EventId);
        }
        catch (InvalidOperationException)
        {
            // No logger available (a context built by hand in tests).
        }
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<WhatsAppConfig> WhatsAppConfigs => Set<WhatsAppConfig>();
    public DbSet<WhatsAppConversation> WhatsAppConversations => Set<WhatsAppConversation>();
    public DbSet<WhatsAppMessage> WhatsAppMessages => Set<WhatsAppMessage>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<TemporaryCustomer> TemporaryCustomers => Set<TemporaryCustomer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<FolioSequence> FolioSequences => Set<FolioSequence>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<OrderRefund> OrderRefunds => Set<OrderRefund>();
    public DbSet<ServiceOrderDetail> ServiceOrderDetails => Set<ServiceOrderDetail>();
    public DbSet<ServiceOrderPiece> ServiceOrderPieces => Set<ServiceOrderPiece>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AiAgent> AiAgents => Set<AiAgent>();
    public DbSet<SupplyItem> SupplyItems => Set<SupplyItem>();
    public DbSet<InventoryLocation> InventoryLocations => Set<InventoryLocation>();
    public DbSet<InventoryStock> InventoryStocks => Set<InventoryStock>();
    public DbSet<ProductSupplyRequirement> ProductSupplyRequirements => Set<ProductSupplyRequirement>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryMovementLine> InventoryMovementLines => Set<InventoryMovementLine>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OwnerWhatsAppConfig> OwnerWhatsAppConfigs => Set<OwnerWhatsAppConfig>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<MessageCampaign> MessageCampaigns => Set<MessageCampaign>();
    public DbSet<OutgoingMessage> OutgoingMessages => Set<OutgoingMessage>();
    public DbSet<MessagingOptOut> MessagingOptOuts => Set<MessagingOptOut>();
    public DbSet<ProductOptionGroup> ProductOptionGroups => Set<ProductOptionGroup>();
    public DbSet<ProductOption> ProductOptions => Set<ProductOption>();
    public DbSet<OrderItemOption> OrderItemOptions => Set<OrderItemOption>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<KitchenTicket> KitchenTickets => Set<KitchenTicket>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<DailySalesSnapshot> DailySalesSnapshots => Set<DailySalesSnapshot>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();

    // Background queues: infrastructure only, read across tenants by the workers (not on IApplicationDbContext).
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OutboxMessageConsumer> OutboxMessageConsumers => Set<OutboxMessageConsumer>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore DomainEvent - EF discovers it via BaseEntity.DomainEvents navigation
        modelBuilder.Ignore<DomainEvent>();

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // BaseEntity sets Id = Guid.NewGuid() in the constructor (client-generated keys).
        // Tell EF the key is never DB-generated so it treats entities added via navigation
        // collections as Added (not Unchanged/Modified based on the non-default key value).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.Id))
                    .ValueGeneratedNever();
            }
        }

        // Apply global tenant query filters to all ITenantScoped entities
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(ApplicationDbContext)
                    .GetMethod(nameof(ApplyTenantQueryFilter),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);

                method.Invoke(this, [modelBuilder]);
            }
        }

        // WhatsAppMessage is tenant-scoped through its parent conversation.
        // Add an equivalent filter to avoid required-navigation/filter mismatch warnings.
        modelBuilder.Entity<WhatsAppMessage>()
            .HasQueryFilter(m => m.Conversation.TenantId == CurrentTenantId);
    }

    private void ApplyTenantQueryFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }
}
