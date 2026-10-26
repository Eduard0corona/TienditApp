using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
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
/// AddSelfInvoicing (Fase 28F, ADR 0050): whether a business's customers invoice their tickets online, and the key of the
/// codes its tickets print. The version deployed before keeps creating and changing invoicing profiles (off, without a
/// key); the check refuses what the domain never writes; and rolling back stops with error 50042 while a business has a
/// key, on or off, and otherwise drops only what it added, keeping the invoices customers requested.
/// </summary>
public class SelfInvoicingMigrationSqlServerTests
{
    // The migration right before AddSelfInvoicing, where its rollback.sql takes the database.
    private const string BeforeSelfInvoicingMigration = "20261026032721_AddCfdiEmails";
    private const string Migration = "20261026082041_AddSelfInvoicing";

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepCreatingAndChangingProfiles_OffAndWithoutAKey()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeSelfInvoicingMigration);
        var seed = await SeedAsync(database);
        await using (var previous = BeforeSelfInvoicing.CreateContext(database, seed.TenantId))
        {
            previous.InvoicingProfiles.Add(InvoicingProfile.Create(seed.TenantId));
            await previous.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        // The deployed version keeps creating profiles and changing their series.
        await using (var previous = BeforeSelfInvoicing.CreateContext(database, seed.OtherTenantId))
        {
            previous.InvoicingProfiles.Add(InvoicingProfile.Create(seed.OtherTenantId));
            await previous.SaveChangesAsync();
        }
        await using (var previous = BeforeSelfInvoicing.CreateContext(database, seed.TenantId))
        {
            (await previous.InvoicingProfiles.SingleAsync()).ChangeSerie("A");
            await previous.SaveChangesAsync();
        }

        foreach (var tenantId in new[] { seed.TenantId, seed.OtherTenantId })
        {
            await using var context = database.CreateContext(tenantId);
            var profile = await context.InvoicingProfiles.SingleAsync();
            Assert.False(profile.SelfInvoicing);
            Assert.Null(profile.SelfInvoiceKey);
        }
        await using (var context = database.CreateContext(seed.TenantId))
            Assert.Equal("A", (await context.InvoicingProfiles.SingleAsync()).Serie);
    }

    [SqlServerFact]
    public async Task Database_ShouldKeepTheKey_AndRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var profile = InvoicingProfile.Create(seed.TenantId);
        profile.TurnOnSelfInvoicing();
        var code = profile.SelfInvoiceCodeFor(seed.OrderId);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.InvoicingProfiles.Add(profile);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(seed.TenantId))
        {
            var kept = await context.InvoicingProfiles.SingleAsync();
            Assert.True(kept.SelfInvoicing);
            Assert.Equal(profile.SelfInvoiceKey, kept.SelfInvoiceKey);
            Assert.Equal(code, kept.SelfInvoiceCodeFor(seed.OrderId));

            // Off, it keeps the key: the tickets already printed work again when it is turned back on.
            kept.TurnOffSelfInvoicing();
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var off = await context.InvoicingProfiles.SingleAsync();
            Assert.False(off.SelfInvoicing);
            Assert.Equal(profile.SelfInvoiceKey, off.SelfInvoiceKey);
        }

        // Another business does not see it.
        await using (var other = database.CreateContext(seed.OtherTenantId))
            Assert.Empty(await other.InvoicingProfiles.ToListAsync());

        FormattableString[] never =
        [
            // On without a key.
            $"UPDATE [InvoicingProfiles] SET [SelfInvoicing] = 1, [SelfInvoiceKey] = NULL WHERE [Id] = {profile.Id}",
            // A key that is not of 32 bytes.
            $"UPDATE [InvoicingProfiles] SET [SelfInvoiceKey] = CAST(0x0102 AS varbinary(32)) WHERE [Id] = {profile.Id}",
            $"UPDATE [InvoicingProfiles] SET [SelfInvoiceKey] = CAST(N'' AS varbinary(32)) WHERE [Id] = {profile.Id}"
        ];
        foreach (var sql in never)
        {
            var refused = await Record.ExceptionAsync(() => ExecuteAsync(database, sql));
            Assert.True(refused is SqlException { Number: 547 }, $"{sql.Format}: {refused?.Message ?? "it was not refused"}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithAKey_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var on = InvoicingProfile.Create(seed.TenantId);
        on.TurnOnSelfInvoicing();
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.InvoicingProfiles.Add(on);
            context.OrderInvoices.Add(OrderInvoice.Request(seed.TenantId, seed.OrderId, "F", 1, Receiver(), "01", "{}",
                172.41m, 27.59m, 200m, OrderInvoice.SelfInvoiceRequester, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(seed.OtherTenantId))
        {
            context.InvoicingProfiles.Add(InvoicingProfile.Create(seed.OtherTenantId));
            await context.SaveChangesAsync();
        }

        async Task StopsAsync(string what)
        {
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeSelfInvoicingMigration));
            Assert.True(stopped is SqlException { Number: 50042 }, $"{what}: {stopped?.Message ?? "it was not stopped"}");
        }

        // On, or off keeping its key: each stops it.
        await StopsAsync("Self-invoicing on");
        await ExecuteAsync(database, $"UPDATE [InvoicingProfiles] SET [SelfInvoicing] = 0");
        await StopsAsync("Self-invoicing off, keeping its key");
        await using (var context = database.CreateContext())
            Assert.Equal(Migration, await LatestMigrationAsync(context));

        await ExecuteAsync(database, $"UPDATE [InvoicingProfiles] SET [SelfInvoiceKey] = NULL");
        await MigrateAsync(database, BeforeSelfInvoicingMigration);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeSelfInvoicingMigration, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 2, 1), (
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND [name] IN (N'SelfInvoicing', N'SelfInvoiceKey')
                    """),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_InvoicingProfiles_SelfInvoicing'
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [InvoicingProfiles]"),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM [OrderInvoices]
                    WHERE [RequestedByUserId] = {OrderInvoice.SelfInvoiceRequester}
                    """)));
        }

        // Rolling back again does nothing, and forward again every business has it off and without a key.
        await MigrateAsync(database, BeforeSelfInvoicingMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        var profile = await forward.InvoicingProfiles.SingleAsync();
        Assert.False(profile.SelfInvoicing);
        Assert.Null(profile.SelfInvoiceKey);
        Assert.True((await forward.OrderInvoices.SingleAsync()).RequestedByCustomer);
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid OrderId);

    /// <summary>Two businesses, the first with a $200 order paid in cash, as any version stores them.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeSelfInvoicing.CreateContext(database);
        var tenants = Enumerable.Range(0, 2).Select(_ =>
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        }).ToList();
        foreach (var tenant in tenants)
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        var order = new Order(tenants[0].Id, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla", "+525512345678",
            null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        order.RegisterPayment(200m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-1), null, null, null, null);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return new Seed(tenants[0].Id, tenants[1].Id, order.Id);
    }

    private static CfdiReceiver Receiver() => CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03");

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
