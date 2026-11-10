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
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Taking and releasing a piece of the service board on SQL Server: both write the order row, like changing the piece's
/// status, so what someone else saved meanwhile is caught by the order's row version and the use case decides again with
/// it. A release no longer erases who worked a piece completed meanwhile, a piece completed while it was released stays
/// with whoever completed it, and of two employees who take a piece at once only the one who saved first keeps it.
/// </summary>
public class ServiceBoardAssignmentSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);
    private static readonly Member Owner = new(Guid.Parse("4f6c2a0e-8d8b-4a8f-9a55-0c4b1f3f8a10"), "duena@demo.com", IsManager: true);
    private static readonly Member Luis = new(Guid.Parse("8b1d9c3e-2f4a-4e6b-9c7d-1a2b3c4d5e6f"), "luis@demo.com", IsManager: false);
    private static readonly Member Ana = new(Guid.Parse("c2e4a6b8-0d1f-4a3c-8e5b-7f9a1b3c5d7e"), "ana@demo.com", IsManager: false);

    [SqlServerFact]
    public async Task Release_OfAPieceCompletedMeanwhile_ShouldBeRefused_KeepingWhoWorkedIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedTenantAsync(database);
        var (orderId, pieceId) = await SeedPieceAsync(database, tenantId, ServiceOrderPieceStatus.InProgress, assignedTo: Luis);
        // Luis completes the piece after the owner's release read it in progress, and saves first.
        await using var racing = Racing(database, tenantId, async other =>
        {
            var completed = await CompleteAsync(other, tenantId, Luis, orderId, pieceId);
            Assert.True(completed.IsSuccess, completed.Error);
        });

        var result = await ReleaseAsync(racing, tenantId, Owner, pieceId);

        Assert.Equal("No se puede liberar una pieza completada o cancelada.", result.Error);
        var piece = await LoadAsync(database, tenantId, pieceId);
        Assert.Equal((ServiceOrderPieceStatus.Completed, Luis.Id, Luis.UserName), (piece.Status, piece.AssignedToUserId, piece.AssignedToName));
    }

    [SqlServerFact]
    public async Task Completing_APieceReleasedMeanwhile_ShouldLeaveItWithWhoCompletedIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedTenantAsync(database);
        var (orderId, pieceId) = await SeedPieceAsync(database, tenantId, ServiceOrderPieceStatus.InProgress, assignedTo: Luis);
        // Luis releases the piece after the owner's completion read it as his, and saves first.
        await using var racing = Racing(database, tenantId, async other =>
        {
            var released = await ReleaseAsync(other, tenantId, Luis, pieceId);
            Assert.True(released.IsSuccess, released.Error);
        });

        var result = await CompleteAsync(racing, tenantId, Owner, orderId, pieceId);

        Assert.True(result.IsSuccess, result.Error);
        var piece = await LoadAsync(database, tenantId, pieceId);
        Assert.Equal((ServiceOrderPieceStatus.Completed, Owner.Id, Owner.UserName), (piece.Status, piece.AssignedToUserId, piece.AssignedToName));
    }

    [SqlServerFact]
    public async Task TwoEmployeesTakingAPieceAtOnce_ShouldLeaveItToTheOneWhoSavedFirst()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedTenantAsync(database);
        var (_, pieceId) = await SeedPieceAsync(database, tenantId, ServiceOrderPieceStatus.Accepted, assignedTo: null);
        // Luis takes the piece after Ana's request read it free, and saves first.
        await using var racing = Racing(database, tenantId, async other =>
        {
            var taken = await AssignAsync(other, tenantId, Luis, pieceId);
            Assert.True(taken.IsSuccess, taken.Error);
        });

        var result = await AssignAsync(racing, tenantId, Ana, pieceId);

        Assert.Equal("La pieza ya esta asignada a otro empleado.", result.Error);
        var piece = await LoadAsync(database, tenantId, pieceId);
        Assert.Equal((ServiceOrderPieceStatus.Accepted, Luis.Id, Luis.UserName), (piece.Status, piece.AssignedToUserId, piece.AssignedToName));
    }

    private static Task<Result> AssignAsync(ApplicationDbContext context, Guid tenantId, Member member, Guid pieceId)
        => new AssignServiceOrderPieceToMeCommandHandler(context, member, Access(member, tenantId), NoScreens.Instance)
            .Handle(new AssignServiceOrderPieceToMeCommand { PieceId = pieceId }, CancellationToken.None);

    private static Task<Result> ReleaseAsync(ApplicationDbContext context, Guid tenantId, Member member, Guid pieceId)
        => new ReleaseServiceOrderPieceAssignmentCommandHandler(context, member, Access(member, tenantId), NoScreens.Instance)
            .Handle(new ReleaseServiceOrderPieceAssignmentCommand { PieceId = pieceId }, CancellationToken.None);

    private static Task<Result> CompleteAsync(ApplicationDbContext context, Guid tenantId, Member member, Guid orderId, Guid pieceId)
        => new UpdateServiceOrderPieceStatusCommandHandler(context, member, Access(member, tenantId),
                new OrderInventoryService(context, new Clock(), new FolioGenerator(context), NullLogger<OrderInventoryService>.Instance))
            .Handle(new UpdateServiceOrderPieceStatusCommand
            {
                OrderId = orderId,
                PieceId = pieceId,
                NewStatus = ServiceOrderPieceStatus.Completed
            }, CancellationToken.None);

    /// <summary>A context set up like production whose first save is preceded by <paramref name="change"/>, saved by someone else.</summary>
    private static ApplicationDbContext Racing(SqlServerTestDatabase database, Guid tenantId, Func<ApplicationDbContext, Task> change)
        => Context(database, tenantId, new SavedMeanwhile(async () =>
        {
            await using var other = Context(database, tenantId);
            await change(other);
        }));

    private static ApplicationDbContext Context(SqlServerTestDatabase database, Guid tenantId, params SavedMeanwhile[] meanwhile)
    {
        var tenant = Tenant(tenantId);
        return database.CreateProductionLikeContext(tenant, new InMemoryApplicationDbContext.NullMediator(),
            [new TenantSaveChangesInterceptor(tenant), .. meanwhile]);
    }

    private static async Task<ServiceOrderPiece> LoadAsync(SqlServerTestDatabase database, Guid tenantId, Guid pieceId)
    {
        await using var context = database.CreateContext(tenantId);
        return await context.ServiceOrderPieces.AsNoTracking().SingleAsync(p => p.Id == pieceId);
    }

    /// <summary>A service order with one piece of the board, moved to <paramref name="status"/> and taken by <paramref name="assignedTo"/>.</summary>
    private static async Task<(Guid OrderId, Guid PieceId)> SeedPieceAsync(
        SqlServerTestDatabase database, Guid tenantId, ServiceOrderPieceStatus status, Member? assignedTo)
    {
        var order = new Order(tenantId, null, null, createdByBusiness: true, "S-000001", BusinessType.Service, OrderType.Service,
            "Valeria", "+525522107765", null, null);
        order.AddItem(Guid.NewGuid(), "Lavado de tenis", 1, 180m);
        order.AddServicePiece("Par Nike", null, null, null, null, null, null, "Lavado premium", null);
        var piece = order.ServicePieces.Single();
        if (assignedTo is not null)
            order.AssignServicePiece(piece, assignedTo.Id, assignedTo.UserName, Now);
        if (status == ServiceOrderPieceStatus.InProgress)
            order.ChangeServicePieceStatus(piece, ServiceOrderPieceStatus.InProgress, Now);

        await using var context = database.CreateContext(tenantId);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order.Id, piece.Id);
    }

    /// <summary>The business, with the accounts of the owner and her two employees (who takes a piece is an account).</summary>
    private static async Task<Guid> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var tenant = new Tenant("Lavandería La Esquina", "la-esquina", BusinessType.Service, "esquina@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service));
        seed.AddRange(new[] { Owner, Luis, Ana }.Select(member => new ApplicationUser
        {
            Id = member.Id,
            UserName = member.UserName,
            NormalizedUserName = member.UserName.ToUpperInvariant(),
            Email = member.UserName,
            NormalizedEmail = member.UserName.ToUpperInvariant(),
            FirstName = member.UserName.Split('@')[0],
            LastName = "Esquina",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = member.IsManager ? UserRole.TenantAdmin : UserRole.Staff,
            IsActive = true
        }));
        await seed.SaveChangesAsync();
        return tenant.Id;
    }

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    private static CurrentAccessContext Access(Member member, Guid tenantId) => new(member, Tenant(tenantId), new MembershipOf(member));

    /// <summary>A member of the business: the owner manages it; an employee works the board without managing it.</summary>
    private sealed record Member(Guid Id, string UserName, bool IsManager) : ICurrentUserService
    {
        public string? UserId => Id.ToString();
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => IsManager ? nameof(TenantUserRole.Owner) : nameof(TenantUserRole.Staff);
        public IReadOnlyCollection<string> Permissions => [nameof(TenantPermission.ManageServiceBoard), nameof(TenantPermission.ManageOrders)];
        public Guid? TenantId => null;
    }

    private sealed class MembershipOf(Member member) : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Service, member.Permissions, IsManager: member.IsManager));

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

    /// <summary>The live screens of the board, which nobody watches in these tests.</summary>
    private sealed class NoScreens : IOrderNotificationService
    {
        public static readonly NoScreens Instance = new();

        public Task NotifyOrderCreated(Guid tenantId, Guid orderId, string orderNumber, BusinessType businessType, decimal total,
            OrderStatus status, OrderChannel channel, string? externalOrderNumber) => Task.CompletedTask;
        public Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus) => Task.CompletedTask;
        public Task NotifyOrderCancelled(Guid tenantId, Guid orderId, OrderStatus previousStatus, string? reason, string orderNumber, bool byCustomer)
            => Task.CompletedTask;
        public Task NotifyKitchenTicketsChanged(Guid tenantId, Guid orderId, int round) => Task.CompletedTask;
        public Task NotifyOrderUpdated(Guid tenantId, Guid orderId) => Task.CompletedTask;
        public Task NotifyServiceBoardPieceUpdated(Guid tenantId, Guid orderId, Guid pieceId, ServiceOrderPieceStatus status, Guid? assignedToUserId)
            => Task.CompletedTask;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now.AddHours(1);
    }
}
