using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Taking («Tomar servicio») and releasing («Liberar») a piece of the service board on SQL Server. The piece has no row
/// version of its own, so both go through its order (<see cref="Order.AssignServicePiece"/> and
/// <see cref="Order.ReleaseServicePiece"/>): a change saved by another request meanwhile is caught by the order's row
/// version, and the request is decided again with what the other one saved. A piece completed meanwhile keeps who worked
/// it, one cancelled meanwhile is not taken, one taken meanwhile stays with whoever has it, and one released while someone
/// completes it stays with whoever completed it.
/// </summary>
public class ServiceOrderPieceAssignmentSqlServerTests
{
    private const string ClosedToReleaseMessage = "No se puede liberar una pieza completada o cancelada.";
    private const string ClosedToAssignMessage = "No se puede asignar una pieza completada o cancelada.";
    private static readonly DateTimeOffset AssignedAt = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Release_OfAnOpenPiece_ShouldLeaveItWithNobody_AndTellTheBoard()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await SeedShopAsync(database);
        var pieceId = await SeedPieceAsync(database, shop, ServiceOrderPieceStatus.InProgress, assignedTo: shop.Ana);
        await using var context = database.CreateProductionLikeContext(Tenant(shop.TenantId), new InMemoryApplicationDbContext.NullMediator());
        var board = new RecordingBoard();

        var result = await ReleaseAsync(context, shop, shop.Ana, pieceId, board);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await LoadPieceAsync(database, shop.TenantId, pieceId);
        Assert.Equal(ServiceOrderPieceStatus.InProgress, stored.Status);
        Assert.Equal(Nobody, AssignmentOf(stored));
        var update = Assert.Single(board.PieceUpdates);
        Assert.Equal(pieceId, update.PieceId);
        Assert.Null(update.AssignedToUserId);
    }

    [SqlServerFact]
    public async Task Release_OfAPieceCompletedMeanwhile_ShouldKeepWhoWorkedIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await SeedShopAsync(database);
        var pieceId = await SeedPieceAsync(database, shop, ServiceOrderPieceStatus.InProgress, assignedTo: shop.Ana);
        await using var racing = Racing(database, shop.TenantId, pieceId, (order, piece) =>
            order.ChangeServicePieceStatus(piece, ServiceOrderPieceStatus.Completed, AssignedAt.AddHours(1)));
        var board = new RecordingBoard();

        var result = await ReleaseAsync(racing, shop, shop.Ana, pieceId, board);

        Assert.Equal(ClosedToReleaseMessage, result.Error);
        var stored = await LoadPieceAsync(database, shop.TenantId, pieceId);
        Assert.Equal(ServiceOrderPieceStatus.Completed, stored.Status);
        Assert.Equal(Assignment(shop.Ana, AssignedAt), AssignmentOf(stored));
        Assert.Empty(board.PieceUpdates);
    }

    [SqlServerFact]
    public async Task Release_OfAPieceGivenToSomeoneElseMeanwhile_ShouldLeaveItWithThem()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await SeedShopAsync(database);
        var pieceId = await SeedPieceAsync(database, shop, ServiceOrderPieceStatus.InProgress, assignedTo: shop.Ana);
        // A manager gives the piece to Luis while Ana, who had it, releases it.
        await using var racing = Racing(database, shop.TenantId, pieceId, (order, piece) =>
            order.AssignServicePiece(piece, shop.Luis.Id, "Luis", AssignedAt.AddHours(1)));
        var board = new RecordingBoard();

        var result = await ReleaseAsync(racing, shop, shop.Ana, pieceId, board);

        Assert.Equal("Solo el asignado o un administrador pueden liberar la pieza.", result.Error);
        var stored = await LoadPieceAsync(database, shop.TenantId, pieceId);
        Assert.Equal(Assignment(shop.Luis, AssignedAt.AddHours(1)), AssignmentOf(stored));
        Assert.Empty(board.PieceUpdates);
    }

    [SqlServerFact]
    public async Task Assign_OfAPieceCancelledMeanwhile_ShouldNotTakeIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await SeedShopAsync(database);
        var pieceId = await SeedPieceAsync(database, shop, ServiceOrderPieceStatus.Accepted, assignedTo: null);
        await using var racing = Racing(database, shop.TenantId, pieceId, (order, piece) =>
            order.ChangeServicePieceStatus(piece, ServiceOrderPieceStatus.Cancelled, AssignedAt.AddHours(1)));
        var board = new RecordingBoard();

        var result = await AssignAsync(racing, shop, shop.Ana, pieceId, board);

        Assert.Equal(ClosedToAssignMessage, result.Error);
        var stored = await LoadPieceAsync(database, shop.TenantId, pieceId);
        Assert.Equal(ServiceOrderPieceStatus.Cancelled, stored.Status);
        Assert.Equal(Nobody, AssignmentOf(stored));
        Assert.Empty(board.PieceUpdates);
    }

    [SqlServerFact]
    public async Task Assign_OfAPieceTakenMeanwhile_ShouldLeaveItWithWhoTookItFirst()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await SeedShopAsync(database);
        var pieceId = await SeedPieceAsync(database, shop, ServiceOrderPieceStatus.Accepted, assignedTo: null);
        await using var racing = Racing(database, shop.TenantId, pieceId, (order, piece) =>
            order.AssignServicePiece(piece, shop.Luis.Id, "Luis", AssignedAt.AddHours(1)));
        var board = new RecordingBoard();

        var result = await AssignAsync(racing, shop, shop.Ana, pieceId, board);

        Assert.Equal("La pieza ya esta asignada a otro empleado.", result.Error);
        var stored = await LoadPieceAsync(database, shop.TenantId, pieceId);
        Assert.Equal(Assignment(shop.Luis, AssignedAt.AddHours(1)), AssignmentOf(stored));
        Assert.Empty(board.PieceUpdates);
    }

    [SqlServerFact]
    public async Task Completing_APieceReleasedMeanwhile_ShouldLeaveItWithWhoCompletesIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await SeedShopAsync(database);
        var pieceId = await SeedPieceAsync(database, shop, ServiceOrderPieceStatus.InProgress, assignedTo: shop.Ana);
        var orderId = (await LoadPieceAsync(database, shop.TenantId, pieceId)).OrderId;
        // Ana releases the piece with the use case while the owner completes it, and saves first. The release writes the
        // order row, so the completion is decided again with the piece free and the owner takes it as she completes it,
        // instead of leaving a completed piece with nobody.
        await using var racing = database.CreateProductionLikeContext(Tenant(shop.TenantId), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(shop.TenantId);
                var released = await ReleaseAsync(other, shop, shop.Ana, pieceId, new RecordingBoard());
                Assert.True(released.IsSuccess, released.Error);
            }));

        var result = await CompleteAsync(racing, shop, shop.Owner, orderId, pieceId);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await LoadPieceAsync(database, shop.TenantId, pieceId);
        Assert.Equal(ServiceOrderPieceStatus.Completed, stored.Status);
        Assert.Equal((shop.Owner.Id, shop.Owner.UserName), (stored.AssignedToUserId, stored.AssignedToName));
        Assert.NotNull(stored.AssignedAt);
    }

    private static readonly (Guid?, string?, DateTimeOffset?) Nobody = (null, null, null);

    private static (Guid?, string?, DateTimeOffset?) Assignment(Member member, DateTimeOffset at) => (member.Id, member.UserName, at);

    private static (Guid?, string?, DateTimeOffset?) AssignmentOf(ServiceOrderPiece piece)
        => (piece.AssignedToUserId, piece.AssignedToName, piece.AssignedAt);

    private static Task<Result> ReleaseAsync(ApplicationDbContext context, Shop shop, Member member, Guid pieceId, RecordingBoard board)
        => new ReleaseServiceOrderPieceAssignmentCommandHandler(context, member, Access(member, shop.TenantId), board)
            .Handle(new ReleaseServiceOrderPieceAssignmentCommand { PieceId = pieceId }, CancellationToken.None);

    private static Task<Result> AssignAsync(ApplicationDbContext context, Shop shop, Member member, Guid pieceId, RecordingBoard board)
        => new AssignServiceOrderPieceToMeCommandHandler(context, member, Access(member, shop.TenantId), board)
            .Handle(new AssignServiceOrderPieceToMeCommand { PieceId = pieceId }, CancellationToken.None);

    private static Task<Result> CompleteAsync(ApplicationDbContext context, Shop shop, Member member, Guid orderId, Guid pieceId)
        => new UpdateServiceOrderPieceStatusCommandHandler(context, member, Access(member, shop.TenantId),
                new OrderInventoryService(context, new Clock(), new FolioGenerator(context), NullLogger<OrderInventoryService>.Instance))
            .Handle(new UpdateServiceOrderPieceStatusCommand
            {
                OrderId = orderId,
                PieceId = pieceId,
                NewStatus = ServiceOrderPieceStatus.Completed
            }, CancellationToken.None);

    /// <summary>
    /// A context whose first save is preceded by <paramref name="change"/> of the piece through its order, saved by another
    /// request: what lands between what the use case read and what it writes.
    /// </summary>
    private static ApplicationDbContext Racing(
        SqlServerTestDatabase database, Guid tenantId, Guid pieceId, Action<Order, ServiceOrderPiece> change)
        => database.CreateProductionLikeContext(Tenant(tenantId), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenantId);
                var order = await other.Orders.Include(o => o.ServicePieces)
                    .SingleAsync(o => o.ServicePieces.Any(p => p.Id == pieceId));
                change(order, order.ServicePieces.Single(p => p.Id == pieceId));
                await other.SaveChangesAsync();
            }));

    private static async Task<ServiceOrderPiece> LoadPieceAsync(SqlServerTestDatabase database, Guid tenantId, Guid pieceId)
    {
        await using var context = database.CreateContext(tenantId);
        return await context.ServiceOrderPieces.AsNoTracking().SingleAsync(p => p.Id == pieceId);
    }

    /// <summary>A piece of a service order on the board, taken by <paramref name="assignedTo"/> while open and moved to the status.</summary>
    private static async Task<Guid> SeedPieceAsync(SqlServerTestDatabase database, Shop shop, ServiceOrderPieceStatus status, Member? assignedTo)
    {
        await using var context = database.CreateContext(shop.TenantId);
        var order = new Order(shop.TenantId, null, null, createdByBusiness: true, "S-000001", BusinessType.Service, OrderType.Service,
            "Valeria", "+525522107765", null, null);
        order.AddItem(Guid.NewGuid(), "Limpieza de tenis", 1, 250m);
        order.AddServicePiece("Par Nike", null, null, null, null, null, null, "Lavado premium", null);
        var piece = order.ServicePieces.Single();
        if (assignedTo is not null)
            order.AssignServicePiece(piece, assignedTo.Id, assignedTo.UserName, AssignedAt);
        if (status != ServiceOrderPieceStatus.Accepted)
            order.ChangeServicePieceStatus(piece, status, AssignedAt);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return piece.Id;
    }

    /// <summary>
    /// A shop with the service board, its owner and two employees, each with an account (a piece points to its user).
    /// </summary>
    private static async Task<Shop> SeedShopAsync(SqlServerTestDatabase database)
    {
        var tenant = new Tenant("Lavado de Tenis Norte", "tenis-norte", BusinessType.Service, "tenis@demo.com");
        var shop = new Shop(tenant.Id, new Member("Ana", nameof(TenantUserRole.Staff)), new Member("Luis", nameof(TenantUserRole.Staff)),
            new Member("Marta", nameof(TenantUserRole.Owner)));
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service));
        seed.Users.AddRange(UserOf(shop.Ana), UserOf(shop.Luis), UserOf(shop.Owner));
        await seed.SaveChangesAsync();
        return shop;
    }

    private static ApplicationUser UserOf(Member member)
    {
        var email = $"{member.UserName!.ToLowerInvariant()}-{member.Id:N}@tenis.mx";
        return new ApplicationUser
        {
            Id = member.Id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = member.UserName,
            LastName = "Tenis",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = member.TenantRole == nameof(TenantUserRole.Owner) ? UserRole.TenantAdmin : UserRole.Staff,
            IsActive = true
        };
    }

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    private static CurrentAccessContext Access(Member member, Guid tenantId) => new(member, Tenant(tenantId), new MembershipOf(member));

    private sealed record Shop(Guid TenantId, Member Ana, Member Luis, Member Owner);

    /// <summary>A member of the business with the service board, by name and membership role.</summary>
    private sealed class Member(string name, string tenantRole) : ICurrentUserService
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string? UserId => Id.ToString();
        public string? UserName => name;
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => tenantRole;
        public IReadOnlyCollection<string> Permissions => [nameof(TenantPermission.ManageServiceBoard)];
        public Guid? TenantId => null;
    }

    /// <summary>An active member of the business, who manages it when their membership role is owner or admin.</summary>
    private sealed class MembershipOf(Member member) : ITenantAccessService
    {
        private bool IsManager => member.TenantRole is nameof(TenantUserRole.Owner) or nameof(TenantUserRole.Admin);

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Service, member.Permissions, IsManager: IsManager));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken)
            => Task.FromResult(capability == TenantCapability.Service);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken)
            => Task.FromResult((capabilities & TenantCapability.Service) != 0);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(member.Permissions);

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(member.Permissions.Contains(permission.ToString()));
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => AssignedAt.AddHours(2);
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
