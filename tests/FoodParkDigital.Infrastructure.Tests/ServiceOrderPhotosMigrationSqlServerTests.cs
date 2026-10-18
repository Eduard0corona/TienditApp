using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddServiceOrderPhotos (Fase 25, ADR 0024): the photos of a service order keep a stage the domain knows, go with the
/// order and its business; the version deployed before keeps working over the new table; and rolling back stops with
/// error 50018 once a business added a photo, and otherwise drops only the table.
/// </summary>
public class ServiceOrderPhotosMigrationSqlServerTests
{
    // The migration right before AddServiceOrderPhotos, where its rollback.sql takes the database.
    private const string BeforePhotos = "20261017100104_AddOrderTrackingToken";
    private const string Migration = "20261017183622_AddServiceOrderPhotos";
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Author = Guid.NewGuid();

    [SqlServerFact]
    public async Task Photos_ShouldKeepAKnownStage_StayInTheirBusiness_AndGoWithTheOrder()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var other = await SeedTenantAsync(database);
        var orderId = await SeedOrderWithPhotosAsync(database, tenant.Id);

        await using (var context = BeforeOnlineBooking.CreateContext(database, tenant.Id))
        {
            var order = await context.Orders.Include(o => o.ServicePhotos).SingleAsync(o => o.Id == orderId);
            Assert.Equal(
                [(ServicePhotoStage.Before, "Mancha en la manga", "Ana", (Guid?)Author), (ServicePhotoStage.After, (string?)null, (string?)"Luis", (Guid?)null)],
                order.ServicePhotos.OrderBy(p => p.Stage).Select(p => (p.Stage, p.Note, p.TakenBy, p.TakenByUserId)).ToList());
        }
        // Another business does not see them, not even asking for them by the order.
        await using (var neighbor = BeforeOnlineBooking.CreateContext(database, other.Id))
            Assert.Empty(await neighbor.Set<ServiceOrderPhoto>().Where(p => p.OrderId == orderId).ToListAsync());

        var stage = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database,
            $"UPDATE [ServiceOrderPhotos] SET [Stage] = {2} WHERE [OrderId] = {orderId}"));
        Assert.Equal(547, stage.Number);
        Assert.Contains("CK_ServiceOrderPhotos_Stage", stage.Message);

        await ExecuteAsync(database, $"DELETE FROM [Orders] WHERE [Id] = {orderId}");
        await using var check = BeforeOnlineBooking.CreateContext(database);
        Assert.Equal(0, await CountAsync(check, $"SELECT COUNT(*) AS [Value] FROM [ServiceOrderPhotos]"));
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepCreatingAndFinishingOrders_AndThePhotosStay()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var withPhotos = await SeedOrderWithPhotosAsync(database, tenant.Id);

        // The version deployed before does not know the table: it creates orders and moves them as always.
        await using (var previous = BeforeServicePhotos.CreateContext(database, tenant.Id))
        {
            previous.Orders.Add(NewServiceOrder(tenant.Id));
            var order = await previous.Orders.SingleAsync(o => o.Id == withPhotos);
            order.TransitionTo(OrderStatus.Completed, new ServiceOrderStateMachine(), "mostrador");
            await previous.SaveChangesAsync();
        }

        await using var check = BeforeOnlineBooking.CreateContext(database, tenant.Id);
        Assert.Equal(2, await check.Orders.CountAsync());
        var finished = await check.Orders.Include(o => o.ServicePhotos).SingleAsync(o => o.Id == withPhotos);
        Assert.Equal(OrderStatus.Completed, finished.Status);
        Assert.Equal(2, finished.ServicePhotos.Count);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceABusinessAddedAPhoto_AndThenDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await SeedOrderWithPhotosAsync(database, tenant.Id);

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforePhotos));
        Assert.Equal(50018, error.Number);
        Assert.Contains("Businesses added photos to their service orders", error.Message);
        await using (var context = BeforeOnlineBooking.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(2, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [ServiceOrderPhotos]"));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [ServiceOrderPhotos]");
        }

        await MigrateAsync(database, BeforePhotos);
        await using (var context = BeforeOnlineBooking.CreateContext(database))
        {
            Assert.Equal(BeforePhotos, await LatestMigrationAsync(context));
            Assert.Equal((0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'ServiceOrderPhotos'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Orders]")));
        }

        // Rolling back again does nothing, and forward again the table is back, empty.
        await MigrateAsync(database, BeforePhotos);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeOnlineBooking.CreateContext(database);
        Assert.Equal((1, 0), (
            await CountAsync(forward, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'ServiceOrderPhotos'"),
            await CountAsync(forward, $"SELECT COUNT(*) AS [Value] FROM [ServiceOrderPhotos]")));
    }

    /// <summary>A service order with a photo of what was received, with its note, and one of the work done.</summary>
    private static async Task<Guid> SeedOrderWithPhotosAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        await using var context = BeforeOnlineBooking.CreateContext(database, tenantId);
        var order = NewServiceOrder(tenantId);
        order.AddServicePhoto(ServicePhotoStage.Before, $"/files/orders/{tenantId}/service-intake/202610/antes.webp", "antes.jpg",
            "Mancha en la manga", Now, "Ana", Author);
        order.AddServicePhoto(ServicePhotoStage.After, $"/files/orders/{tenantId}/service-intake/202610/despues.webp", "despues.jpg",
            null, Now.AddHours(2), "Luis");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static Order NewServiceOrder(Guid tenantId)
    {
        var order = new Order(
            tenantId, null, null, createdByBusiness: true, $"S-{Guid.NewGuid():N}"[..10], BusinessType.Service,
            OrderType.Service, "Valeria", customerPhone: "+525522107765", customerNotes: null, tableNumber: null,
            pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Lavado de saco", 1, 180m);
        return order;
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Tintorería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        await using var seed = BeforeOnlineBooking.CreateContext(database);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = BeforeOnlineBooking.CreateContext(database);
        await context.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
