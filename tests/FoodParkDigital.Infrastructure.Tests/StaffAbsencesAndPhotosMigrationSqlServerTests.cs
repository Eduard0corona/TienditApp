using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
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
/// AddOnlineBookingStaffAbsencesAndPhotos (Fase 31I, ADR 0089): a table of the days a person who takes online appointments is
/// away, which goes with the person, and a column with the base URL of their photo. The version deployed before it keeps
/// saving and deleting people on the migrated database. Rolling back stops with error 50059, without changes, once a
/// business set an absence or a photo; without any, it drops only the table and the column.
/// </summary>
public class StaffAbsencesAndPhotosMigrationSqlServerTests
{
    // The migration right before it, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261110201340_AddAppointmentStaffForeignKey";
    private const string Migration = "20261111060433_AddOnlineBookingStaffAbsencesAndPhotos";
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [SqlServerFact]
    public async Task TheDatabase_ShouldKeepTheAbsencesOfEachPerson_RefuseOneBackwards_AndDeleteThemWithThePerson()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext(shop.TenantId))
        {
            var staff = await context.OnlineBookingStaff.Include(s => s.Absences).SingleAsync();
            Assert.Equal(("Luis B.", null), (staff.Name, staff.PhotoUrl));
            staff.ReplaceAbsences([new StaffAbsence(Monday, Monday.AddDays(2)), new StaffAbsence(Monday.AddDays(9), Monday.AddDays(9))], Monday);
            staff.SetPhoto($"/uploads/staff/{shop.TenantId}/luis");
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(shop.TenantId))
        {
            var staff = await context.OnlineBookingStaff.Include(s => s.Absences).AsNoTracking().SingleAsync();
            Assert.Equal(
                [new StaffAbsence(Monday, Monday.AddDays(2)), new StaffAbsence(Monday.AddDays(9), Monday.AddDays(9))],
                staff.AbsencesFrom(Monday));
            Assert.Equal($"/uploads/staff/{shop.TenantId}/luis", staff.PhotoUrl);

            var backwards = await Record.ExceptionAsync(() => context.Database.ExecuteSqlAsync($"""
                INSERT INTO [OnlineBookingStaffAbsences] ([Id], [TenantId], [StaffId], [FirstDay], [LastDay])
                VALUES ({Guid.NewGuid()}, {shop.TenantId}, {staff.Id}, {Monday.AddDays(3)}, {Monday.AddDays(2)})
                """));
            Assert.Contains("CK_OnlineBookingStaffAbsences_Days", backwards?.Message);
        }

        // Deleting the account deletes the person and their absences, as the version before deletes the person.
        await using (var context = database.CreateContext(shop.TenantId))
            await context.Database.ExecuteSqlAsync($"DELETE FROM [AspNetUsers] WHERE [Id] = {shop.StaffUserId}");
        await using var check = database.CreateContext(shop.TenantId);
        Assert.Equal(0, await check.OnlineBookingStaff.CountAsync());
        Assert.Equal(0, await check.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [OnlineBookingStaffAbsences]").SingleAsync());
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingAndDeletingPeople_OnTheMigratedDatabase()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);
        await using (var context = database.CreateContext(shop.TenantId))
        {
            var staff = await context.OnlineBookingStaff.Include(s => s.Absences).SingleAsync();
            staff.ReplaceAbsences([new StaffAbsence(Monday, Monday)], Monday);
            staff.SetPhoto($"/uploads/staff/{shop.TenantId}/luis");
            await context.SaveChangesAsync();
        }

        await using (var before = BeforeStaffAbsencesAndPhotos.CreateContext(database, shop.TenantId))
        {
            var staff = await before.OnlineBookingStaff.SingleAsync();
            staff.Change("Luis Barbero", false, true, [], null);
            before.OnlineBookingStaff.Add(OnlineBookingStaff.Create(shop.TenantId, shop.OtherUserId, "Ana R.", true, true, [], null));
            await before.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(shop.TenantId))
        {
            var people = await context.OnlineBookingStaff.Include(s => s.Absences).AsNoTracking().OrderBy(s => s.Name).ToListAsync();
            Assert.Equal([("Ana R.", null, 0), ("Luis Barbero", $"/uploads/staff/{shop.TenantId}/luis", 1)],
                people.Select(p => (p.Name, p.PhotoUrl, p.Absences.Count)));
        }

        // The version before deletes a person with their absences (the key cascades).
        await using (var before = BeforeStaffAbsencesAndPhotos.CreateContext(database, shop.TenantId))
        {
            before.OnlineBookingStaff.Remove(await before.OnlineBookingStaff.SingleAsync(s => s.UserId == shop.StaffUserId));
            await before.SaveChangesAsync();
        }
        await using var check = database.CreateContext(shop.TenantId);
        Assert.Equal(0, await check.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [OnlineBookingStaffAbsences]").SingleAsync());
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAnAbsenceOrAPhotoWasSet_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);

        // An absence alone stops it.
        await using (var context = database.CreateContext(shop.TenantId))
        {
            var staff = await context.OnlineBookingStaff.Include(s => s.Absences).SingleAsync();
            staff.ReplaceAbsences([new StaffAbsence(Monday, Monday)], Monday);
            await context.SaveChangesAsync();
        }
        await AssertStoppedAsync(database, shop.TenantId, absences: 1, photos: 0);

        // A photo alone stops it too.
        await using (var context = database.CreateContext(shop.TenantId))
        {
            await context.Database.ExecuteSqlAsync($"DELETE FROM [OnlineBookingStaffAbsences]");
            await context.Database.ExecuteSqlAsync($"UPDATE [OnlineBookingStaff] SET [PhotoUrl] = N'/uploads/staff/x/luis'");
        }
        await AssertStoppedAsync(database, shop.TenantId, absences: 0, photos: 1);

        // Without any, as query 3 of verify.sql leaves it: it drops the table and the column, and the people stay.
        await using (var context = database.CreateContext(shop.TenantId))
            await context.Database.ExecuteSqlAsync($"UPDATE [OnlineBookingStaff] SET [PhotoUrl] = NULL");
        await MigrateAsync(database, BeforeMigration);
        await using (var before = BeforeStaffAbsencesAndPhotos.CreateContext(database, shop.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(before));
            Assert.Equal((0, 0), await SchemaAsync(before));
            Assert.Equal("Luis B.", (await before.OnlineBookingStaff.AsNoTracking().SingleAsync()).Name);
        }

        // Rolling back again does nothing, and forward again adds them again.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(shop.TenantId);
        Assert.Equal((1, 1), await SchemaAsync(forward));
        Assert.Equal(Migration, await LatestMigrationAsync(forward));
    }

    private static async Task AssertStoppedAsync(SqlServerTestDatabase database, Guid tenantId, int absences, int photos)
    {
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50059 }, stopped?.Message ?? "it was not stopped");
        await using var context = database.CreateContext(tenantId);
        Assert.Equal(Migration, await LatestMigrationAsync(context));
        Assert.Equal((1, 1), await SchemaAsync(context));
        Assert.Equal(absences, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [OnlineBookingStaffAbsences]").SingleAsync());
        Assert.Equal(photos, await context.OnlineBookingStaff.CountAsync(s => s.PhotoUrl != null));
    }

    private sealed record Shop(Guid TenantId, Guid StaffUserId, Guid OtherUserId);

    /// <summary>A barbershop that books Luis online, and Ana, on its team but not booked yet; seeded with the version before.</summary>
    private static async Task<Shop> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Barbería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var luis = User("luis", slug);
        var ana = User("ana", slug);

        await using var seed = BeforeStaffAbsencesAndPhotos.CreateContext(database, tenant.Id);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments));
        seed.Users.AddRange(luis, ana);
        seed.TenantUsers.AddRange(new TenantUser(tenant.Id, luis.Id, TenantUserRole.Staff), new TenantUser(tenant.Id, ana.Id, TenantUserRole.Staff));
        seed.OnlineBookingStaff.Add(OnlineBookingStaff.Create(tenant.Id, luis.Id, "Luis B.", true, true, [], null));
        await seed.SaveChangesAsync();
        return new Shop(tenant.Id, luis.Id, ana.Id);
    }

    private static ApplicationUser User(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"{name}-{slug}@demo.com",
        NormalizedUserName = $"{name}-{slug}@DEMO.COM".ToUpperInvariant(),
        Email = $"{name}-{slug}@demo.com",
        NormalizedEmail = $"{name}-{slug}@DEMO.COM".ToUpperInvariant(),
        FirstName = name,
        LastName = "Barbero",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        Role = UserRole.Staff,
        IsActive = true
    };

    /// <summary>Whether the table and the column exist (1) or not (0).</summary>
    private static async Task<(int Table, int Column)> SchemaAsync(ApplicationDbContext context)
    {
        var table = await context.Database.SqlQuery<int>($"""
            SELECT CASE WHEN OBJECT_ID(N'dbo.OnlineBookingStaffAbsences', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]
            """).SingleAsync();
        var column = await context.Database.SqlQuery<int>($"""
            SELECT CASE WHEN COL_LENGTH(N'dbo.OnlineBookingStaff', N'PhotoUrl') IS NULL THEN 0 ELSE 1 END AS [Value]
            """).SingleAsync();
        return (table, column);
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
