using System.Data.Common;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The folio of what an order takes from the inventory or gives back, in the transaction of its command (Fase 31G), in SQL
/// Server set up as in production (retries, MARS): a command that fails after taking it gives it back, the first of the
/// day included, and a status change whose save meets the order changed by someone else runs again from an undone
/// transaction, with one movement and one folio. Before, these commands took the folio outside a transaction and every
/// failure or internal retry left a gap. Two requests moving the same order at once post its movement once.
/// </summary>
public class OrderStockFolioSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 17, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task StatusChangeFailingAfterItsFolio_ShouldGiveItBack_TheFirstOfTheDayIncluded()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, inventoryFolios: null);

        // The save fails after the consumption took the day's first folio, which also created the day's counter.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            shop.ChangeStatusAsync(OrderStatus.Confirmed, new FailingSave()));

        Assert.Null(await shop.InventoryFolioAsync());
        Assert.Empty(await shop.MovementNumbersAsync());
        Assert.Equal(10m, await shop.StockAsync());
        Assert.Equal(OrderStatus.Pending, await shop.OrderStatusAsync());
        Assert.Empty(await shop.OutboxTypesAsync());
        Assert.Empty(shop.Mediator.Published);

        var confirmed = await shop.ChangeStatusAsync(OrderStatus.Confirmed);

        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.Equal(new[] { Folios.DailyNumber(Folios.InventoryMovementScope(Now), 1) }, await shop.MovementNumbersAsync());
        Assert.Equal(1, await shop.InventoryFolioAsync());
    }

    [SqlServerFact]
    public async Task StatusChangeMeetingTheOrderChanged_ShouldRunAgain_WithOneMovementAndOneFolio()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, inventoryFolios: 4);
        var meanwhile = new OrderChangedMeanwhile(database, shop.OrderId, times: 1);

        var result = await shop.ChangeStatusAsync(OrderStatus.Confirmed, meanwhile);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, meanwhile.Changes);
        Assert.Equal(2, shop.HandlerRuns);
        // The first attempt's folio (5) went back with its transaction: the movement that stays has it, and nothing else.
        Assert.Equal(new[] { Folios.DailyNumber(Folios.InventoryMovementScope(Now), 5) }, await shop.MovementNumbersAsync());
        Assert.Equal(5, await shop.InventoryFolioAsync());
        Assert.Equal(8m, await shop.StockAsync());
        Assert.Equal(OrderStatus.Confirmed, await shop.OrderStatusAsync());
        Assert.Single(await shop.OutboxTypesAsync(), type => type.Contains(nameof(OrderStatusChangedEvent), StringComparison.Ordinal));
        var announced = Assert.Single(shop.Mediator.Published, p => p.Notification is OrderStatusChangedEvent);
        Assert.False(announced.InsideTransaction);
    }

    [SqlServerFact]
    public async Task StatusChangeMeetingTheOrderChangedEveryTime_ShouldAnswerItsMessage_AndLeaveNoFolio()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, inventoryFolios: 4);
        var meanwhile = new OrderChangedMeanwhile(database, shop.OrderId, times: int.MaxValue);

        var result = await shop.ChangeStatusAsync(OrderStatus.Confirmed, meanwhile);

        Assert.True(result.IsFailure);
        Assert.Equal(UpdateOrderStatusCommandHandler.UpdatedMeanwhileMessage, result.Error);
        Assert.Equal(TransactionBehavior<UpdateOrderStatusCommand, Result>.MaxConcurrencyAttempts, shop.HandlerRuns);
        Assert.Equal(4, await shop.InventoryFolioAsync());
        Assert.Empty(await shop.MovementNumbersAsync());
        Assert.Equal(10m, await shop.StockAsync());
        Assert.Equal(OrderStatus.Pending, await shop.OrderStatusAsync());
        Assert.Empty(shop.Mediator.Published);
    }

    [SqlServerFact]
    public async Task CancellationMeetingTheOrderChanged_ShouldUndoItsReturnAndItsFolio_WithoutRunningAgain()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, inventoryFolios: 4);
        Assert.True((await shop.ChangeStatusAsync(OrderStatus.Confirmed)).IsSuccess);
        shop.HandlerRuns = 0;

        // Cancelling gives the supplies back with a movement of its own (folio 6), and its save meets the order changed.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            shop.CancelAsync(new OrderChangedMeanwhile(database, shop.OrderId, times: 1)));

        Assert.Equal(1, shop.HandlerRuns);
        Assert.Equal(5, await shop.InventoryFolioAsync());
        Assert.Equal(new[] { Folios.DailyNumber(Folios.InventoryMovementScope(Now), 5) }, await shop.MovementNumbersAsync());
        Assert.Equal(8m, await shop.StockAsync());
        Assert.Equal(OrderStatus.Confirmed, await shop.OrderStatusAsync());
    }

    [SqlServerFact]
    public async Task TwoConfirmingTheSameOrderAtOnce_ShouldPostItsConsumptionOnce()
    {
        // Read committed snapshot on, as in Azure SQL Database: a read does not wait for what another request has not
        // committed yet, so only waiting at the counter tells the second one that the first is posting.
        await using var database = await SqlServerTestDatabase.CreateAsync(readCommittedSnapshot: true);
        var shop = await Shop.OpenAsync(database, inventoryFolios: 4);
        var other = shop.Twin();
        var meanwhile = new ConfirmedMeanwhile(database, other);

        var result = await shop.ChangeStatusAsync(OrderStatus.Confirmed, meanwhile);

        // The other employee posted the consumption (folio 5) and committed while this one waited at the counter. This
        // one found it there, met the order changed and ran again on what the other one left: nothing else to do.
        Assert.True(await meanwhile.WaitSeen, "Nadie esperó un candado mientras el otro empleado confirmaba.");
        Assert.True((await meanwhile.Confirming).IsSuccess);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, shop.HandlerRuns);
        Assert.Equal(new[] { Folios.DailyNumber(Folios.InventoryMovementScope(Now), 5) }, await shop.MovementNumbersAsync());
        Assert.Equal(5, await shop.InventoryFolioAsync());
        Assert.Equal(8m, await shop.StockAsync());
        Assert.Equal(OrderStatus.Confirmed, await shop.OrderStatusAsync());
        Assert.Equal(1, await shop.HistoryEntriesAsync(OrderStatus.Confirmed));
        Assert.Single(await shop.OutboxTypesAsync(), type => type.Contains(nameof(OrderStatusChangedEvent), StringComparison.Ordinal));
        Assert.Empty(shop.Mediator.Published);
        Assert.Single(other.Mediator.Published, p => p.Notification is OrderStatusChangedEvent);
    }

    [SqlServerFact]
    public async Task PieceCompletedMeetingTheOrderChanged_ShouldRunAgain_WithOneMovementAndOneFolio()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenServiceAsync(database, inventoryFolios: 4);
        var meanwhile = new OrderChangedMeanwhile(database, shop.OrderId, times: 1);

        var result = await shop.CompletePieceAsync(meanwhile);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, meanwhile.Changes);
        // Run again from an undone transaction, not inside it: with MARS, SQL Server keeps no savepoint, so what the first
        // attempt wrote before its save met the change (the movement, the stock, the order's history) would have stayed.
        Assert.Equal(2, shop.HandlerRuns);
        Assert.Equal(new[] { Folios.DailyNumber(Folios.InventoryMovementScope(Now), 5) }, await shop.MovementNumbersAsync());
        Assert.Equal(5, await shop.InventoryFolioAsync());
        Assert.Equal(9m, await shop.StockAsync());
        Assert.Equal(ServiceOrderPieceStatus.Completed, await shop.PieceStatusAsync());
        Assert.Equal(OrderStatus.Completed, await shop.OrderStatusAsync());
        Assert.Equal(1, await shop.HistoryEntriesAsync(OrderStatus.Completed));
    }

    [SqlServerFact]
    public async Task CustomerCancellingMeetingTheOrderChanged_ShouldRunAgain_AndCancelItOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenOnlineAsync(database);
        var meanwhile = new OrderChangedMeanwhile(database, shop.OrderId, times: 1);

        var result = await shop.CancelByLinkAsync(meanwhile);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, meanwhile.Changes);
        Assert.Equal(2, shop.HandlerRuns);
        Assert.Equal(OrderStatus.Cancelled, await shop.OrderStatusAsync());
        // What the first attempt wrote before its save met the change went back with its transaction: retried inside it,
        // with MARS and no savepoint, its entry in the history would have stayed next to the second one.
        Assert.Equal(1, await shop.HistoryEntriesAsync(OrderStatus.Cancelled));
        var outbox = await shop.OutboxTypesAsync();
        Assert.NotEmpty(outbox);
        Assert.Equal(outbox.Distinct().Count(), outbox.Count);
        Assert.Empty(await shop.MovementNumbersAsync());
        Assert.Equal(4, await shop.InventoryFolioAsync());
    }

    /// <summary>A business with a supply in stock (10), a product made of one, and an order of two waiting to be confirmed.</summary>
    private sealed class Shop
    {
        private readonly SqlServerTestDatabase _database;

        private Shop(SqlServerTestDatabase database, Guid tenantId, Guid orderId, Guid supplyId, Guid? pieceId, string? token)
        {
            _database = database;
            TenantId = tenantId;
            OrderId = orderId;
            SupplyId = supplyId;
            PieceId = pieceId;
            Token = token;
        }

        public Guid TenantId { get; }
        public Guid OrderId { get; }
        public Guid SupplyId { get; }
        public Guid? PieceId { get; }
        public string? Token { get; }
        public RecordingMediator Mediator { get; } = new();

        /// <summary>How many times a handler ran, its retries included.</summary>
        public int HandlerRuns { get; set; }

        /// <param name="inventoryFolios">The last folio of the day's inventory movements; null when none was taken yet.</param>
        public static Task<Shop> OpenAsync(SqlServerTestDatabase database, long? inventoryFolios)
            => SeedAsync(database, inventoryFolios, BusinessType.Retail, TenantCapability.Retail, (_, tenant, product) =>
            {
                var order = new Order(
                    tenant.Id, null, null, true, Folios.OrderNumber(1), BusinessType.Retail, OrderType.Retail, "Público en general", null, null, null);
                order.AddItem(product.Id, product.Name, 2, 30m);
                return order;
            });

        /// <summary>A repair shop instead: the order has a piece in progress whose service takes one unit of the supply.</summary>
        public static Task<Shop> OpenServiceAsync(SqlServerTestDatabase database, long? inventoryFolios)
            => SeedAsync(database, inventoryFolios, BusinessType.Service, TenantCapability.Service, (_, tenant, product) =>
            {
                var order = new Order(
                    tenant.Id, null, null, true, Folios.OrderNumber(1), BusinessType.Service, OrderType.Service, "Cliente", null, null, null);
                order.AddItem(product.Id, product.Name, 1, 30m);
                order.AddServicePiece("Par de tenis", null, null, null, null, null, null, null, null, product.Id);
                order.ChangeServicePieceStatus(order.ServicePieces.Single(), ServiceOrderPieceStatus.InProgress, Now);
                return order;
            });

        /// <summary>The order came from the online menu instead: not accepted yet, followed by its customer with its link.</summary>
        public static Task<Shop> OpenOnlineAsync(SqlServerTestDatabase database)
            => SeedAsync(database, inventoryFolios: 4, BusinessType.Retail, TenantCapability.Retail, (context, tenant, product) =>
            {
                var customer = new TemporaryCustomer(tenant.Id, "+525512345678", "Ana");
                context.TemporaryCustomers.Add(customer);
                var order = new Order(
                    tenant.Id, null, customer.Id, false, Folios.OrderNumber(1), BusinessType.Retail, OrderType.Retail, "Ana",
                    "+525512345678", null, null, channel: OrderChannel.Online) { CreatedAt = Now.AddMinutes(-5) };
                order.AddItem(product.Id, product.Name, 2, 30m);
                order.IssueTrackingToken(new string('a', Order.TrackingTokenLength));
                return order;
            });

        /// <summary>Another employee of the same business, with the same order in front of them.</summary>
        public Shop Twin() => new(_database, TenantId, OrderId, SupplyId, PieceId, Token);

        private static async Task<Shop> SeedAsync(
            SqlServerTestDatabase database,
            long? inventoryFolios,
            BusinessType businessType,
            TenantCapability capability,
            Func<ApplicationDbContext, Tenant, Product, Order> createOrder)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant("Cafetería", slug, businessType, $"{slug}@demo.com");
            var supply = new SupplyItem(tenant.Id, "Café en grano", UnitType.Unit);
            var location = new InventoryLocation(tenant.Id, "Almacén", $"MAIN-{slug}", isDefault: true);
            var stock = new InventoryStock(tenant.Id, supply.Id, location.Id);
            stock.ApplyMovement(10m, 4m, Now.AddDays(-1));
            var category = new Category(tenant.Id, "Bebidas", null);
            var product = new Product(tenant.Id, category.Id, "Americano", null, 30m, "MXN");

            await using var context = database.CreateContext(tenant.Id);
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(tenant.Id, capability));
            // The cashier takes the pieces they complete.
            context.Users.Add(new ApplicationUser
            {
                Id = Cashier.Id,
                UserName = $"cajero-{slug}@demo.com",
                NormalizedUserName = $"CAJERO-{slug}@DEMO.COM",
                Email = $"cajero-{slug}@demo.com",
                NormalizedEmail = $"CAJERO-{slug}@DEMO.COM",
                FirstName = "Cajero",
                LastName = "Demo",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Role = UserRole.Staff,
                IsActive = true
            });
            context.AddRange(supply, location, stock, category, product);
            context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.Id, product.Id, supply.Id, 1m));
            var order = createOrder(context, tenant, product);
            context.Orders.Add(order);
            if (inventoryFolios is { } last)
                context.FolioSequences.Add(new FolioSequence(tenant.Id, Folios.InventoryMovementScope(Now), last));
            await context.SaveChangesAsync();

            return new Shop(database, tenant.Id, order.Id, supply.Id, order.ServicePieces.SingleOrDefault()?.Id, order.TrackingToken);
        }

        /// <summary>Sends the status change through the transaction of the pipeline, with the services of production.</summary>
        public Task<Result> ChangeStatusAsync(OrderStatus status, params IInterceptor[] interceptors)
            => SendAsync<UpdateOrderStatusCommand, Result>(
                new UpdateOrderStatusCommand { OrderId = OrderId, NewStatus = status },
                s => new UpdateOrderStatusCommandHandler(s.Context, s.User, s.Inventory),
                interceptors);

        public Task<Result> CancelAsync(params IInterceptor[] interceptors)
            => SendAsync<CancelOrderCommand, Result>(
                new CancelOrderCommand { OrderId = OrderId, Reason = "Ya no lo quiso" },
                s => new CancelOrderCommandHandler(s.Context, s.User, s.Inventory),
                interceptors);

        public Task<Result> CompletePieceAsync(params IInterceptor[] interceptors)
            => SendAsync<UpdateServiceOrderPieceStatusCommand, Result>(
                new UpdateServiceOrderPieceStatusCommand { OrderId = OrderId, PieceId = PieceId!.Value, NewStatus = ServiceOrderPieceStatus.Completed },
                s => new UpdateServiceOrderPieceStatusCommandHandler(
                    s.Context, s.User, new CurrentAccessContext(s.User, s.Tenant, new Managers()), s.Inventory),
                interceptors);

        public Task<Result<PublicOrderTrackingDto>> CancelByLinkAsync(params IInterceptor[] interceptors)
            => SendAsync<CancelPublicOrderCommand, Result<PublicOrderTrackingDto>>(
                new CancelPublicOrderCommand(Token!),
                s => new CancelPublicOrderCommandHandler(s.Context, s.Tenant, s.Inventory, s.Clock),
                interceptors);

        public async Task<long?> InventoryFolioAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.FolioSequences
                .Where(s => s.TenantId == TenantId && s.Scope == Folios.InventoryMovementScope(Now))
                .Select(s => (long?)s.LastNumber)
                .SingleOrDefaultAsync();
        }

        public async Task<List<string>> MovementNumbersAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.InventoryMovements.OrderBy(m => m.MovementNumber).Select(m => m.MovementNumber).ToListAsync();
        }

        public async Task<decimal> StockAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.InventoryStocks.Where(s => s.SupplyItemId == SupplyId).Select(s => s.OnHandQuantity).SingleAsync();
        }

        public async Task<OrderStatus> OrderStatusAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.Orders.Where(o => o.Id == OrderId).Select(o => o.Status).SingleAsync();
        }

        public async Task<int> HistoryEntriesAsync(OrderStatus status)
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.OrderStatusHistories.CountAsync(h => h.OrderId == OrderId && h.NewStatus == status);
        }

        public async Task<ServiceOrderPieceStatus> PieceStatusAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.ServiceOrderPieces.Where(p => p.Id == PieceId).Select(p => p.Status).SingleAsync();
        }

        public async Task<List<string>> OutboxTypesAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.OutboxMessages.IgnoreQueryFilters().Where(m => m.TenantId == TenantId).Select(m => m.Type).ToListAsync();
        }

        private async Task<TResponse> SendAsync<TCommand, TResponse>(
            TCommand command,
            Func<Services, IRequestHandler<TCommand, TResponse>> createHandler,
            IInterceptor[] interceptors)
            where TCommand : IRequest<TResponse>, ITransactionalRequest
            where TResponse : Result
        {
            var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
            tenant.SetTenant(TenantId);
            var user = new Cashier();
            var clock = new Clock();
            await using var context = _database.CreateProductionLikeContext(
                tenant,
                Mediator,
                [
                    .. interceptors,
                    new AuditableEntityInterceptor(user, clock),
                    new TenantSaveChangesInterceptor(tenant),
                    new OutboxSaveChangesInterceptor(user, clock, new OutboxSignal())
                ]);
            Mediator.Context = context;
            var inventory = new OrderInventoryService(context, clock, new FolioGenerator(context), NullLogger<OrderInventoryService>.Instance);
            var handler = createHandler(new Services(context, tenant, user, inventory, clock));

            return await new TransactionBehavior<TCommand, TResponse>(context).Handle(command, () =>
            {
                HandlerRuns++;
                return handler.Handle(command, CancellationToken.None);
            }, CancellationToken.None);
        }

        private sealed record Services(
            ApplicationDbContext Context, ICurrentTenantService Tenant, ICurrentUserService User, IOrderInventoryService Inventory, IDateTimeProvider Clock);
    }

    /// <summary>The next save fails before it writes, after the command took its folio.</summary>
    private sealed class FailingSave : SaveChangesInterceptor
    {
        private bool _failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_failed)
                return ValueTask.FromResult(result);
            _failed = true;
            throw new InvalidOperationException("La base no respondió.");
        }
    }

    /// <summary>
    /// Someone else changes the order (its row version) from another connection right before the command saves, the first
    /// <c>times</c> saves: the command's update of the order then finds it changed.
    /// </summary>
    private sealed class OrderChangedMeanwhile(SqlServerTestDatabase database, Guid orderId, int times) : SaveChangesInterceptor
    {
        public int Changes { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Changes >= times)
                return result;
            Changes++;
            await using var other = database.CreateContext();
            await other.Database.ExecuteSqlAsync(
                $"UPDATE [Orders] SET [CustomerNotes] = N'Cambió en otro dispositivo' WHERE [Id] = {orderId};", cancellationToken);
            return result;
        }
    }

    /// <summary>
    /// Another employee confirms the same order right after this request looked for its consumption and did not find it:
    /// the other one takes its folio and posts it, and commits only once a request of this database waits for a lock, which
    /// is this one at the counter of the folios.
    /// </summary>
    private sealed class ConfirmedMeanwhile(SqlServerTestDatabase database, Shop other) : DbCommandInterceptor
    {
        private Task<Result>? _confirming;
        private Task<bool>? _waitSeen;

        /// <summary>The other employee's confirmation, once it ran.</summary>
        public Task<Result> Confirming => _confirming ?? Task.FromResult(Result.Failure("Nadie más confirmó la orden."));

        /// <summary>Whether a request of this database was seen waiting for a lock before the other one committed.</summary>
        public Task<bool> WaitSeen => _waitSeen ?? Task.FromResult(false);

        public override async ValueTask<InterceptionResult> DataReaderClosingAsync(
            DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
        {
            if (_confirming is not null || !command.CommandText.Contains("[InventoryMovements]", StringComparison.Ordinal))
                return result;

            var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirming = Task.Run(() => other.ChangeStatusAsync(OrderStatus.Confirmed, new HoldsItsCommit(posted, commit)));
            try
            {
                await Task.WhenAny(posted.Task, _confirming).WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch
            {
                commit.TrySetResult();
                throw;
            }
            _waitSeen = CommitWhenSomeoneWaitsAsync(commit);
            return result;
        }

        /// <summary>
        /// Lets the other one commit once a request of this database waits for a lock (this one, at the counter of the
        /// folios), or after 15 s. Reading <c>sys.dm_exec_requests</c> of other sessions takes VIEW SERVER STATE, which the
        /// login of these tests has; without it no wait would be seen and the test says so.
        /// </summary>
        private async Task<bool> CommitWhenSomeoneWaitsAsync(TaskCompletionSource commit)
        {
            try
            {
                await using var connection = new SqlConnection(database.ConnectionString);
                await connection.OpenAsync();
                for (var look = 0; look < 300; look++)
                {
                    await using var waiting = connection.CreateCommand();
                    waiting.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0 AND database_id = DB_ID();";
                    if ((int)(await waiting.ExecuteScalarAsync())! > 0)
                        return true;
                    await Task.Delay(50);
                }
                return false;
            }
            finally
            {
                commit.TrySetResult();
            }
        }
    }

    /// <summary>Its save is done, inside its transaction; it commits when it is told to.</summary>
    private sealed class HoldsItsCommit(TaskCompletionSource posted, TaskCompletionSource commit) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            posted.TrySetResult();
            await commit.Task;
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
        public static readonly Guid Id = Guid.Parse("4f6c2a0e-8d8b-4a8f-9a55-0c4b1f3f8a10");
        public string? UserId => Id.ToString();
        public string? UserName => "cajero";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    /// <summary>The cashier manages the business.</summary>
    private sealed class Managers : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Service, [], IsManager: true));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>(Enum.GetNames<TenantPermission>());

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
