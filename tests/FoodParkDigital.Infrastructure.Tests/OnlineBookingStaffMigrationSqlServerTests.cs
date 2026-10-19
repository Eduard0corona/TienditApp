using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
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
/// AddOnlineBookingStaff (Fase 24J, ADR 0032): one row per team member and business, with their services and their own
/// hours stored as the business's are; a service leaves the list when it is deleted; and rolling back stops with error
/// 50025 once a business set who attends, and otherwise drops only the two tables.
/// </summary>
public class OnlineBookingStaffMigrationSqlServerTests
{
    // The migration right before AddOnlineBookingStaff, where its rollback.sql takes the database.
    private const string BeforeOnlineBookingStaff = "20261019015524_AddOnlinePayments";
    private const string Migration = "20261019150436_AddOnlineBookingStaff";

    [SqlServerFact]
    public async Task Tables_ShouldKeep_OnePersonPerBusiness_TheirServicesAndTheirHours()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);

        await using (var check = database.CreateContext())
        {
            var stored = await check.OnlineBookingStaff.IgnoreQueryFilters().Include(s => s.Services).SingleAsync();
            Assert.Equal(("Ana R. 💈", true, false), (stored.Name, stored.Enabled, stored.AllServices));
            Assert.Equal([seed.ServiceId], stored.Services.Select(s => s.ProductId));
            Assert.Equal(new TimeOnly(10, 0), stored.WeeklyHours!.Intervals.Single().Opens);

            // Changing the services keeps the row that stays and adds the other.
            var color = new Product(seed.TenantId, seed.CategoryId, "Tinte", null, 300m, "MXN");
            check.Products.Add(color);
            stored.Change("Ana R.", true, false, [seed.ServiceId, color.Id], null);
            await check.SaveChangesAsync();
        }

        await using (var check = database.CreateContext())
        {
            var stored = await check.OnlineBookingStaff.IgnoreQueryFilters().Include(s => s.Services).SingleAsync();
            Assert.Equal(2, stored.Services.Count);
            Assert.Null(stored.WeeklyHours);

            // Leaving a service out deletes its row; the one that stays is kept.
            stored.Change("Ana R.", true, false, [seed.ServiceId], null);
            await check.SaveChangesAsync();
        }

        await using (var check = database.CreateContext())
        {
            var stored = await check.OnlineBookingStaff.IgnoreQueryFilters().Include(s => s.Services).SingleAsync();
            Assert.Equal([seed.ServiceId], stored.Services.Select(s => s.ProductId));
            Assert.Equal(1, await CountAsync(check, $"SELECT COUNT(*) AS [Value] FROM [OnlineBookingStaffServices]"));
        }

        var twice = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            INSERT INTO [OnlineBookingStaff] ([Id], [TenantId], [UserId], [Name], [Enabled], [AllServices], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {seed.TenantId}, {seed.UserId}, N'Ana', 1, 1, SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, twice.Number);
        Assert.Contains("IX_OnlineBookingStaff_TenantId_UserId", twice.Message);

        // Deleting the service deletes it from the person's list, and the person stays.
        await ExecuteAsync(database, $"DELETE FROM [Products] WHERE [Id] = {seed.ServiceId}");
        await using var after = database.CreateContext();
        Assert.Equal(0, await CountAsync(after, $"SELECT COUNT(*) AS [Value] FROM [OnlineBookingStaffServices]"));
        Assert.Equal(1, await CountAsync(after, $"SELECT COUNT(*) AS [Value] FROM [OnlineBookingStaff]"));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceABusinessSetWhoAttends_AndThenDropOnlyTheTables()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        await SeedAsync(database);

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeOnlineBookingStaff));
        Assert.Equal(50025, error.Number);
        Assert.Contains("who takes their online appointments", error.Message);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OnlineBookingStaffServices]"));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [OnlineBookingStaff]");
        }

        await MigrateAsync(database, BeforeOnlineBookingStaff);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeOnlineBookingStaff, await LatestMigrationAsync(context));
            Assert.Equal((0, 1, 1, 1), (
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN (N'OnlineBookingStaff', N'OnlineBookingStaffServices')
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [AspNetUsers]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Tenants]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Products]")));
        }

        // Rolling back again does nothing, and forward again the tables are back.
        await MigrateAsync(database, BeforeOnlineBookingStaff);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext();
        Assert.Equal(2, await CountAsync(forward, $"""
            SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN (N'OnlineBookingStaff', N'OnlineBookingStaffServices')
            """));
    }

    private sealed record Seed(Guid TenantId, Guid UserId, Guid CategoryId, Guid ServiceId);

    /// <summary>A barbershop with a service and a team member who attends it online in their own hours.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var slug = $"barberia-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Barbería Norte", slug, BusinessType.Service, $"{slug}@demo.com");
        var member = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{slug}@demo.com",
            NormalizedUserName = $"{slug}@DEMO.COM",
            Email = $"{slug}@demo.com",
            NormalizedEmail = $"{slug}@DEMO.COM",
            FirstName = "Ana",
            LastName = "Ruiz",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.TenantAdmin,
            IsActive = true
        };
        var category = new Category(tenant.Id, "Servicios", null);
        var haircut = new Product(tenant.Id, category.Id, "Corte", null, 150m, "MXN");
        var hours = OpeningHours.Create([new OpeningInterval(DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(18, 0))]);

        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Appointments));
        context.Users.Add(member);
        context.Categories.Add(category);
        context.Products.Add(haircut);
        context.OnlineBookingStaff.Add(OnlineBookingStaff.Create(tenant.Id, member.Id, "Ana R. 💈", true, false, [haircut.Id], hours));
        await context.SaveChangesAsync();
        return new Seed(tenant.Id, member.Id, category.Id, haircut.Id);
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
