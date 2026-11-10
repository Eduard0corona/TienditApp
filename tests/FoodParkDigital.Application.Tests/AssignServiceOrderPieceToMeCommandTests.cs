using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// «Tomar servicio» on the service board (<c>PUT /service-board/pieces/{id}/assign-to-me</c>): the piece is taken through its
/// order, which marks the order row so a change saved meanwhile is caught by its row version (proven on SQL Server by
/// <c>ServiceOrderPieceAssignmentSqlServerTests</c>); here, that the order is written and that the use case tries again
/// and then says so.
/// </summary>
public class AssignServiceOrderPieceToMeCommandTests
{
    [Fact]
    public async Task Assign_ShouldTakeAnOpenPiece_ThroughItsOrder_AndTellTheBoard()
    {
        var tenant = NewTenant();
        var worker = NewWorker();
        var database = Guid.NewGuid().ToString();
        var pieceId = await SeedOpenPieceAsync(tenant, database);
        await using var context = NewContext(tenant, database);
        var board = new RecordingBoard();

        var result = await NewHandler(context, worker, tenant, board)
            .Handle(new AssignServiceOrderPieceToMeCommand { PieceId = pieceId }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await context.ServiceOrderPieces.AsNoTracking().SingleAsync(p => p.Id == pieceId);
        Assert.Equal(Guid.Parse(worker.UserId!), stored.AssignedToUserId);
        Assert.Equal("Técnico", stored.AssignedToName);
        Assert.NotNull(stored.AssignedAt);
        // The order row was written with the piece: that write is what checks its row version.
        Assert.NotNull((await context.Orders.AsNoTracking().SingleAsync()).UpdatedAt);
        var update = Assert.Single(board.PieceUpdates);
        Assert.Equal((pieceId, stored.AssignedToUserId), update);
    }

    [Fact]
    public async Task Assign_ShouldStopAfterThreeTries_WhenTheOrderChangesEveryTime_WithoutTakingThePiece()
    {
        var tenant = NewTenant();
        var worker = NewWorker();
        var database = Guid.NewGuid().ToString();
        var pieceId = await SeedOpenPieceAsync(tenant, database);
        var meanwhile = new AlwaysSavedMeanwhile();
        await using var context = NewContext(tenant, database, meanwhile);
        var board = new RecordingBoard();

        var result = await NewHandler(context, worker, tenant, board)
            .Handle(new AssignServiceOrderPieceToMeCommand { PieceId = pieceId }, CancellationToken.None);

        Assert.Equal(ServiceBoardPieceSaves.SavedMeanwhileMessage, result.Error);
        Assert.Equal(3, meanwhile.Attempts);
        var stored = await context.ServiceOrderPieces.AsNoTracking().SingleAsync(p => p.Id == pieceId);
        Assert.Null(stored.AssignedToUserId);
        Assert.Empty(board.PieceUpdates);
    }

    private static AssignServiceOrderPieceToMeCommandHandler NewHandler(
        ApplicationDbContext context,
        ICurrentUserService user,
        ICurrentTenantService tenant,
        IOrderNotificationService board)
        => new(context, user, TestAccess.For(user, tenant), board);

    private static SalesTenantService NewTenant()
    {
        var tenant = new SalesTenantService();
        tenant.SetTenant(Guid.NewGuid());
        return tenant;
    }

    private static SalesCurrentUser NewWorker() => new()
    {
        UserName = "Técnico",
        TenantRole = nameof(TenantUserRole.Staff),
        Permissions = [nameof(TenantPermission.ManageServiceBoard)]
    };

    private static SalesDbContext NewContext(ICurrentTenantService tenant, string database, params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(database).AddInterceptors(interceptors).Options, tenant);

    /// <summary>A piece of a service order on the board that nobody has taken yet.</summary>
    private static async Task<Guid> SeedOpenPieceAsync(ICurrentTenantService tenant, string database)
    {
        await using var context = NewContext(tenant, database);
        var order = new Order(tenant.TenantId, null, null, createdByBusiness: true, "S-000001", BusinessType.Service, OrderType.Service,
            "Valeria", "+525522107765", null, null);
        order.AddItem(Guid.NewGuid(), "Limpieza de tenis", 1, 250m);
        order.AddServicePiece("Par Nike", null, null, null, null, null, null, "Lavado premium", null);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.ServicePieces.Single().Id;
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
}
