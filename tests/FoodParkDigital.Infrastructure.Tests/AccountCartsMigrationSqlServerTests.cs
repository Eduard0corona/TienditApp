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
/// AddAccountCartsAndPostalCode (Fase 31J, ADR 0090): a table of the carts customer accounts leave in the stores, one per
/// account and store, that goes with the account, and a column with the account's postal code. The version deployed before
/// it keeps signing customers up on the migrated database. Rolling back stops with error 50060, without changes, once an
/// account left a cart or a postal code; without any, it drops only the table and the column.
/// </summary>
public class AccountCartsMigrationSqlServerTests
{
    // The migration right before it.
    private const string BeforeMigration = "20261111060433_AddOnlineBookingStaffAbsencesAndPhotos";
    private const string Migration = "20261112154235_AddAccountCartsAndPostalCode";
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task TheDatabase_ShouldKeepOneCartPerStore_WithItsLines_AndDeleteItWithTheAccount()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);
        var option = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.AccountCarts.Add(AccountCart.Create(shop.AccountId, shop.TenantId,
                [new AccountCartLine(shop.ProductId, [option], 2, "sin cebolla ñ")], reorderOf: 3, skipped: null, Now));
            (await context.Users.SingleAsync(u => u.Id == shop.AccountId)).PostalCode = "72000";
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var cart = await context.AccountCarts.AsNoTracking().SingleAsync();
            Assert.Equal([new AccountCartLine(shop.ProductId, [option], 2, "sin cebolla ñ")], cart.Lines);
            Assert.Equal((3, Now), (cart.ReorderOf, cart.SavedAt));
            Assert.Equal("72000", (await context.Users.AsNoTracking().SingleAsync(u => u.Id == shop.AccountId)).PostalCode);

            // One per account and store.
            context.AccountCarts.Add(AccountCart.Create(shop.AccountId, shop.TenantId, [new AccountCartLine(shop.ProductId, [], 1, null)], null, null, Now));
            var twice = await Record.ExceptionAsync(() => context.SaveChangesAsync());
            Assert.Contains("IX_AccountCarts_UserId_BusinessId", twice?.InnerException?.Message);
        }

        // The cart goes with the account.
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlAsync($"DELETE FROM [AspNetUsers] WHERE [Id] = {shop.AccountId}");
        await using var check = database.CreateContext();
        Assert.Equal(0, await check.AccountCarts.CountAsync());
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSigningCustomersUp_AndReadingAccounts_OnTheMigratedDatabase()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);
        await using (var context = database.CreateContext())
        {
            (await context.Users.SingleAsync(u => u.Id == shop.AccountId)).PostalCode = "72000";
            await context.SaveChangesAsync();
        }

        await using (var before = BeforeAccountCarts.CreateContext(database))
        {
            before.Users.Add(Customer("beto"));
            var mariana = await before.Users.SingleAsync(u => u.Id == shop.AccountId);
            mariana.FirstName = "Mariana Isabel";
            await before.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var accounts = await check.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
        Assert.Equal([("beto@demo.com", null), ("mariana@demo.com", "72000")], accounts.Select(u => (u.Email, u.PostalCode)));
        Assert.Equal("Mariana Isabel", accounts[1].FirstName);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceACartOrAPostalCodeWasLeft_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(database);
        await MigrateAsync(database, Migration);

        // A cart alone stops it.
        await using (var context = database.CreateContext())
        {
            context.AccountCarts.Add(AccountCart.Create(shop.AccountId, shop.TenantId, [new AccountCartLine(shop.ProductId, [], 1, null)], null, null, Now));
            await context.SaveChangesAsync();
        }
        await AssertStoppedAsync(database, carts: 1, postalCodes: 0);

        // A postal code alone stops it too.
        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlAsync($"DELETE FROM [AccountCarts]");
            await context.Database.ExecuteSqlAsync($"UPDATE [AspNetUsers] SET [PostalCode] = '72000' WHERE [Id] = {shop.AccountId}");
        }
        await AssertStoppedAsync(database, carts: 0, postalCodes: 1);

        // Without any, as query 3 of verify.sql leaves it: it drops the table and the column, and the accounts stay.
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlAsync($"UPDATE [AspNetUsers] SET [PostalCode] = NULL");
        await MigrateAsync(database, BeforeMigration);
        await using (var before = BeforeAccountCarts.CreateContext(database))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(before));
            Assert.Equal((0, 0), await SchemaAsync(before));
            Assert.Equal("mariana@demo.com", (await before.Users.AsNoTracking().SingleAsync()).Email);
        }

        // Rolling back again does nothing, and forward again adds them again.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext();
        Assert.Equal((1, 1), await SchemaAsync(forward));
        Assert.Equal(Migration, await LatestMigrationAsync(forward));
    }

    private static async Task AssertStoppedAsync(SqlServerTestDatabase database, int carts, int postalCodes)
    {
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50060 }, stopped?.Message ?? "it was not stopped");
        await using var context = database.CreateContext();
        Assert.Equal(Migration, await LatestMigrationAsync(context));
        Assert.Equal((1, 1), await SchemaAsync(context));
        Assert.Equal(carts, await context.AccountCarts.CountAsync());
        Assert.Equal(postalCodes, await context.Users.CountAsync(u => u.PostalCode != null));
    }

    private sealed record Shop(Guid TenantId, Guid ProductId, Guid AccountId);

    /// <summary>A taquería with a product, and a customer account; seeded with the version before.</summary>
    private static async Task<Shop> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Taquería {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
        var category = new FoodParkDigital.Domain.Catalog.Entities.Category(tenant.Id, "Tacos", null);
        var product = new FoodParkDigital.Domain.Catalog.Entities.Product(tenant.Id, category.Id, "Orden de tacos", null, 120m, "MXN");
        var mariana = Customer("mariana");

        await using var seed = BeforeAccountCarts.CreateContext(database, tenant.Id);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food), category, product);
        seed.Users.Add(mariana);
        await seed.SaveChangesAsync();
        return new Shop(tenant.Id, product.Id, mariana.Id);
    }

    private static ApplicationUser Customer(string name) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"{name}@demo.com",
        NormalizedUserName = $"{name}@DEMO.COM".ToUpperInvariant(),
        Email = $"{name}@demo.com",
        NormalizedEmail = $"{name}@DEMO.COM".ToUpperInvariant(),
        FirstName = name,
        LastName = "Cliente",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        Role = UserRole.Customer,
        IsActive = true
    };

    /// <summary>Whether the table and the column exist (1) or not (0).</summary>
    private static async Task<(int Table, int Column)> SchemaAsync(ApplicationDbContext context)
    {
        var table = await context.Database.SqlQuery<int>($"""
            SELECT CASE WHEN OBJECT_ID(N'dbo.AccountCarts', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]
            """).SingleAsync();
        var column = await context.Database.SqlQuery<int>($"""
            SELECT CASE WHEN COL_LENGTH(N'dbo.AspNetUsers', N'PostalCode') IS NULL THEN 0 ELSE 1 END AS [Value]
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
