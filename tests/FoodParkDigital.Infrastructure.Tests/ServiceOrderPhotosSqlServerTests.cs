using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The photos of a service on SQL Server (Fase 25, ADR 0024): every change of the photos changes the order row, so a
/// photo added or a state saved by someone else meanwhile is caught by its row version, and the use case tries again
/// with what they saved: both photos stay, the twenty of a stage hold, and a photo of an order closed meanwhile stays.
/// </summary>
public class ServiceOrderPhotosSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task PhotoAddedMeanwhile_ShouldStay_WithTheOneOfThisRequest()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenant = await SeedTenantAsync(database);
        var orderId = await SeedOrderAsync(database, tenant.Id, photos: 0);
        await using var racing = Racing(database, tenant.Id, async other =>
            (await LoadAsync(other, orderId)).AddServicePhoto(ServicePhotoStage.Before, File(tenant.Id), null, "De Luis", Now.AddMinutes(30), "Luis"));

        var result = await AddAsync(racing, tenant.Id, orderId, ServicePhotoStage.Before);

        Assert.True(result.IsSuccess, result.Error);
        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(["De Ana", "De Luis"], (await LoadAsync(check, orderId)).ServicePhotos.Select(p => p.Note).Order());
    }

    [SqlServerFact]
    public async Task TwentiethPhotoAddedMeanwhile_ShouldLeaveNoRoom_ForOneMore()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenant = await SeedTenantAsync(database);
        var orderId = await SeedOrderAsync(database, tenant.Id, photos: ServiceOrderPhoto.MaxPerStage - 1);
        // Each request takes its photo at its own time: the order row changes with it.
        await using var racing = Racing(database, tenant.Id, async other =>
            (await LoadAsync(other, orderId)).AddServicePhoto(ServicePhotoStage.Before, File(tenant.Id), null, null, Now.AddMinutes(30), "Luis"));

        var result = await AddAsync(racing, tenant.Id, orderId, ServicePhotoStage.Before);

        Assert.Equal(Order.TooManyPhotosMessage(ServicePhotoStage.Before), result.Error);
        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(ServiceOrderPhoto.MaxPerStage, (await LoadAsync(check, orderId)).ServicePhotos.Count);
    }

    [SqlServerFact]
    public async Task Photo_OfAnOrderFinishedMeanwhile_ShouldStay()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenant = await SeedTenantAsync(database);
        var orderId = await SeedOrderAsync(database, tenant.Id, photos: 1);
        Guid photoId;
        await using (var context = database.CreateContext(tenant.Id))
            photoId = (await LoadAsync(context, orderId)).ServicePhotos.Single().Id;
        await using var racing = Racing(database, tenant.Id, async other =>
            (await LoadAsync(other, orderId)).TransitionTo(OrderStatus.Completed, new ServiceOrderStateMachine(), "mostrador"));

        var result = await new RemoveServiceOrderPhotoCommandHandler(racing, Owner.Instance, Access(tenant.Id), new Clock())
            .Handle(new RemoveServiceOrderPhotoCommand(orderId, photoId), CancellationToken.None);

        Assert.Equal(Order.PhotosOfFinishedOrderMessage, result.Error);
        await using var check = database.CreateContext(tenant.Id);
        var order = await LoadAsync(check, orderId);
        Assert.Equal((OrderStatus.Completed, 1), (order.Status, order.ServicePhotos.Count));
    }

    private static Task<Result<ServiceOrderPhotoDto>> AddAsync(ApplicationDbContext context, Guid tenantId, Guid orderId, ServicePhotoStage stage)
        => new AddServiceOrderPhotoCommandHandler(context, Tenant(tenantId), Owner.Instance, Access(tenantId), new Clock())
            .Handle(new AddServiceOrderPhotoCommand { OrderId = orderId, Stage = stage, FileUrl = File(tenantId), Note = "De Ana" },
                CancellationToken.None);

    /// <summary>A context whose first save is preceded by <paramref name="change"/>, saved by someone else.</summary>
    private static ApplicationDbContext Racing(SqlServerTestDatabase database, Guid tenantId, Func<ApplicationDbContext, Task> change)
        => database.CreateProductionLikeContext(Tenant(tenantId), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenantId);
                await change(other);
                await other.SaveChangesAsync();
            }));

    private static Task<Order> LoadAsync(ApplicationDbContext context, Guid orderId)
        => context.Orders.Include(o => o.ServicePhotos).SingleAsync(o => o.Id == orderId);

    private static async Task<Guid> SeedOrderAsync(SqlServerTestDatabase database, Guid tenantId, int photos)
    {
        await using var context = database.CreateContext(tenantId);
        var order = new Order(tenantId, null, null, createdByBusiness: true, "S-000001", BusinessType.Service, OrderType.Service,
            "Valeria", "+525522107765", null, null);
        order.AddItem(Guid.NewGuid(), "Lavado de saco", 1, 180m);
        for (var i = 0; i < photos; i++)
            order.AddServicePhoto(ServicePhotoStage.Before, File(tenantId), null, null, Now, "Luis");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var tenant = new Tenant("Tintorería La Esquina", "la-esquina", BusinessType.Service, "esquina@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service));
        await seed.SaveChangesAsync();
        return tenant;
    }

    private static string File(Guid tenantId) => $"/files/orders/{tenantId}/service-intake/202610/{Guid.NewGuid()}.webp";

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    private static CurrentAccessContext Access(Guid tenantId) => new(Owner.Instance, Tenant(tenantId), new OwnerAccess());

    private sealed class Owner : ICurrentUserService
    {
        public static readonly Owner Instance = new();
        public string? UserId => "4f6c2a0e-8d8b-4a8f-9a55-0c4b1f3f8a10";
        public string? UserName => "duena@demo.com";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    /// <summary>The owner of the business: an active member who manages it.</summary>
    private sealed class OwnerAccess : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Service, [], IsManager: true));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken)
            => Task.FromResult(capability == TenantCapability.Service);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken)
            => Task.FromResult((capabilities & TenantCapability.Service) != 0);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now.AddHours(1);
    }
}
