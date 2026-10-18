using FoodParkDigital.Domain.Customers.Entities;
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
/// AddFavoriteBusinesses (Fase 24F, ADR 0028): one favorite per business and account, gone with the account, never in the
/// way of a business; and rolling back stops with error 50021 once an account kept one, and otherwise drops only the table.
/// </summary>
public class FavoriteBusinessesMigrationSqlServerTests
{
    // The migration right before AddFavoriteBusinesses, where its rollback.sql takes the database.
    private const string BeforeFavorites = "20261018032200_AddBusinessCover";
    private const string Migration = "20261018081803_AddFavoriteBusinesses";

    [SqlServerFact]
    public async Task Table_ShouldKeepOneFavoritePerBusiness_AndTheFavoritesGoWithTheAccount()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (userId, businessId) = await SeedAccountWithAFavoriteAsync(database);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            INSERT INTO [FavoriteBusinesses] ([Id], [UserId], [BusinessId], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {userId}, {businessId}, SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("IX_FavoriteBusinesses_UserId_BusinessId", duplicate.Message);

        // A business is never deleted; its favorites would not let it.
        var deleteBusiness = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database,
            $"DELETE FROM [Tenants] WHERE [Id] = {businessId}"));
        Assert.Equal(547, deleteBusiness.Number);

        await ExecuteAsync(database, $"DELETE FROM [AspNetUsers] WHERE [Id] = {userId}");
        await using var check = database.CreateContext();
        Assert.Equal(0, await CountAsync(check, $"SELECT COUNT(*) AS [Value] FROM [FavoriteBusinesses]"));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAnAccountKeptAFavorite_AndThenDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        await SeedAccountWithAFavoriteAsync(database);

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeFavorites));
        Assert.Equal(50021, error.Number);
        Assert.Contains("Customer accounts saved favorite businesses", error.Message);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [FavoriteBusinesses]"));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [FavoriteBusinesses]");
        }

        await MigrateAsync(database, BeforeFavorites);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeFavorites, await LatestMigrationAsync(context));
            Assert.Equal((0, 1, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'FavoriteBusinesses'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [AspNetUsers]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Tenants]")));
        }

        // Rolling back again does nothing, and forward again the table is back.
        await MigrateAsync(database, BeforeFavorites);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext();
        Assert.Equal(1, await CountAsync(forward, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'FavoriteBusinesses'"));
    }

    /// <summary>A customer account that keeps a business among its favorites.</summary>
    private static async Task<(Guid UserId, Guid BusinessId)> SeedAccountWithAFavoriteAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var slug = $"cafe-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Café Luna", slug, BusinessType.Food, $"{slug}@demo.com");
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "cliente@demo.com",
            NormalizedUserName = "CLIENTE@DEMO.COM",
            Email = "cliente@demo.com",
            NormalizedEmail = "CLIENTE@DEMO.COM",
            FirstName = "María Fernanda",
            LastName = "López",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Customer,
            IsActive = true
        };
        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        context.Users.Add(user);
        context.FavoriteBusinesses.Add(FavoriteBusiness.Create(user.Id, tenant.Id, favoritesKept: 0));
        await context.SaveChangesAsync();
        return (user.Id, tenant.Id);
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
