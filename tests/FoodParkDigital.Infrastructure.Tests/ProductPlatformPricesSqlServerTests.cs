using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The prices of products and options in the apps of the delivery platforms (Fase 22F-2): their checks keep every row one
/// the domain could write, one price per product (or option) and platform, the prices go with their product or option,
/// keep their cents and stay in their business.
/// </summary>
public class ProductPlatformPricesSqlServerTests
{
    [SqlServerFact]
    public async Task Tables_ShouldHaveTheirShape()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Equal(
            new[] { "ProductOptionPlatformPrices.PriceDelta decimal 18 2", "ProductPlatformPrices.Price decimal 18 2" },
            await context.Database.SqlQuery<string>($"""
                SELECT OBJECT_NAME(c.[object_id]) + N'.' + c.[name] + N' ' + t.[name] + N' ' + CAST(c.[precision] AS nvarchar(3))
                    + N' ' + CAST(c.[scale] AS nvarchar(3)) AS [Value]
                FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
                WHERE c.[object_id] IN (OBJECT_ID(N'dbo.ProductPlatformPrices'), OBJECT_ID(N'dbo.ProductOptionPlatformPrices'))
                    AND t.[name] = N'decimal' AND c.[is_nullable] = 0
                ORDER BY [Value]
                """).ToListAsync());
        Assert.Equal(
            new[]
            {
                "CK_ProductOptionPlatformPrices_Channel", "CK_ProductOptionPlatformPrices_PriceDelta",
                "CK_ProductPlatformPrices_Channel", "CK_ProductPlatformPrices_Price"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] AS [Value] FROM sys.check_constraints
                WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.ProductPlatformPrices'), OBJECT_ID(N'dbo.ProductOptionPlatformPrices'))
                ORDER BY [name]
                """).ToListAsync());
        Assert.Equal(
            new[]
            {
                "IX_ProductOptionPlatformPrices_OptionId_Channel 1", "IX_ProductOptionPlatformPrices_TenantId_Channel 0",
                "IX_ProductPlatformPrices_ProductId_Channel 1", "IX_ProductPlatformPrices_TenantId_Channel 0"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] + N' ' + CAST([is_unique] AS nvarchar(1)) AS [Value] FROM sys.indexes
                WHERE [object_id] IN (OBJECT_ID(N'dbo.ProductPlatformPrices'), OBJECT_ID(N'dbo.ProductOptionPlatformPrices'))
                    AND [name] LIKE N'IX[_]%'
                ORDER BY [name]
                """).ToListAsync());
        // Each price goes with its product or its option.
        Assert.Equal(
            new[]
            {
                "FK_ProductOptionPlatformPrices_ProductOptions_OptionId CASCADE",
                "FK_ProductPlatformPrices_Products_ProductId CASCADE"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] COLLATE DATABASE_DEFAULT + N' ' + [delete_referential_action_desc] COLLATE DATABASE_DEFAULT AS [Value] FROM sys.foreign_keys
                WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.ProductPlatformPrices'), OBJECT_ID(N'dbo.ProductOptionPlatformPrices'))
                ORDER BY [name]
                """).ToListAsync());
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuseRowsTheDomainCannotWrite()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var burger = await SeedBurgerAsync(database, tenant.Id);
        var doble = Option(burger, "Doble");

        await using var context = database.CreateContext(tenant.Id);
        await RefusedAsync("CK_ProductPlatformPrices_Channel", () => InsertPriceAsync(context, tenant.Id, burger.Id, channel: 2));
        await RefusedAsync("CK_ProductPlatformPrices_Channel", () => InsertPriceAsync(context, tenant.Id, burger.Id, channel: 6));
        await RefusedAsync("CK_ProductPlatformPrices_Price", () => InsertPriceAsync(context, tenant.Id, burger.Id, price: 0m));
        await RefusedAsync("CK_ProductPlatformPrices_Price", () => InsertPriceAsync(context, tenant.Id, burger.Id, price: -0.01m));
        await RefusedAsync("CK_ProductOptionPlatformPrices_Channel", () => InsertOptionPriceAsync(context, tenant.Id, doble.Id, channel: 0));
        await RefusedAsync("CK_ProductOptionPlatformPrices_PriceDelta", () => InsertOptionPriceAsync(context, tenant.Id, doble.Id, delta: 100_000.01m));
        await RefusedAsync("CK_ProductOptionPlatformPrices_PriceDelta", () => InsertOptionPriceAsync(context, tenant.Id, doble.Id, delta: -100_000.01m));

        // What the domain writes goes in, a cheaper variant included; a second price for the same platform does not.
        await InsertPriceAsync(context, tenant.Id, burger.Id, channel: 3, price: 0.01m);
        await InsertOptionPriceAsync(context, tenant.Id, doble.Id, channel: 5, delta: -100_000m);
        var product = await Assert.ThrowsAsync<SqlException>(() => InsertPriceAsync(context, tenant.Id, burger.Id, channel: 3, price: 125m));
        var option = await Assert.ThrowsAsync<SqlException>(() => InsertOptionPriceAsync(context, tenant.Id, doble.Id, channel: 5, delta: 1m));
        Assert.Contains("IX_ProductPlatformPrices_ProductId_Channel", product.Message);
        Assert.Contains("IX_ProductOptionPlatformPrices_OptionId_Channel", option.Message);
    }

    [SqlServerFact]
    public async Task Prices_ShouldKeepTheirCents_AndChangeOrGoAway()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var burger = await SeedBurgerAsync(database, tenant.Id);

        await using (var context = database.CreateContext(tenant.Id))
        {
            var product = await LoadAsync(context, burger.Id);
            product.SetPlatformPrice(OrderChannel.Rappi, 125.55m);
            product.SetPlatformPrice(OrderChannel.DidiFood, 119m);
            product.SetOptionPlatformPrice(Option(product, "Doble").Id, OrderChannel.Rappi, 37.45m);
            product.SetOptionPlatformPrice(Option(product, "Sencilla").Id, OrderChannel.Rappi, -7.25m);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var product = await LoadAsync(context, burger.Id);
            Assert.Equal(125.55m, product.PriceIn(OrderChannel.Rappi));
            Assert.Equal(119m, product.PriceIn(OrderChannel.DidiFood));
            Assert.Equal(99m, product.PriceIn(OrderChannel.UberEats));
            Assert.Equal(37.45m, Option(product, "Doble").PriceDeltaIn(OrderChannel.Rappi));
            Assert.Equal(-7.25m, Option(product, "Sencilla").PriceDeltaIn(OrderChannel.Rappi));

            // Another price replaces the row; null takes it out.
            product.SetPlatformPrice(OrderChannel.Rappi, 129m);
            product.SetPlatformPrice(OrderChannel.DidiFood, null);
            product.SetOptionPlatformPrice(Option(product, "Sencilla").Id, OrderChannel.Rappi, null);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(
                new[] { (OrderChannel.Rappi, 129m) },
                (await context.ProductPlatformPrices.AsNoTracking().ToListAsync()).Select(x => (x.Channel, x.Price)));
            Assert.Equal(
                new[] { (Option(burger, "Doble").Id, 37.45m) },
                (await context.ProductOptionPlatformPrices.AsNoTracking().ToListAsync()).Select(x => (x.OptionId, x.PriceDelta)));
        }
    }

    [SqlServerFact]
    public async Task Prices_ShouldGoWithTheOptionTheEditorRemoves_AndWithTheProduct()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var burger = await SeedBurgerAsync(database, tenant.Id);
        var doble = Option(burger, "Doble");
        var sencilla = Option(burger, "Sencilla");

        await using (var context = database.CreateContext(tenant.Id))
        {
            var product = await LoadAsync(context, burger.Id);
            product.SetPlatformPrice(OrderChannel.Rappi, 125m);
            product.SetOptionPlatformPrice(doble.Id, OrderChannel.Rappi, 38m);
            product.SetOptionPlatformPrice(sencilla.Id, OrderChannel.Rappi, 0m);
            await context.SaveChangesAsync();
        }

        // The editor of options keeps «Sencilla» (by its id) and leaves «Doble» out.
        await using (var context = database.CreateContext(tenant.Id))
        {
            var product = await LoadAsync(context, burger.Id);
            var size = product.OptionGroups.Single();
            product.SetOptionGroups(
            [
                new ProductOptionGroupDefinition(size.Id, size.Name, 1, 1, [new ProductOptionDefinition(sencilla.Id, "Sencilla", 0m)])
            ]);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(new[] { sencilla.Id }, await context.ProductOptionPlatformPrices.Select(x => x.OptionId).ToListAsync());
            Assert.Equal(1, await context.ProductPlatformPrices.CountAsync());

            context.Products.Remove(await LoadAsync(context, burger.Id));
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(0, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM ProductPlatformPrices"));
            Assert.Equal(0, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM ProductOptionPlatformPrices"));
        }
    }

    [SqlServerFact]
    public async Task Prices_ShouldStayInTheirBusiness()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        var burger = await SeedBurgerAsync(database, tenant.Id);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var product = await LoadAsync(context, burger.Id);
            product.SetPlatformPrice(OrderChannel.Rappi, 125m);
            product.SetOptionPlatformPrice(Option(product, "Doble").Id, OrderChannel.Rappi, 38m);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(other.Id))
        {
            Assert.Equal(0, await context.ProductPlatformPrices.CountAsync());
            Assert.Equal(0, await context.ProductOptionPlatformPrices.CountAsync());
            Assert.Null(await context.Products.Include(p => p.PlatformPrices).FirstOrDefaultAsync(p => p.Id == burger.Id));
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var price = await context.ProductPlatformPrices.SingleAsync();
            var optionPrice = await context.ProductOptionPlatformPrices.SingleAsync();
            Assert.Equal((tenant.Id, burger.Id), (price.TenantId, price.ProductId));
            Assert.Equal(tenant.Id, optionPrice.TenantId);
        }
    }

    private static async Task<(Tenant Tenant, Tenant Other)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Cocina sin local", slug, BusinessType.Food, $"{slug}@demo.com");
        var other = new Tenant("Taquería", slug + "b", BusinessType.Food, $"{slug}b@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, TenantCapability.Food), new TenantSettings(other.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    /// <summary>A burger of $99 in the menu, «Sencilla» or «Doble» (+$30).</summary>
    private static async Task<Product> SeedBurgerAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        var category = new Category(tenantId, "Hamburguesas", null);
        var burger = new Product(tenantId, category.Id, "Hamburguesa", null, 99m, "MXN");
        burger.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Tamaño", 1, 1,
            [
                new ProductOptionDefinition(null, "Sencilla", 0m),
                new ProductOptionDefinition(null, "Doble", 30m)
            ])
        ]);
        await using var seed = database.CreateContext(tenantId);
        seed.AddRange(category, burger);
        await seed.SaveChangesAsync();
        return burger;
    }

    private static Task<Product> LoadAsync(ApplicationDbContext context, Guid productId)
        => context.Products
            .Include(p => p.PlatformPrices)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Options).ThenInclude(o => o.PlatformPrices)
            .SingleAsync(p => p.Id == productId);

    private static ProductOption Option(Product product, string name)
        => product.OptionGroups.SelectMany(g => g.Options).Single(o => o.Name == name);

    // Rows written straight to the tables, past the app's own checks.
    private static Task<int> InsertPriceAsync(ApplicationDbContext context, Guid tenantId, Guid productId, int channel = 3, decimal price = 125m)
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ProductPlatformPrices (Id, TenantId, ProductId, Channel, Price)
            VALUES ({Guid.NewGuid()}, {tenantId}, {productId}, {channel}, {price})
            """);

    private static Task<int> InsertOptionPriceAsync(ApplicationDbContext context, Guid tenantId, Guid optionId, int channel = 3, decimal delta = 38m)
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ProductOptionPlatformPrices (Id, TenantId, OptionId, Channel, PriceDelta)
            VALUES ({Guid.NewGuid()}, {tenantId}, {optionId}, {channel}, {delta})
            """);

    private static async Task RefusedAsync(string constraint, Func<Task> insert)
    {
        var error = await Assert.ThrowsAsync<SqlException>(insert);
        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
