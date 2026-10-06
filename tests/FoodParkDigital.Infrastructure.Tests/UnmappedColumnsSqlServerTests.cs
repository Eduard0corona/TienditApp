using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
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
/// The model no longer maps Products.StockQuantity and LowStockThreshold nor CustomerProfiles.LoyaltyPoints and
/// LoyaltyTier, but the migrations keep those columns while the previous version, which still reads them, may run:
/// this version saves rows without them, leaving what that version reads, and never touches what was captured before.
/// </summary>
public class UnmappedColumnsSqlServerTests
{
    private const string LastMigrationOfThePreviousVersion = "20261006054141_DropPlainRefreshTokenColumns";

    [SqlServerFact]
    public async Task Rows_ShouldSaveWithoutTheUnmappedColumns_WhichStayWithWhatWasCapturedBefore()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var (tenantId, productId, userId) = await SeedAsync(database);

        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(4, await ColumnsAsync(context));
            // What the previous version reads from a row of this one: no stock captured, and the default 0 points.
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM Products WHERE Id = {productId} AND StockQuantity IS NULL AND LowStockThreshold IS NULL"));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM CustomerProfiles WHERE UserId = {userId} AND LoyaltyPoints = 0 AND LoyaltyTier IS NULL"));

            // Captured by the previous version: editing the rows here keeps it.
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET StockQuantity = 4, LowStockThreshold = 2 WHERE Id = {productId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE CustomerProfiles SET LoyaltyPoints = 7, LoyaltyTier = N'Oro' WHERE UserId = {userId}");
            (await context.Products.SingleAsync(p => p.Id == productId)).SetAvailability(false);
            (await context.CustomerProfiles.SingleAsync(c => c.UserId == userId)).UpdateProfile("5512345678", "Ana", "Ana", "Pérez");
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenantId))
        {
            Assert.False((await context.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).IsAvailable);
            Assert.Equal(1, await CapturedAsync(context, productId, userId));
        }

        // Rolling back to the previous version's migrations leaves the tables as they are.
        await MigrateAsync(database, LastMigrationOfThePreviousVersion);
        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(4, await ColumnsAsync(context));
            Assert.Equal(1, await CapturedAsync(context, productId, userId));
        }
    }

    /// <summary>Applies the migrations up to <paramref name="target"/> (all of them without one), as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string? target = null)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task<(Guid TenantId, Guid ProductId, Guid UserId)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Tienda", slug, BusinessType.Retail, $"{slug}@demo.com");
        var category = new Category(tenant.Id, "General", null);
        var product = new Product(tenant.Id, category.Id, "Refresco", null, 20m, "MXN");
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "cliente@demo.com",
            NormalizedUserName = "CLIENTE@DEMO.COM",
            Email = "cliente@demo.com",
            NormalizedEmail = "CLIENTE@DEMO.COM",
            FirstName = "Ana",
            LastName = "Pérez",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Customer,
            IsActive = true
        };

        await using var context = database.CreateContext(tenant.Id);
        context.AddRange(tenant, category, product);
        context.Users.Add(user);
        context.CustomerProfiles.Add(new CustomerProfile(user.Id, "5512345678", "Ana", "Ana", null));
        await context.SaveChangesAsync();
        return (tenant.Id, product.Id, user.Id);
    }

    /// <summary>How many of the four columns the model stopped mapping the tables still have.</summary>
    private static Task<int> ColumnsAsync(ApplicationDbContext context) =>
        CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns
            WHERE ([object_id] = OBJECT_ID(N'dbo.Products') AND [name] IN (N'StockQuantity', N'LowStockThreshold'))
                OR ([object_id] = OBJECT_ID(N'dbo.CustomerProfiles') AND [name] IN (N'LoyaltyPoints', N'LoyaltyTier'))
            """);

    /// <summary>1 when both rows keep the values captured by the previous version.</summary>
    private static Task<int> CapturedAsync(ApplicationDbContext context, Guid productId, Guid userId) =>
        CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM Products p
            CROSS JOIN CustomerProfiles c
            WHERE p.Id = {productId} AND p.StockQuantity = 4 AND p.LowStockThreshold = 2
                AND c.UserId = {userId} AND c.LoyaltyPoints = 7 AND c.LoyaltyTier = N'Oro'
            """);

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
