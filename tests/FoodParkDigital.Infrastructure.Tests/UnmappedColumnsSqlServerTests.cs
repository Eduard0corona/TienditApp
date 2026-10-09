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
/// Columns the previous version still reads go in two steps: the model stops mapping them, and a later migration drops
/// them. Products.StockQuantity and LowStockThreshold and CustomerProfiles.LoyaltyPoints and LoyaltyTier went both ways:
/// rows saved by this version work on both sides, and rolling the drop back gives the columns back, empty, for the
/// version that reads them. TenantSettings.RequireOpenCashSession is in the first step.
/// </summary>
public class UnmappedColumnsSqlServerTests
{
    private const string StopMappingMigration = "20261006101452_StopMappingStockAndLoyaltyColumns";
    // The last migration whose model maps TenantSettings.RequireOpenCashSession. Two migrations stop mapping it after
    // it, Fase 15's (20261008074715) and PR #47's (20261009041644), and neither changes the database.
    private const string LastMappingRequireOpenCashSession = "20261008072542_AddTabItemCancellations";

    [SqlServerFact]
    public async Task Columns_ShouldStayWhileUnmapped_ThenGo_AndComeBackEmptyOnRollback()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, StopMappingMigration);
        var (tenantId, productId, userId) = await SeedAsync(database);

        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(4, await ColumnsAsync(context));
            // What the previous version reads from a row of this one: no stock captured, and the default 0 points.
            Assert.Equal(1, await EmptyColumnsAsync(context, productId, userId));

            // Captured by the previous version: editing the product here keeps it until the columns go.
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET StockQuantity = 4, LowStockThreshold = 2 WHERE Id = {productId}");
            (await context.Products.SingleAsync(p => p.Id == productId)).SetAvailability(false);
            await context.SaveChangesAsync();
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM Products WHERE Id = {productId} AND StockQuantity = 4 AND LowStockThreshold = 2"));
        }

        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(0, await ColumnsAsync(context));
            await AssertRowsWorkAsync(context, tenantId, productId, userId, "Agua sin columnas");
        }

        // Back to the version that reads them: they return empty (no stock captured, no points), the rows unchanged.
        await MigrateAsync(database, StopMappingMigration);
        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(4, await ColumnsAsync(context));
            Assert.Equal(1, await EmptyColumnsAsync(context, productId, userId));
            await AssertRowsWorkAsync(context, tenantId, productId, userId, "Agua con columnas");
        }

        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(0, await ColumnsAsync(context));
            Assert.Equal(3, await context.Products.CountAsync());
        }
    }

    [SqlServerFact]
    public async Task RequireOpenCashSession_ShouldStayWhileUnmapped_AndNewSettingsTakeItsDefault()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        // Seeded with this model (it maps columns newer than the versions that mapped this one), then taken back there.
        await MigrateAsync(database);
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Tienda", slug, BusinessType.Retail, $"{slug}@demo.com");
        await using (var seed = database.CreateContext(tenant.Id))
        {
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
            await seed.SaveChangesAsync();
        }
        await MigrateAsync(database, LastMappingRequireOpenCashSession);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            // Saved by this version, without the column: the version that reads it gets its default, 0.
            Assert.Equal(1, await CountAsync(seed, $"SELECT COUNT(*) AS [Value] FROM TenantSettings WHERE TenantId = {tenant.Id} AND RequireOpenCashSession = 0"));
            // Turned on by a version that still mapped it.
            await seed.Database.ExecuteSqlInterpolatedAsync($"UPDATE TenantSettings SET RequireOpenCashSession = 1 WHERE TenantId = {tenant.Id}");
        }

        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            // Nothing changed in the database: the column keeps its values and the default new rows take.
            Assert.Equal(1, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns c
                INNER JOIN sys.default_constraints d ON d.[object_id] = c.[default_object_id]
                WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'RequireOpenCashSession'
                    AND c.[is_nullable] = 0 AND d.[definition] = N'(CONVERT([bit],(0)))'
                """));

            // This version changes the settings without touching it, and saves new ones with the default.
            var settings = await context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
            settings.UpdateSalesPolicy(pricesIncludeTax: true, defaultTaxRate: 16m, autoCloseCounterSales: false);
            settings.SetInsufficientStockPolicy(InsufficientStockPolicy.Warn);
            var other = new Tenant("Otra", slug + "b", BusinessType.Retail, $"{slug}b@demo.com");
            context.AddRange(other, new TenantSettings(other.Id, TenantCapability.Retail));
            await context.SaveChangesAsync();

            Assert.Equal(1, await CountAsync(context,
                $"SELECT COUNT(*) AS [Value] FROM TenantSettings WHERE TenantId = {tenant.Id} AND RequireOpenCashSession = 1 AND PricesIncludeTax = 1 AND InsufficientStockPolicy = 1"));
            Assert.Equal(1, await CountAsync(context,
                $"SELECT COUNT(*) AS [Value] FROM TenantSettings WHERE TenantId = {other.Id} AND RequireOpenCashSession = 0"));
        }

        // Back to the version that reads it: nothing to restore, the column and its values never left.
        await MigrateAsync(database, LastMappingRequireOpenCashSession);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(1, await RequireOpenCashSessionColumnsAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM TenantSettings WHERE TenantId = {tenant.Id} AND RequireOpenCashSession = 1"));
        }

        // And forward again: the column stays, and this version reads the settings as it left them.
        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(1, await RequireOpenCashSessionColumnsAsync(context));
            Assert.True((await context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id)).PricesIncludeTax);
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

    /// <summary>The rows of this version read, change and grow the same with or without the columns.</summary>
    private static async Task AssertRowsWorkAsync(ApplicationDbContext context, Guid tenantId, Guid productId, Guid userId, string newProduct)
    {
        var product = await context.Products.SingleAsync(p => p.Id == productId);
        Assert.False(product.IsAvailable);
        var profile = await context.CustomerProfiles.SingleAsync(c => c.UserId == userId);
        profile.UpdateProfile(profile.PhoneNormalized, newProduct, profile.FirstName, profile.LastName);
        context.Products.Add(new Product(tenantId, product.CategoryId, newProduct, null, 12m, "MXN"));
        await context.SaveChangesAsync();

        Assert.Equal(newProduct, (await context.CustomerProfiles.AsNoTracking().SingleAsync(c => c.UserId == userId)).DisplayName);
        Assert.True(await context.Products.AnyAsync(p => p.Name == newProduct));
    }

    /// <summary>How many of the four columns the model stopped mapping the tables still have.</summary>
    private static Task<int> ColumnsAsync(ApplicationDbContext context) =>
        CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns
            WHERE ([object_id] = OBJECT_ID(N'dbo.Products') AND [name] IN (N'StockQuantity', N'LowStockThreshold'))
                OR ([object_id] = OBJECT_ID(N'dbo.CustomerProfiles') AND [name] IN (N'LoyaltyPoints', N'LoyaltyTier'))
            """);

    /// <summary>1 when the product has no stock captured and the profile no points: what a row of this version leaves.</summary>
    private static Task<int> EmptyColumnsAsync(ApplicationDbContext context, Guid productId, Guid userId) =>
        CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM Products p
            CROSS JOIN CustomerProfiles c
            WHERE p.Id = {productId} AND p.StockQuantity IS NULL AND p.LowStockThreshold IS NULL
                AND c.UserId = {userId} AND c.LoyaltyPoints = 0 AND c.LoyaltyTier IS NULL
            """);

    /// <summary>1 while TenantSettings still has the column the model stopped mapping.</summary>
    private static Task<int> RequireOpenCashSessionColumnsAsync(ApplicationDbContext context) =>
        CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'RequireOpenCashSession'");

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
