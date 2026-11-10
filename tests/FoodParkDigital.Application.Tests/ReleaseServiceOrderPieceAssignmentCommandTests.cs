using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// «Liberar» on the service board (<c>PUT /service-board/pieces/{id}/release</c>): an open piece goes back to nobody;
/// a completed or cancelled one keeps who worked it, for its assignee and for a manager alike. The board stops offering
/// it on closed pieces (Fase 30M), but the server is the one that decides. The release goes through the order, whose row
/// version catches a change saved meanwhile (proven on SQL Server by <c>ServiceOrderPieceAssignmentSqlServerTests</c>):
/// here, that the use case tries again and then says so.
/// </summary>
public class ReleaseServiceOrderPieceAssignmentCommandTests
{
    private const string ClosedPieceMessage = "No se puede liberar una pieza completada o cancelada.";
    private static readonly DateTimeOffset AssignedAt = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Release_ShouldLeaveAnOpenPieceWithNobody_AndTellTheBoard()
    {
        var tenant = new TestCurrentTenantService();
        var worker = new TestCurrentUserService(nameof(TenantUserRole.Staff));
        await using var context = NewContext(tenant);
        var pieceId = await SeedAssignedPieceAsync(context, tenant, worker.Id, ServiceOrderPieceStatus.InProgress);
        var board = new RecordingBoard();

        var result = await NewHandler(context, worker, tenant, board)
            .Handle(new ReleaseServiceOrderPieceAssignmentCommand { PieceId = pieceId }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await context.ServiceOrderPieces.AsNoTracking().SingleAsync(p => p.Id == pieceId);
        Assert.Equal(ServiceOrderPieceStatus.InProgress, stored.Status);
        Assert.Null(stored.AssignedToUserId);
        Assert.Null(stored.AssignedToName);
        Assert.Null(stored.AssignedAt);
        // The order row was written with the piece: that write is what checks its row version.
        Assert.NotEqual(AssignedAt, (await context.Orders.AsNoTracking().SingleAsync()).UpdatedAt);
        var update = Assert.Single(board.PieceUpdates);
        Assert.Equal(pieceId, update.PieceId);
        Assert.Null(update.AssignedToUserId);
    }

    [Theory]
    [InlineData(ServiceOrderPieceStatus.Completed, false)]
    [InlineData(ServiceOrderPieceStatus.Completed, true)]
    [InlineData(ServiceOrderPieceStatus.Cancelled, false)]
    [InlineData(ServiceOrderPieceStatus.Cancelled, true)]
    public async Task Release_ShouldBeRejected_WhenThePieceIsClosed_KeepingWhoWorkedIt(
        ServiceOrderPieceStatus status,
        bool byManager)
    {
        var tenant = new TestCurrentTenantService();
        var worker = new TestCurrentUserService(nameof(TenantUserRole.Staff));
        var manager = new TestCurrentUserService(nameof(TenantUserRole.Owner));
        var caller = byManager ? manager : worker;
        await using var context = NewContext(tenant);
        var pieceId = await SeedAssignedPieceAsync(context, tenant, worker.Id, status);
        var board = new RecordingBoard();

        var result = await NewHandler(context, caller, tenant, board)
            .Handle(new ReleaseServiceOrderPieceAssignmentCommand { PieceId = pieceId }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClosedPieceMessage, result.Error);
        var stored = await context.ServiceOrderPieces.AsNoTracking().SingleAsync(p => p.Id == pieceId);
        Assert.Equal(status, stored.Status);
        Assert.Equal(worker.Id, stored.AssignedToUserId);
        Assert.Equal("Técnico", stored.AssignedToName);
        Assert.Equal(AssignedAt, stored.AssignedAt);
        Assert.Empty(board.PieceUpdates);
    }

    [Fact]
    public async Task Release_ShouldStopAfterThreeTries_WhenTheOrderChangesEveryTime_KeepingTheAssignment()
    {
        var tenant = new TestCurrentTenantService();
        var worker = new TestCurrentUserService(nameof(TenantUserRole.Staff));
        var database = Guid.NewGuid().ToString();
        Guid pieceId;
        await using (var seed = NewContext(tenant, database))
            pieceId = await SeedAssignedPieceAsync(seed, tenant, worker.Id, ServiceOrderPieceStatus.InProgress);
        var meanwhile = new AlwaysSavedMeanwhile();
        await using var context = NewContext(tenant, database, meanwhile);
        var board = new RecordingBoard();

        var result = await NewHandler(context, worker, tenant, board)
            .Handle(new ReleaseServiceOrderPieceAssignmentCommand { PieceId = pieceId }, CancellationToken.None);

        Assert.Equal(ServiceBoardPieceSaves.SavedMeanwhileMessage, result.Error);
        Assert.Equal(3, meanwhile.Attempts);
        var stored = await context.ServiceOrderPieces.AsNoTracking().SingleAsync(p => p.Id == pieceId);
        Assert.Equal(worker.Id, stored.AssignedToUserId);
        Assert.Equal("Técnico", stored.AssignedToName);
        Assert.Equal(AssignedAt, stored.AssignedAt);
        Assert.Empty(board.PieceUpdates);
    }

    private static ReleaseServiceOrderPieceAssignmentCommandHandler NewHandler(
        ApplicationDbContext context,
        ICurrentUserService user,
        ICurrentTenantService tenant,
        IOrderNotificationService board)
        => new(context, user, TestAccess.For(user, tenant), board);

    private static TestApplicationDbContext NewContext(ICurrentTenantService tenant)
        => NewContext(tenant, Guid.NewGuid().ToString());

    private static TestApplicationDbContext NewContext(ICurrentTenantService tenant, string database, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(database)
            .AddInterceptors(interceptors)
            .Options;

        return new TestApplicationDbContext(options, tenant, new NullMediator());
    }

    /// <summary>A piece of a service order, taken by the worker while open and then moved to the status.</summary>
    private static async Task<Guid> SeedAssignedPieceAsync(
        ApplicationDbContext context,
        ICurrentTenantService tenant,
        Guid workerId,
        ServiceOrderPieceStatus status)
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
        order.AssignServicePiece(piece, workerId, "Técnico", AssignedAt);

        ServiceOrderPieceStatus[] path = status switch
        {
            ServiceOrderPieceStatus.InProgress => [ServiceOrderPieceStatus.InProgress],
            ServiceOrderPieceStatus.Completed => [ServiceOrderPieceStatus.InProgress, ServiceOrderPieceStatus.Completed],
            ServiceOrderPieceStatus.Cancelled => [ServiceOrderPieceStatus.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
        foreach (var next in path)
            order.ChangeServicePieceStatus(piece, next, AssignedAt);

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return piece.Id;
    }

    /// <summary>Another change of the order saved before each save of this context: its row version never matches.</summary>
    private sealed class AlwaysSavedMeanwhile : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new DbUpdateConcurrencyException("La orden cambió.");
        }
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
        public List<(Guid PieceId, ServiceOrderPieceStatus Status, Guid? AssignedToUserId)> PieceUpdates { get; } = [];

        public Task NotifyServiceBoardPieceUpdated(Guid tenantId, Guid orderId, Guid pieceId, ServiceOrderPieceStatus status, Guid? assignedToUserId)
        {
            PieceUpdates.Add((pieceId, status, assignedToUserId));
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

    private sealed class TestApplicationDbContext : ApplicationDbContext
    {
        public TestApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentTenantService tenantService,
            IMediator mediator)
            : base(options, tenantService, mediator)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Order>()
                .Property(o => o.Version)
                .IsRequired(false)
                .ValueGeneratedNever();
        }
    }
}
