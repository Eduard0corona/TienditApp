using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Taking and releasing a piece of the service board write the order row, so a change of the piece saved meanwhile hits
/// the order's row version: the use case decides again with what was saved, and gives up after three conflicts. The
/// in-memory provider has no row version: the conflict is thrown by hand, and only when the save writes the order row,
/// as SQL Server would (<c>ServiceBoardAssignmentSqlServerTests</c> proves it there).
/// </summary>
public class ServiceBoardPieceConflictTests
{
    private const string SavedMeanwhileMessage = "La orden fue actualizada por otro proceso. Refresca el tablero e intenta nuevamente.";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Release_OfAPieceCompletedMeanwhile_ShouldBeRefused_KeepingWhoWorkedIt()
    {
        var tenant = new TestCurrentTenantService();
        var worker = new TestCurrentUserService(nameof(TenantUserRole.Staff));
        var owner = new TestCurrentUserService(nameof(TenantUserRole.Owner));
        var options = NewOptions();
        await using var context = new TestApplicationDbContext(options, tenant);
        var pieceId = await SeedPieceAsync(context, tenant, ServiceOrderPieceStatus.InProgress, worker.Id);
        // The worker completes the piece after the owner's release read it in progress, and saves first.
        context.OrderChangedMeanwhile(conflicts: 1, change: () => ChangePieceAsync(options, tenant, pieceId,
            (order, piece) => order.ChangeServicePieceStatus(piece, ServiceOrderPieceStatus.Completed, Now.AddMinutes(1))));
        var board = new RecordingBoard();

        var result = await Release(context, owner, tenant, board, pieceId);

        Assert.Equal("No se puede liberar una pieza completada o cancelada.", result.Error);
        Assert.Equal(1, context.Conflicts);
        var stored = await StoredPieceAsync(options, tenant, pieceId);
        Assert.Equal((ServiceOrderPieceStatus.Completed, worker.Id), (stored.Status, stored.AssignedToUserId));
        Assert.Empty(board.PieceUpdates);
    }

    [Fact]
    public async Task Assign_OfAPieceTakenMeanwhile_ShouldBeRefused_LeavingItToWhoTookItFirst()
    {
        var tenant = new TestCurrentTenantService();
        var ana = new TestCurrentUserService(nameof(TenantUserRole.Staff));
        var luis = new TestCurrentUserService(nameof(TenantUserRole.Staff));
        var options = NewOptions();
        await using var context = new TestApplicationDbContext(options, tenant);
        var pieceId = await SeedPieceAsync(context, tenant, ServiceOrderPieceStatus.Accepted, assignedTo: null);
        // Luis takes the piece after Ana's request read it free, and saves first.
        context.OrderChangedMeanwhile(conflicts: 1, change: () => ChangePieceAsync(options, tenant, pieceId,
            (order, piece) => order.AssignServicePiece(piece, luis.Id, "Luis", Now.AddMinutes(1))));
        var board = new RecordingBoard();

        var result = await Assign(context, ana, tenant, board, pieceId);

        Assert.Equal("La pieza ya esta asignada a otro empleado.", result.Error);
        Assert.Equal(1, context.Conflicts);
        Assert.Equal(luis.Id, (await StoredPieceAsync(options, tenant, pieceId)).AssignedToUserId);
        Assert.Empty(board.PieceUpdates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssignAndRelease_ShouldGiveUp_AfterThreeConflicts_WithoutSavingOrTellingTheBoard(bool assign)
    {
        var tenant = new TestCurrentTenantService();
        var worker = new TestCurrentUserService(nameof(TenantUserRole.Staff));
        var options = NewOptions();
        await using var context = new TestApplicationDbContext(options, tenant);
        var pieceId = await SeedPieceAsync(context, tenant, ServiceOrderPieceStatus.InProgress, assign ? null : worker.Id);
        context.OrderChangedMeanwhile(conflicts: int.MaxValue);
        var board = new RecordingBoard();

        var result = assign
            ? await Assign(context, worker, tenant, board, pieceId)
            : await Release(context, worker, tenant, board, pieceId);

        Assert.Equal(SavedMeanwhileMessage, result.Error);
        Assert.Equal(3, context.Conflicts);
        Assert.Equal(assign ? null : worker.Id, (await StoredPieceAsync(options, tenant, pieceId)).AssignedToUserId);
        Assert.Empty(board.PieceUpdates);
    }

    private static Task<Result> Assign(
        ApplicationDbContext context, TestCurrentUserService user, ICurrentTenantService tenant, RecordingBoard board, Guid pieceId)
        => new AssignServiceOrderPieceToMeCommandHandler(context, user, TestAccess.For(user, tenant), board)
            .Handle(new AssignServiceOrderPieceToMeCommand { PieceId = pieceId }, CancellationToken.None);

    private static Task<Result> Release(
        ApplicationDbContext context, TestCurrentUserService user, ICurrentTenantService tenant, RecordingBoard board, Guid pieceId)
        => new ReleaseServiceOrderPieceAssignmentCommandHandler(context, user, TestAccess.For(user, tenant), board)
            .Handle(new ReleaseServiceOrderPieceAssignmentCommand { PieceId = pieceId }, CancellationToken.None);

    private static DbContextOptions<ApplicationDbContext> NewOptions()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    /// <summary>Someone else's request: loads the piece with its order in its own context, changes it and saves.</summary>
    private static async Task ChangePieceAsync(
        DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenant, Guid pieceId, Action<Order, ServiceOrderPiece> change)
    {
        await using var other = new TestApplicationDbContext(options, tenant);
        var piece = await other.ServiceOrderPieces.Include(p => p.Order).SingleAsync(p => p.Id == pieceId);
        change(piece.Order, piece);
        await other.SaveChangesAsync();
    }

    private static async Task<ServiceOrderPiece> StoredPieceAsync(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenant, Guid pieceId)
    {
        await using var check = new TestApplicationDbContext(options, tenant);
        return await check.ServiceOrderPieces.AsNoTracking().SingleAsync(p => p.Id == pieceId);
    }

    private static async Task<Guid> SeedPieceAsync(
        ApplicationDbContext context, ICurrentTenantService tenant, ServiceOrderPieceStatus status, Guid? assignedTo)
    {
        var order = new Order(
            tenantId: tenant.TenantId,
            customerUserId: Guid.NewGuid(),
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Service,
            orderType: OrderType.Service,
            customerName: "Cliente Test",
            customerPhone: "+525511112222",
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Limpieza de tenis", 1, 250m);
        order.AddServicePiece("Par Nike", null, null, null, null, null, null, "Lavado premium", null);

        var piece = order.ServicePieces.Single();
        if (assignedTo is { } userId)
            order.AssignServicePiece(piece, userId, "Técnico", Now);
        if (status == ServiceOrderPieceStatus.InProgress)
            order.ChangeServicePieceStatus(piece, ServiceOrderPieceStatus.InProgress, Now);

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return piece.Id;
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    /// <summary>A member of the business with a fixed id, so the handler can tell whether they are the assignee.</summary>
    private sealed class TestCurrentUserService(string tenantRole) : ICurrentUserService
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string? UserId => Id.ToString();
        public string? UserName => "tester";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => tenantRole;
        public IReadOnlyCollection<string> Permissions => [nameof(TenantPermission.ManageServiceBoard)];
        public Guid? TenantId => null;
    }

    /// <summary>What the live service board heard of each piece.</summary>
    private sealed class RecordingBoard : IOrderNotificationService
    {
        public List<(Guid PieceId, Guid? AssignedToUserId)> PieceUpdates { get; } = [];

        public Task NotifyServiceBoardPieceUpdated(Guid tenantId, Guid orderId, Guid pieceId, ServiceOrderPieceStatus status, Guid? assignedToUserId)
        {
            PieceUpdates.Add((pieceId, assignedToUserId));
            return Task.CompletedTask;
        }

        public Task NotifyOrderCreated(Guid tenantId, Guid orderId, string orderNumber, BusinessType businessType, decimal total,
            OrderStatus status, OrderChannel channel, string? externalOrderNumber) => throw new NotSupportedException();
        public Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus) => throw new NotSupportedException();
        public Task NotifyOrderCancelled(Guid tenantId, Guid orderId, OrderStatus previousStatus, string? reason, string orderNumber, bool byCustomer)
            => throw new NotSupportedException();
        public Task NotifyKitchenTicketsChanged(Guid tenantId, Guid orderId, int round) => throw new NotSupportedException();
        public Task NotifyOrderUpdated(Guid tenantId, Guid orderId) => throw new NotSupportedException();
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<object?>();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => Task.FromResult<object?>(null);
    }

    private sealed class TestApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenantService)
        : ApplicationDbContext(options, tenantService, new NullMediator())
    {
        private int _conflictsLeft;
        private Func<Task>? _changeMeanwhile;

        /// <summary>Saves refused because the order changed meanwhile.</summary>
        public int Conflicts { get; private set; }

        /// <summary>
        /// Refuses the next <paramref name="conflicts"/> saves that write the order row, as its row version would after
        /// <paramref name="change"/>, saved by someone else right before the first one. A save that does not write the
        /// order row is not refused.
        /// </summary>
        public void OrderChangedMeanwhile(int conflicts, Func<Task>? change = null)
            => (_conflictsLeft, _changeMeanwhile) = (conflicts, change);

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_conflictsLeft > 0 && ChangeTracker.Entries<Order>().Any(entry => entry.State == EntityState.Modified))
            {
                _conflictsLeft--;
                Conflicts++;
                if (_changeMeanwhile is { } change)
                {
                    _changeMeanwhile = null;
                    await change();
                }

                throw new DbUpdateConcurrencyException("La orden cambió mientras tanto.");
            }

            return await base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // The in-memory provider does not generate SQL Server row versions.
            modelBuilder.Entity<Order>()
                .Property(o => o.Version)
                .IsRequired(false)
                .ValueGeneratedNever();
        }
    }
}
