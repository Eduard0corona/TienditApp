using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// A sale is one transaction (<see cref="TransactionBehavior{TRequest,TResponse}"/>) in SQL Server set up as in
/// production (retries, MARS): it takes the next folio and the screens hear of it once committed, a refused sale gives
/// its folio back, and when the same offline sale arrives twice at once the late copy is undone whole. An order of a
/// delivery platform is stored with its detail and its «Plataforma» payment in the same transaction.
/// </summary>
public class SaleTransactionSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 17, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Sale_ShouldTakeTheNextFolio_AndBeAnnouncedOnceCommitted()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);

        var sale = await shop.SellAsync(Sale(shop.ProductId));

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal("ORD-000002", sale.Value.OrderNumber);
        var announcement = Assert.Single(shop.Mediator.Published, p => p.Notification is OrderCreatedEvent);
        Assert.False(announcement.InsideTransaction);
        Assert.Equal(2, await shop.LastOrderFolioAsync());
    }

    [SqlServerFact]
    public async Task RefusedSale_ShouldGiveItsFolioBack()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);

        // Refused after the folio was taken: the tip is checked against the total of the sale.
        var sale = await shop.SellAsync(Sale(shop.ProductId) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 23.20m, Method = OrderPaymentMethod.Cash, Tip = 50m, CashReceived = 100m }]
        });

        Assert.True(sale.IsFailure);
        Assert.Equal(Order.TipAboveTotalMessage, sale.Error);
        Assert.Equal(1, await shop.LastOrderFolioAsync());
        await using var check = database.CreateContext(shop.TenantId);
        Assert.Empty(await check.Orders.ToListAsync());
        Assert.Empty(await check.CashTransactions.ToListAsync());
        Assert.Empty(shop.Mediator.Published);
    }

    [SqlServerFact]
    public async Task SameOfflineSaleTwiceAtOnce_ShouldUndoTheLateCopyWhole_AndAnswerWithTheStoredOne()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var key = Guid.NewGuid();
        var storedFirst = new StoreConcurrentSale(database, key);
        var shop = await Shop.OpenAsync(database, storedFirst);

        var sale = await shop.SellAsync(Sale(shop.ProductId) with { IdempotencyKey = key });

        Assert.True(storedFirst.Fired);
        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal(storedFirst.OrderId, sale.Value.Id);
        // Nothing of the late copy remains: not its folio, payment, drawer movements nor outbox messages.
        Assert.Equal(1, await shop.LastOrderFolioAsync());
        await using var check = database.CreateContext(shop.TenantId);
        Assert.Equal(new[] { storedFirst.OrderId }, await check.Orders.Select(o => o.Id).ToListAsync());
        Assert.Empty(await check.OrderPayments.ToListAsync());
        Assert.Empty(await check.CashTransactions.ToListAsync());
        Assert.Empty(await check.OutboxMessages.ToListAsync());
        Assert.DoesNotContain(shop.Mediator.Published, p => p.Notification is OrderCreatedEvent);
    }

    [SqlServerFact]
    public async Task FolioCounterCreatedInsideASale_ShouldNotChangeHowItsErrorsAreHandled()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        await using var context = database.CreateContext(shop.TenantId);
        var folios = new FolioGenerator(context);
        await using var transaction = await context.Database.BeginTransactionAsync();

        // The first movement of a day creates its counter; a later statement of the same request then fails.
        var first = await folios.NextInventoryMovementNumberAsync(shop.TenantId, Now, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlAsync(
            $"INSERT INTO [FolioSequences] ([TenantId], [Scope], [LastNumber]) VALUES ({shop.TenantId}, {Folios.OrderScope}, 0)"));
        var second = await folios.NextInventoryMovementNumberAsync(shop.TenantId, Now, CancellationToken.None);
        await transaction.CommitAsync();

        // The failed statement alone was undone: the transaction went on and kept both folios.
        Assert.Equal("INV-20261005-0001", first);
        Assert.Equal("INV-20261005-0002", second);
        await using var check = database.CreateContext(shop.TenantId);
        Assert.Equal(2, await check.FolioSequences.Where(s => s.Scope == "INV-20261005").Select(s => s.LastNumber).SingleAsync());
    }

    [SqlServerFact]
    public async Task OrderOfAPlatform_ShouldBeStoredWithItsDetailAndPayment_InOneTransaction_OutsideTheDrawer()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);

        var sale = await shop.SellAsync(PlatformOrder(shop.ProductId, OrderChannel.Rappi, "#bc 953"));

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal("ORD-000002", sale.Value.OrderNumber);
        Assert.Equal("BC953", sale.Value.PlatformOrder!.ExternalOrderNumber);
        var announcement = Assert.Single(shop.Mediator.Published, p => p.Notification is OrderCreatedEvent);
        Assert.False(announcement.InsideTransaction);
        var created = (OrderCreatedEvent)announcement.Notification;
        Assert.Equal((OrderChannel.Rappi, "BC953", (PlatformCollectedBy?)PlatformCollectedBy.Platform),
            (created.Channel, created.ExternalOrderNumber, created.CollectedBy));

        await using var check = database.CreateContext(shop.TenantId);
        var order = await check.Orders.SingleAsync();
        var detail = await check.PlatformOrderDetails.SingleAsync();
        var payment = await check.OrderPayments.SingleAsync();
        Assert.Equal((order.Id, "BC953", Now), (detail.OrderId, detail.ExternalOrderNumber, detail.RegisteredAt));
        // $20 plus IVA: 30 % of $23.20, its IVA, 2.5 % of $20 and half of the $3.20 of IVA.
        Assert.Equal((23.20m, 6.96m, 1.11m, 0.50m, 1.60m, 13.03m),
            (detail.OrderTotal, detail.Commission, detail.CommissionTax, detail.IsrWithheld, detail.VatWithheld, detail.ExpectedSettlement));
        Assert.Equal((OrderPaymentMethod.Platform, 23.20m, Now, "BC953"), (payment.Method, payment.Amount, payment.PaidAt, payment.Reference));
        // The shop's drawer is open, and still nothing of it entered it.
        Assert.Empty(await check.CashTransactions.ToListAsync());
        Assert.Contains("BC953", Assert.Single(await check.OutboxMessages
            .Where(m => m.Type == typeof(OrderCreatedEvent).FullName)
            .Select(m => m.Payload)
            .ToListAsync()));
    }

    [SqlServerFact]
    public async Task RefusedOrderOfAPlatform_ShouldLeaveNoDetail_AndGiveItsFolioBack()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        await shop.CloseDrawerAsync();

        // The business collects it and charges the cash now, with no drawer open: refused after the folio was taken
        // and the platform's amounts were calculated.
        var sale = await shop.SellAsync(PlatformOrder(shop.ProductId, OrderChannel.DidiFood, "12", PlatformCollectedBy.Business) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 23.20m, Method = OrderPaymentMethod.Cash }]
        });

        Assert.True(sale.IsFailure);
        Assert.Equal(OrderPaymentRegistrar.NoOpenDrawerMessage, sale.Error);
        Assert.Equal(1, await shop.LastOrderFolioAsync());
        await using var check = database.CreateContext(shop.TenantId);
        Assert.Empty(await check.Orders.ToListAsync());
        Assert.Empty(await check.PlatformOrderDetails.ToListAsync());
        Assert.Empty(await check.OrderPayments.ToListAsync());
        Assert.Empty(await check.OutboxMessages.ToListAsync());
        Assert.Empty(shop.Mediator.Published);
    }

    [SqlServerFact]
    public async Task SameFolioWithAnotherKey_ShouldAnswerTheWarning_NeverAServerError()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();

        // Vender always sends a key: a repeated folio is no index violation for the key's catch to turn into a 500.
        var first = await shop.SellAsync(PlatformOrder(shop.ProductId, OrderChannel.UberEats, "BC953") with { IdempotencyKey = firstKey });
        var repeated = await shop.SellAsync(PlatformOrder(shop.ProductId, OrderChannel.UberEats, "bc953") with { IdempotencyKey = secondKey });
        var retried = await shop.SellAsync(PlatformOrder(shop.ProductId, OrderChannel.UberEats, "BC953") with { IdempotencyKey = firstKey });
        var confirmed = await shop.SellAsync(PlatformOrder(shop.ProductId, OrderChannel.UberEats, "bc953") with
        {
            IdempotencyKey = secondKey,
            ConfirmPossibleDuplicate = true
        });

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(PlatformOrderErrors.PossibleDuplicate, repeated.Error);
        Assert.Equal(first.Value.Id, retried.Value.Id);
        Assert.Equal("BC953", retried.Value.PlatformOrder!.ExternalOrderNumber);
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.Equal(["ORD-000002", "ORD-000003"], [first.Value.OrderNumber, confirmed.Value.OrderNumber]);
        Assert.Equal(3, await shop.LastOrderFolioAsync());
        await using var check = database.CreateContext(shop.TenantId);
        Assert.Equal(2, await check.PlatformOrderDetails.CountAsync(d => d.ExternalOrderNumber == "BC953"));
    }

    private static CreateOrderCommand Sale(Guid productId) => new()
    {
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = 1 }],
        Payments = [new CreateOrderPaymentDto { Amount = 23.20m, Method = OrderPaymentMethod.Cash }]
    };

    private static CreateOrderCommand PlatformOrder(
        Guid productId, OrderChannel channel, string folio, PlatformCollectedBy? collectedBy = null) => new()
    {
        Channel = channel,
        ExternalOrderNumber = folio,
        CollectedBy = collectedBy,
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = 1 }]
    };

    /// <summary>A business with a product, an open drawer and folio 1 already given.</summary>
    private sealed class Shop
    {
        private readonly SqlServerTestDatabase _database;
        private readonly IInterceptor[] _interceptors;

        private Shop(SqlServerTestDatabase database, Guid tenantId, Guid productId, IInterceptor[] interceptors)
        {
            _database = database;
            TenantId = tenantId;
            ProductId = productId;
            _interceptors = interceptors;
        }

        public Guid TenantId { get; }
        public Guid ProductId { get; }
        public RecordingMediator Mediator { get; } = new();

        public static async Task<Shop> OpenAsync(SqlServerTestDatabase database, params IInterceptor[] interceptors)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant("Tienda", slug, BusinessType.Retail, $"{slug}@demo.com");
            var settings = new TenantSettings(tenant.Id, TenantCapability.Retail);
            settings.UpdateSalesPolicy(pricesIncludeTax: false, defaultTaxRate: 16m, autoCloseCounterSales: true);
            var branch = new Branch(tenant.Id, "Matriz", $"MATRIZ-{slug}", isDefault: true);
            var category = new Category(tenant.Id, "General", null);
            var product = new Product(tenant.Id, category.Id, "Refresco", null, 20m, "MXN");

            await using var context = database.CreateContext(tenant.Id);
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(settings);
            context.Branches.Add(branch);
            context.InventoryLocations.Add(new InventoryLocation(tenant.Id, "Mostrador", $"MAIN-{slug}", isDefault: true, branchId: branch.Id));
            context.Categories.Add(category);
            context.Products.Add(product);
            context.CashSessions.Add(new CashSession(tenant.Id, Now.AddHours(-2), "cajero", 500m));
            context.FolioSequences.Add(new FolioSequence(tenant.Id, Folios.OrderScope, 1));
            // It also takes orders of the three delivery platforms, with a commission of 30 %.
            foreach (var platform in OrderChannelRules.DeliveryPlatforms)
                context.DeliveryPlatformSettings.Add(new DeliveryPlatformSetting(tenant.Id, platform, true, PlatformTerms.WithFiscalDefaults(30m)));
            await context.SaveChangesAsync();

            return new Shop(database, tenant.Id, product.Id, interceptors);
        }

        public async Task CloseDrawerAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            var drawer = await context.CashSessions.Include(s => s.Transactions).SingleAsync();
            drawer.Close(Now, "cajero", drawer.ExpectedBalance);
            await context.SaveChangesAsync();
        }

        /// <summary>Sends the sale through the transaction of the pipeline, with the services of production.</summary>
        public async Task<Result<OrderDto>> SellAsync(CreateOrderCommand command)
        {
            var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
            tenant.SetTenant(TenantId);
            var user = new Cashier();
            var clock = new Clock();
            await using var context = _database.CreateProductionLikeContext(
                tenant,
                Mediator,
                [
                    .. _interceptors,
                    new AuditableEntityInterceptor(user, clock),
                    new TenantSaveChangesInterceptor(tenant),
                    new OutboxSaveChangesInterceptor(user, clock, new OutboxSignal())
                ]);
            Mediator.Context = context;

            var folios = new FolioGenerator(context);
            var phones = new PhoneNormalizationService();
            var handler = new CreateOrderCommandHandler(
                context,
                tenant,
                user,
                new CustomerLookupService(context, phones, NullLogger<CustomerLookupService>.Instance),
                clock,
                phones,
                orderInventoryService: new OrderInventoryService(context, clock, folios, NullLogger<OrderInventoryService>.Instance),
                orderCashService: new OrderCashService(context, user),
                folios: folios,
                creditLock: new CustomerCreditLock(context));

            return await new TransactionBehavior<CreateOrderCommand, Result<OrderDto>>(context)
                .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
        }

        public async Task<long> LastOrderFolioAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.FolioSequences
                .Where(s => s.TenantId == TenantId && s.Scope == Folios.OrderScope)
                .Select(s => s.LastNumber)
                .SingleAsync();
        }
    }

    /// <summary>
    /// Plays the other request of a sale sent twice: right before the late copy is saved, the first copy (folio 1, the
    /// same key) is stored and committed from another connection.
    /// </summary>
    private sealed class StoreConcurrentSale(SqlServerTestDatabase database, Guid key) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }
        public Guid OrderId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var lateCopy = eventData.Context!.ChangeTracker.Entries<Order>()
                .Any(e => e.State == EntityState.Added && e.Entity.ClientRequestId == key);
            if (Fired || !lateCopy)
                return result;

            Fired = true;
            var tenantId = eventData.Context.ChangeTracker.Entries<Order>().First().Entity.TenantId;
            var first = new Order(
                tenantId, null, null, true, Folios.OrderNumber(1), BusinessType.Retail, OrderType.Retail, "Público en general", null, null, null);
            first.IdentifyClientRequest(key, null);
            await using var other = database.CreateContext(tenantId);
            other.Orders.Add(first);
            await other.SaveChangesAsync(cancellationToken);
            OrderId = first.Id;
            return result;
        }
    }

    private sealed class RecordingMediator : IMediator
    {
        private readonly List<(object Notification, bool InsideTransaction)> _published = [];

        /// <summary>The context that publishes, to tell whether its transaction was still open.</summary>
        public ApplicationDbContext? Context { get; set; }

        public IReadOnlyList<(object Notification, bool InsideTransaction)> Published => _published;

        public Task Publish(object notification, CancellationToken ct = default) => Record(notification);
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
            => Record(notification!);
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();

        private Task Record(object notification)
        {
            _published.Add((notification, Context?.Database.CurrentTransaction is not null));
            return Task.CompletedTask;
        }
    }

    private sealed class Cashier : ICurrentUserService
    {
        public string? UserId => "4f6c2a0e-8d8b-4a8f-9a55-0c4b1f3f8a10";
        public string? UserName => "cajero";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
