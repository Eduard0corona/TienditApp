using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddAppointmentForeignKeys (Fase 31H, ADR 0088): the service and the team member of an appointment point to a product and
/// an account that exist. The links that already pointed to nothing are emptied, keeping the names the appointment was
/// booked with; deleting a product empties the link of its appointments, as the version before it keeps doing; rolling back
/// drops only the keys and their indexes. The schema of Appointments is the same on both sides, so the model of today
/// reads and writes the database before the migration too.
/// </summary>
public class AppointmentForeignKeysMigrationSqlServerTests
{
    // The migration right before AddAppointmentForeignKeys, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261110022936_AddUniqueCategoryNamePerTenant";
    private const string Migration = "20261110192224_AddAppointmentForeignKeys";
    private const string ProductKey = "FK_Appointments_Products_ServiceProductId";
    private const string StaffKey = "FK_Appointments_AspNetUsers_StaffUserId";
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 16, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task LinksThatPointToNothing_ShouldBeEmptied_KeepingTheNames_AndTheOthersStay()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);

        await MigrateAsync(database, Migration);

        await using var context = database.CreateContext(shop.TenantId);
        var linked = await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.LinkedId);
        Assert.Equal((shop.ProductId, shop.StaffId), (linked.ServiceProductId, linked.StaffUserId));
        var deletedProduct = await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.DeletedProductId);
        Assert.Equal((null, "Corte de cabello", shop.StaffId), (deletedProduct.ServiceProductId, deletedProduct.ServiceName, deletedProduct.StaffUserId));
        var missingAccount = await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.MissingAccountId);
        Assert.Equal((shop.ProductId, null, "Luis"), (missingAccount.ServiceProductId, missingAccount.StaffUserId, missingAccount.StaffName));
        Assert.Equal(new[] { (StaffKey, "NO_ACTION"), (ProductKey, "SET_NULL") }, await KeysAsync(context));
    }

    [SqlServerFact]
    public async Task TheKeys_ShouldRefuseALinkToNothing_AndDeletingTheProduct_ShouldKeepItsAppointmentsWithTheirName()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext(shop.TenantId))
        {
            context.Appointments.Add(Appointment(shop.TenantId, serviceProductId: Guid.NewGuid()));
            var noProduct = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains(ProductKey, noProduct.InnerException?.Message);
        }

        await using (var context = database.CreateContext(shop.TenantId))
        {
            context.Appointments.Add(Appointment(shop.TenantId, staffUserId: Guid.NewGuid()));
            var noAccount = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains(StaffKey, noAccount.InnerException?.Message);
        }

        // The account stays while an appointment points to it, as the customer's does.
        await using (var context = database.CreateContext(shop.TenantId))
        {
            var deleteAccount = await Record.ExceptionAsync(() => context.Database.ExecuteSqlAsync(
                $"DELETE FROM [AspNetUsers] WHERE [Id] = {shop.StaffId}"));
            Assert.Contains(StaffKey, deleteAccount?.Message);
        }

        // Deleting the product, as DeleteProductCommand does (and the version before this one, unaware of the key): its
        // appointments, not loaded, keep their name and everything else, without the link.
        await using (var context = database.CreateContext(shop.TenantId))
        {
            context.Products.Remove(await context.Products.SingleAsync(p => p.Id == shop.ProductId));
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(shop.TenantId);
        var linked = await check.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.LinkedId);
        Assert.Equal(
            (null, "Corte de cabello", shop.StaffId, "Luis", Start),
            (linked.ServiceProductId, linked.ServiceName, linked.StaffUserId, linked.StaffName, linked.StartAt));
        Assert.Equal(0, await check.Appointments.CountAsync(a => a.ServiceProductId != null));
        Assert.Equal(3, await check.Appointments.CountAsync());
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheKeysAndTheirIndexes_AndForwardAgainEmptyTheNewLinksToNothing()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);

        await MigrateAsync(database, BeforeMigration);
        await using (var context = database.CreateContext(shop.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Empty(await KeysAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.Appointments')
                    AND [name] IN (N'IX_Appointments_ServiceProductId', N'IX_Appointments_StaffUserId')
                """).SingleAsync());
            // The appointments stay as the migration left them, and a link to nothing is taken again, as before it.
            Assert.Equal(3, await context.Appointments.CountAsync());
            Assert.Null((await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.DeletedProductId)).ServiceProductId);
            context.Appointments.Add(Appointment(shop.TenantId, serviceProductId: Guid.NewGuid(), staffUserId: Guid.NewGuid()));
            await context.SaveChangesAsync();
        }

        // Rolling back again does nothing, and forward again empties the new links to nothing.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(shop.TenantId);
        Assert.Equal(Migration, await LatestMigrationAsync(forward));
        Assert.Equal(2, (await KeysAsync(forward)).Count);
        Assert.Equal(1, await forward.Appointments.CountAsync(a => a.ServiceProductId == null && a.StaffUserId == null));
    }

    private sealed record Shop(Guid TenantId, Guid ProductId, Guid StaffId, Guid LinkedId, Guid DeletedProductId, Guid MissingAccountId);

    /// <summary>
    /// A barbershop with a service and a barber, and three appointments: one linked to both, one whose service was deleted
    /// before the keys existed, and one whose team member points to an account that does not exist.
    /// </summary>
    private static async Task<Shop> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Barbería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var category = new Category(tenant.Id, "Servicios", null);
        var product = new Product(tenant.Id, category.Id, "Corte de cabello", null, 150m, "MXN");
        var barber = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"luis-{slug}@demo.com",
            NormalizedUserName = $"LUIS-{slug}@DEMO.COM",
            Email = $"luis-{slug}@demo.com",
            NormalizedEmail = $"LUIS-{slug}@DEMO.COM",
            FirstName = "Luis",
            LastName = "Barbero",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Staff,
            IsActive = true
        };
        var linked = Appointment(tenant.Id, product.Id, barber.Id);
        var deletedProduct = Appointment(tenant.Id, Guid.NewGuid(), barber.Id);
        var missingAccount = Appointment(tenant.Id, product.Id, Guid.NewGuid());

        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments), category, product);
        seed.Users.Add(barber);
        seed.Appointments.AddRange(linked, deletedProduct, missingAccount);
        await seed.SaveChangesAsync();
        return new Shop(tenant.Id, product.Id, barber.Id, linked.Id, deletedProduct.Id, missingAccount.Id);
    }

    private static Appointment Appointment(Guid tenantId, Guid? serviceProductId = null, Guid? staffUserId = null)
        => new(tenantId, "Ana", "+525512345678", "Corte de cabello", Start, Start.AddMinutes(30), "Luis",
            serviceProductId: serviceProductId, staffUserId: staffUserId);

    private static async Task<List<(string Name, string OnDelete)>> KeysAsync(ApplicationDbContext context)
        => (await context.Database.SqlQuery<KeyRow>($"""
                SELECT [name] AS [Name], [delete_referential_action_desc] AS [OnDelete] FROM sys.foreign_keys
                WHERE [parent_object_id] = OBJECT_ID(N'dbo.Appointments') AND [is_not_trusted] = 0
                    AND [name] IN (N'FK_Appointments_AspNetUsers_StaffUserId', N'FK_Appointments_Products_ServiceProductId')
                """).ToListAsync())
            .OrderBy(k => k.Name, StringComparer.Ordinal)
            .Select(k => (k.Name, k.OnDelete))
            .ToList();

    private sealed class KeyRow
    {
        public string Name { get; set; } = null!;
        public string OnDelete { get; set; } = null!;
    }

    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
