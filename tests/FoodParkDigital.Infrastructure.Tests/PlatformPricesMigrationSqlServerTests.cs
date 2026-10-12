using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddPlatformPrices (Fase 22F-2) only adds two tables, so the version deployed before keeps selling and editing its
/// catalog over them: the database takes the prices of what it deletes with it. Rolling it back stops with error 50009
/// while there is a price saved (no activity entry keeps them), and otherwise drops only what it added.
/// </summary>
public class PlatformPricesMigrationSqlServerTests
{
    // The migration right before AddPlatformPrices, where its rollback.sql takes the database.
    private const string BeforePrices = "20261012005540_AddDeliveryPlatformOrders";
    private const string Migration = "20261012112823_AddPlatformPrices";

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepEditingItsCatalog_AndTheDatabaseTakesThePricesOfWhatItDeletes()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var tenant = await SeedAsync(database);
        var burger = NewBurger(tenant.Id);

        // The version deployed before creates the product, as it always did.
        await using (var context = CreateVersionBeforePrices(database, tenant.Id))
        {
            context.Categories.Add(new Category(tenant.Id, "Hamburguesas", null));
            await context.SaveChangesAsync();
        }
        await using (var context = CreateVersionBeforePrices(database, tenant.Id))
        {
            var category = await context.Categories.SingleAsync();
            burger.MoveToCategory(category.Id);
            context.Products.Add(burger);
            await context.SaveChangesAsync();
        }

        // This version sets its prices in Rappi.
        await using (var context = database.CreateContext(tenant.Id))
        {
            var product = await LoadAsync(context, burger.Id);
            product.SetPlatformPrice(OrderChannel.Rappi, 125m);
            product.SetOptionPlatformPrice(Option(product, "Doble").Id, OrderChannel.Rappi, 38m);
            product.SetOptionPlatformPrice(Option(product, "Sencilla").Id, OrderChannel.Rappi, 0m);
            await context.SaveChangesAsync();
        }

        // The version before still reads the catalog, edits the options (leaving «Doble» out) and deletes the product.
        await using (var context = CreateVersionBeforePrices(database, tenant.Id))
        {
            var product = await context.Products.Include(p => p.OptionGroups).ThenInclude(g => g.Options).SingleAsync();
            Assert.Equal(99m, product.PriceAmount);
            var size = product.OptionGroups.Single();
            product.SetOptionGroups(
            [
                new ProductOptionGroupDefinition(size.Id, size.Name, 1, 1, [new ProductOptionDefinition(Option(product, "Sencilla").Id, "Sencilla", 0m)])
            ]);
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(1, await context.ProductPlatformPrices.CountAsync());
            Assert.Equal(new[] { Option(burger, "Sencilla").Id }, await context.ProductOptionPlatformPrices.Select(x => x.OptionId).ToListAsync());
        }

        await using (var context = CreateVersionBeforePrices(database, tenant.Id))
        {
            context.Products.Remove(await context.Products.SingleAsync());
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM ProductPlatformPrices"));
            Assert.Equal(0, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM ProductOptionPlatformPrices"));
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileThereArePrices_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var tenant = await SeedAsync(database);
        var burger = NewBurger(tenant.Id);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var category = new Category(tenant.Id, "Hamburguesas", null);
            burger.MoveToCategory(category.Id);
            burger.SetPlatformPrice(OrderChannel.UberEats, 129m);
            burger.SetOptionPlatformPrice(Option(burger, "Doble").Id, OrderChannel.UberEats, 38m);
            context.AddRange(category, burger);
            await context.SaveChangesAsync();
        }

        // A price of a product or of an option stops the rollback on its own, and nothing changes.
        await AssertRollbackStoppedAsync(database, productPrices: 1, optionPrices: 1);
        await ExecuteAsync(database, $"DELETE FROM ProductOptionPlatformPrices");
        await AssertRollbackStoppedAsync(database, productPrices: 1, optionPrices: 0);
        await ExecuteAsync(database, $"DELETE FROM ProductPlatformPrices");
        await ExecuteAsync(database, $"""
            INSERT INTO ProductOptionPlatformPrices (Id, TenantId, OptionId, Channel, PriceDelta)
            VALUES ({Guid.NewGuid()}, {tenant.Id}, {Option(burger, "Doble").Id}, 3, 38)
            """);
        await AssertRollbackStoppedAsync(database, productPrices: 0, optionPrices: 1);
        await ExecuteAsync(database, $"DELETE FROM ProductOptionPlatformPrices");

        // Without prices, the rollback drops the two tables and nothing else.
        await MigrateAsync(database, BeforePrices);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.objects WHERE [name] IN (N'ProductPlatformPrices', N'ProductOptionPlatformPrices')
                """));
            Assert.Equal(BeforePrices, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM Products"));
            Assert.Equal(2, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM ProductOptions"));
        }
        // Rolling back again does nothing.
        await MigrateAsync(database, BeforePrices);

        // Forward again: the tables start empty, and the orders of the platforms sell at the menu's prices.
        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var product = await LoadAsync(context, burger.Id);
            Assert.Empty(product.PlatformPrices);
            Assert.Equal(99m, product.PriceIn(OrderChannel.UberEats));
            Assert.Equal(Migration, await LatestMigrationAsync(context));
        }
    }

    private static async Task AssertRollbackStoppedAsync(SqlServerTestDatabase database, int productPrices, int optionPrices)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforePrices));
        Assert.Equal(50009, error.Number);
        Assert.Contains("prices of delivery platforms", error.Message);

        await using var context = database.CreateContext();
        Assert.Equal(Migration, await LatestMigrationAsync(context));
        Assert.Equal(productPrices, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM ProductPlatformPrices"));
        Assert.Equal(optionPrices, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM ProductOptionPlatformPrices"));
    }

    private static async Task<Tenant> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Cocina sin local", slug, BusinessType.Food, $"{slug}@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A burger of $99 in the menu, «Sencilla» or «Doble» (+$30); its category is set before saving it.</summary>
    private static Product NewBurger(Guid tenantId)
    {
        var burger = new Product(tenantId, Guid.NewGuid(), "Hamburguesa", null, 99m, "MXN");
        burger.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Tamaño", 1, 1,
            [
                new ProductOptionDefinition(null, "Sencilla", 0m),
                new ProductOptionDefinition(null, "Doble", 30m)
            ])
        ]);
        return burger;
    }

    private static Task<Product> LoadAsync(ApplicationDbContext context, Guid productId)
        => context.Products
            .Include(p => p.PlatformPrices)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Options).ThenInclude(o => o.PlatformPrices)
            .SingleAsync(p => p.Id == productId);

    private static ProductOption Option(Product product, string name)
        => product.OptionGroups.SelectMany(g => g.Options).Single(o => o.Name == name);

    private static VersionBeforePricesContext CreateVersionBeforePrices(SqlServerTestDatabase database, Guid tenantId)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new VersionBeforePricesContext(options, tenant, mediator));

    /// <summary>The app as deployed before Fase 22F-2: no prices of the platforms.</summary>
    private sealed class VersionBeforePricesContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Product>().Ignore(p => p.PlatformPrices);
            modelBuilder.Entity<ProductOption>().Ignore(o => o.PlatformPrices);
            modelBuilder.Ignore<ProductPlatformPrice>();
            modelBuilder.Ignore<ProductOptionPlatformPrice>();
        }
    }

    /// <summary>Applies the migrations up to <paramref name="target"/> (all of them without one), as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string? target = null)
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
