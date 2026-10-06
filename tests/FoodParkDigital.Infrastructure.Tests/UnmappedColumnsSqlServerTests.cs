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
/// Products.StockQuantity and LowStockThreshold and CustomerProfiles.LoyaltyPoints and LoyaltyTier go in two steps: the
/// model stops mapping them while the previous version still reads them, and the next migration drops them. Rows saved
/// by this version work on both sides, and rolling the drop back gives the columns back, empty, for the version that
/// reads them.
/// </summary>
public class UnmappedColumnsSqlServerTests
{
    private const string StopMappingMigration = "20261006101452_StopMappingStockAndLoyaltyColumns";

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

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
