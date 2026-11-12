using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
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
/// AddAppointmentStaffForeignKey (Fase 31H, ADR 0088): the team member of an appointment points to an account that exists.
/// An appointment that points to one that does not stops the migration with error 50058 before anything changes, since
/// accounts are never deleted and that Id would be something else. The service keeps no key, on purpose: deleting its
/// product leaves the appointment pointing to the Id the sale of a visit and the credit of its quote match on. Rolling back
/// drops only the key and its index. The schema of Appointments is the same on both sides, so the model of today reads and
/// writes the database before the migration too.
/// </summary>
public class AppointmentStaffForeignKeyMigrationSqlServerTests
{
    // The migration right before AddAppointmentStaffForeignKey, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261110022936_AddUniqueCategoryNamePerTenant";
    private const string Migration = "20261110201340_AddAppointmentStaffForeignKey";
    private const string Key = "FK_Appointments_AspNetUsers_StaffUserId";
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 16, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task AnAppointmentWithAnAccountThatDoesNotExist_ShouldStopIt_WithoutChanges_UntilItIsLookedInto()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        Guid stranger;
        await using (var context = BeforeAccountCarts.CreateContext(database, shop.TenantId))
        {
            var appointment = Appointment(shop.TenantId, staffUserId: Guid.NewGuid());
            context.Appointments.Add(appointment);
            await context.SaveChangesAsync();
            stranger = appointment.Id;
        }

        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, Migration));
        Assert.True(stopped is SqlException { Number: 50058 }, stopped?.Message ?? "it was not stopped");
        await using (var context = BeforeAccountCarts.CreateContext(database, shop.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Empty(await KeysAsync(context));
            Assert.Equal(0, await IndexesAsync(context));
            Assert.NotNull((await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == stranger)).StaffUserId);
            Assert.Equal(shop.StaffId, (await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.AppointmentId)).StaffUserId);

            // Looked into, as query 3 of verify.sql says: it points to nobody of the team, so it goes, and the name stays.
            await context.Database.ExecuteSqlAsync($"UPDATE [Appointments] SET [StaffUserId] = NULL WHERE [Id] = {stranger}");
        }

        await MigrateAsync(database, Migration);
        await using var migrated = BeforeAccountCarts.CreateContext(database, shop.TenantId);
        Assert.Equal(Migration, await LatestMigrationAsync(migrated));
        Assert.Equal(new[] { (Key, "NO_ACTION") }, await KeysAsync(migrated));
        Assert.Equal(1, await IndexesAsync(migrated));
        var lookedInto = await migrated.Appointments.AsNoTracking().SingleAsync(a => a.Id == stranger);
        Assert.Equal((null, "Luis"), (lookedInto.StaffUserId, lookedInto.StaffName));
        Assert.Equal(shop.StaffId, (await migrated.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.AppointmentId)).StaffUserId);
    }

    [SqlServerFact]
    public async Task TheKey_ShouldRefuseAnAccountThatDoesNotExist_AndKeepTheAccount_ButLeaveTheServiceWithoutOne()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);

        await using (var context = BeforeAccountCarts.CreateContext(database, shop.TenantId))
        {
            context.Appointments.Add(Appointment(shop.TenantId, staffUserId: Guid.NewGuid()));
            var noAccount = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains(Key, noAccount.InnerException?.Message);
        }

        // The account stays while an appointment points to it, as the customer's does.
        await using (var context = BeforeAccountCarts.CreateContext(database, shop.TenantId))
        {
            var deleteAccount = await Record.ExceptionAsync(() => context.Database.ExecuteSqlAsync(
                $"DELETE FROM [AspNetUsers] WHERE [Id] = {shop.StaffId}"));
            Assert.Contains(Key, deleteAccount?.Message);
        }

        // Deleting the product, as DeleteProductCommand does, leaves the appointment pointing to its Id: the sale of a visit
        // and the credit of its quote match their lines on it.
        await using (var context = BeforeAccountCarts.CreateContext(database, shop.TenantId))
        {
            context.Products.Remove(await context.Products.SingleAsync(p => p.Id == shop.ProductId));
            await context.SaveChangesAsync();
        }

        await using var check = BeforeAccountCarts.CreateContext(database, shop.TenantId);
        var appointment = await check.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.AppointmentId);
        Assert.Equal((shop.ProductId, shop.StaffId), (appointment.ServiceProductId, appointment.StaffUserId));
        Assert.Equal(0, await check.Products.CountAsync());
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheKeyAndItsIndex_AndForwardAgainCheckAgain()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAccountCarts.CreateContext(database, shop.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Empty(await KeysAsync(context));
            Assert.Equal(0, await IndexesAsync(context));
            Assert.Equal(shop.StaffId, (await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == shop.AppointmentId)).StaffUserId);
            // As before it, the database takes an account that does not exist.
            context.Appointments.Add(Appointment(shop.TenantId, staffUserId: Guid.NewGuid()));
            await context.SaveChangesAsync();
        }

        // Rolling back again does nothing, and forward again checks again.
        await MigrateAsync(database, BeforeMigration);
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, Migration));
        Assert.True(stopped is SqlException { Number: 50058 }, stopped?.Message ?? "it was not stopped");
    }

    private sealed record Shop(Guid TenantId, Guid ProductId, Guid StaffId, Guid AppointmentId);

    /// <summary>A barbershop with a service, a barber with an account, and an appointment of that service with the barber.</summary>
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
        var appointment = Appointment(tenant.Id, product.Id, barber.Id);

        await using var seed = BeforeAccountCarts.CreateContext(database, tenant.Id);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments), category, product);
        seed.Users.Add(barber);
        seed.Appointments.Add(appointment);
        await seed.SaveChangesAsync();
        return new Shop(tenant.Id, product.Id, barber.Id, appointment.Id);
    }

    private static Appointment Appointment(Guid tenantId, Guid? serviceProductId = null, Guid? staffUserId = null)
        => new(tenantId, "Ana", "+525512345678", "Corte de cabello", Start, Start.AddMinutes(30), "Luis",
            serviceProductId: serviceProductId, staffUserId: staffUserId);

    private static async Task<List<(string Name, string OnDelete)>> KeysAsync(ApplicationDbContext context)
        => (await context.Database.SqlQuery<KeyRow>($"""
                SELECT [name] AS [Name], [delete_referential_action_desc] AS [OnDelete] FROM sys.foreign_keys
                WHERE [parent_object_id] = OBJECT_ID(N'dbo.Appointments') AND [is_not_trusted] = 0
                    AND [name] = N'FK_Appointments_AspNetUsers_StaffUserId'
                """).ToListAsync())
            .Select(k => (k.Name, k.OnDelete))
            .ToList();

    private static Task<int> IndexesAsync(ApplicationDbContext context)
        => context.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value] FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'dbo.Appointments') AND [name] = N'IX_Appointments_StaffUserId'
            """).SingleAsync();

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
