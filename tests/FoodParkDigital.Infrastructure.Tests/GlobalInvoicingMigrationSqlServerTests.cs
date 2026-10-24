using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
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
/// AddGlobalInvoices (Fase 28B, ADR 0046): the global invoices and the sales each one covers. A sale is in one live global
/// invoice at most (a refused or cancelled one releases it and keeps the row), the checks refuse what the domain never
/// writes, and rolling back stops with error 50038 once there is a global invoice, and otherwise drops only what it added.
/// </summary>
public class GlobalInvoicingMigrationSqlServerTests
{
    // The migration right before AddGlobalInvoices, where its rollback.sql takes the database.
    private const string BeforeGlobal = "20261023064133_AddOrderInvoicing";
    private const string Migration = "20261023175930_AddGlobalInvoices";

    [SqlServerFact]
    public async Task Database_ShouldKeepASaleInOneLiveGlobalInvoice_AndRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);

        var first = Global(seed, 1, seed.Sales);
        await AddAsync(database, seed, first);

        // The same sale in another live global invoice is refused by the database…
        var again = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, seed, Global(seed, 2, [seed.Sales[0]])));
        Assert.Contains("IX_GlobalInvoiceOrders_TenantId_OrderId_Live", again.InnerException!.Message);

        // …until the first one is refused: its sales are released and kept, and they go in the next one.
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var refused = await context.GlobalInvoices.Include(i => i.Orders).SingleAsync(i => i.Id == first.Id);
            refused.Refuse("CFDI40136 - El año no es válido.", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        var second = Global(seed, 3, [seed.Sales[0]]);
        await AddAsync(database, seed, second);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.Equal(3, await context.GlobalInvoiceOrders.CountAsync());
            Assert.Equal(2, await context.GlobalInvoiceOrders.CountAsync(o => o.Released));
        }

        FormattableString[] never =
        [
            $"UPDATE [GlobalInvoices] SET [Status] = 9 WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [Status] = 1 WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [CancellationMotive] = 3 WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [PaymentMethod] = N'PPD' WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [Total] = [Total] + 1 WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [Periodicity] = 6 WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [Months] = '19' WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [PeriodTo] = DATEADD(DAY, -1, [PeriodFrom]) WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [Year] = [Year] - 1 WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoices] SET [OrderCount] = 0 WHERE [Id] = {second.Id}",
            $"UPDATE [GlobalInvoiceOrders] SET [Total] = 0 WHERE [GlobalInvoiceId] = {second.Id}"
        ];
        foreach (var sql in never)
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, sql))).Number);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithAGlobalInvoice_EvenARefusedOne_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var refused = Global(seed, 1, seed.Sales);
        refused.Refuse("Rechazada", DateTimeOffset.UtcNow);
        await AddAsync(database, seed, refused);

        var used = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeGlobal));
        Assert.Equal(50038, used.Number);
        await using (var context = database.CreateContext())
            Assert.Equal(Migration, await LatestMigrationAsync(context));

        await ExecuteAsync(database, $"DELETE FROM [GlobalInvoiceOrders]");
        await ExecuteAsync(database, $"DELETE FROM [GlobalInvoices]");
        await MigrateAsync(database, BeforeGlobal);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeGlobal, await LatestMigrationAsync(context));
            Assert.Equal((0, 1, 2), (
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN (N'GlobalInvoices', N'GlobalInvoiceOrders')
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'OrderInvoices'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Orders]")));
        }

        // Rolling back again does nothing, and forward again it is there, empty.
        await MigrateAsync(database, BeforeGlobal);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.Empty(await forward.GlobalInvoices.ToListAsync());
    }

    private sealed record Seed(Guid TenantId, Guid UserId, IReadOnlyList<GlobalInvoiceSale> Sales);

    /// <summary>A business with two paid sales of $116.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var slug = $"tienda-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        var sales = new List<GlobalInvoiceSale>();
        for (var number = 1; number <= 2; number++)
        {
            var order = new Order(tenant.Id, null, null, true, $"ORD-00000{number}", BusinessType.Retail, OrderType.Retail, "Público",
                null, null, null, pricesIncludeTax: true);
            order.AddItem(Guid.NewGuid(), "Café", 1, 116m);
            context.Orders.Add(order);
            sales.Add(new GlobalInvoiceSale(order.Id, order.OrderNumber, 116m));
        }
        await context.SaveChangesAsync();
        return new Seed(tenant.Id, Guid.NewGuid(), sales);
    }

    private static GlobalInvoice Global(Seed seed, long folio, IReadOnlyList<GlobalInvoiceSale> sales)
        => GlobalInvoice.Request(
            seed.TenantId,
            GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Monthly, new DateOnly(2026, 10, 1)),
            "F",
            folio,
            "01",
            "{\"serie\":\"F\"}",
            100m * sales.Count,
            16m * sales.Count,
            116m * sales.Count,
            sales,
            seed.UserId,
            DateTimeOffset.UtcNow);

    private static async Task AddAsync(SqlServerTestDatabase database, Seed seed, GlobalInvoice invoice)
    {
        await using var context = database.CreateContext(seed.TenantId);
        context.GlobalInvoices.Add(invoice);
        await context.SaveChangesAsync();
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
