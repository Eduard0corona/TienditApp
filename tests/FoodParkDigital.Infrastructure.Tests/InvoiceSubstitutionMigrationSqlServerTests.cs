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
/// AddInvoiceSubstitution (Fase 28G, ADR 0051): the invoice of the same order that replaces another, which is cancelled
/// with motive 01. The version deployed before keeps invoicing with one live invoice per order; a replaced invoice stands
/// next to its replacement until the SAT cancels it, and the database refuses what the domain never writes; and rolling
/// back stops with error 50043 while an invoice is replaced, and otherwise leaves the invoices as they were.
/// </summary>
public class InvoiceSubstitutionMigrationSqlServerTests
{
    // The migration right before AddInvoiceSubstitution, where its rollback.sql takes the database.
    private const string BeforeSubstitutionMigration = "20261026082041_AddSelfInvoicing";
    private const string Migration = "20261026145713_AddInvoiceSubstitution";

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepInvoicing_WithOneLiveInvoicePerOrder()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeSubstitutionMigration);
        var seed = await SeedAsync(database);
        await using (var previous = BeforeInvoiceSubstitution.CreateContext(database, seed.TenantId))
        {
            previous.OrderInvoices.Add(Stamped(seed, seed.OrderId, 1));
            await previous.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        // The deployed version keeps invoicing other orders, and still cannot give an order a second live invoice.
        await using (var previous = BeforeInvoiceSubstitution.CreateContext(database, seed.TenantId))
        {
            previous.OrderInvoices.Add(Stamped(seed, seed.OtherOrderId, 2));
            await previous.SaveChangesAsync();
        }
        await using (var previous = BeforeInvoiceSubstitution.CreateContext(database, seed.TenantId))
        {
            previous.OrderInvoices.Add(Requested(seed, seed.OrderId, 3));
            var refused = await Record.ExceptionAsync(() => previous.SaveChangesAsync());
            Assert.True(refused is DbUpdateException { InnerException: SqlException { Number: 2601 } }, refused?.Message ?? "it was not refused");
        }

        await using var context = database.CreateContext(seed.TenantId);
        var invoices = await context.OrderInvoices.OrderBy(i => i.Folio).ToListAsync();
        Assert.Equal([1L, 2L], invoices.Select(i => i.Folio));
        Assert.All(invoices, invoice => Assert.True(invoice.IsCurrent));
    }

    [SqlServerFact]
    public async Task ReplacedInvoice_ShouldStandNextToItsReplacement_AndTheDatabaseRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var original = Stamped(seed, seed.OrderId, 1);
        var other = Stamped(seed, seed.OtherOrderId, 2);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.OrderInvoices.AddRange(original, other);
            await context.SaveChangesAsync();
        }

        // The replacement first: the original still counts as the order's live invoice.
        var replacement = Requested(seed, seed.OrderId, 3);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.OrderInvoices.Add(replacement);
            var refused = await Record.ExceptionAsync(() => context.SaveChangesAsync());
            Assert.True(refused is DbUpdateException { InnerException: SqlException { Number: 2601 } }, refused?.Message ?? "it was not refused");
        }

        // The original stops counting first, and then its replacement is saved, in one transaction.
        replacement = Requested(seed, seed.OrderId, 3);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var tracked = await context.OrderInvoices.SingleAsync(i => i.Id == original.Id);
            tracked.ReplaceWith(replacement);
            await context.SaveChangesAsync();
            context.OrderInvoices.Add(replacement);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var live = await context.OrderInvoices.Where(i => i.OrderId == seed.OrderId).OrderBy(i => i.Folio).ToListAsync();
            Assert.Equal([(1L, replacement.Id, false), (3L, (Guid?)null, true)],
                live.Select(i => (i.Folio, i.ReplacedByInvoiceId, i.IsCurrent)));

            // Nor a third live one next to the replacement.
            context.OrderInvoices.Add(Requested(seed, seed.OrderId, 4));
            var refused = await Record.ExceptionAsync(() => context.SaveChangesAsync());
            Assert.True(refused is DbUpdateException { InnerException: SqlException { Number: 2601 } }, refused?.Message ?? "it was not refused");
        }

        // Another business does not see them.
        await using (var foreign = database.CreateContext(seed.OtherTenantId))
            Assert.Empty(await foreign.OrderInvoices.ToListAsync());

        (FormattableString Sql, int Number)[] never =
        [
            // Replaced by itself.
            ($"UPDATE [OrderInvoices] SET [ReplacedByInvoiceId] = [Id] WHERE [Id] = {other.Id}", 547),
            // One still stamping is not replaced.
            ($"UPDATE [OrderInvoices] SET [ReplacedByInvoiceId] = {other.Id} WHERE [Id] = {replacement.Id}", 547),
            // One invoice replaces only one.
            ($"UPDATE [OrderInvoices] SET [ReplacedByInvoiceId] = {replacement.Id} WHERE [Id] = {other.Id}", 2601),
            // With relation (01) only once replaced.
            ($"""
              UPDATE [OrderInvoices] SET [Status] = 3, [CancellationMotive] = 1, [CancellationRequestedAt] = SYSDATETIMEOFFSET(),
                  [CancellationRequestedByUserId] = NEWID()
              WHERE [Id] = {other.Id}
              """, 547)
        ];
        foreach (var (sql, number) in never)
        {
            var refused = await Record.ExceptionAsync(() => ExecuteAsync(database, sql));
            Assert.True(refused is SqlException e && e.Number == number, $"{sql.Format}: {refused?.Message ?? "it was not refused"}");
        }

        // The original is cancelled with relation once its replacement is stamped.
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var tracked = await context.OrderInvoices.SingleAsync(i => i.Id == replacement.Id);
            tracked.Stamp("6f0b7c1e-2a3d-4e5f-8a9b-0c1d2e3f4a5c", "pac-3", DateTimeOffset.UtcNow, "invoices/3.xml");
            var replaced = await context.OrderInvoices.SingleAsync(i => i.Id == original.Id);
            replaced.RequestCancellation(CfdiCancellationMotive.ErrorsWithReplacement, Guid.NewGuid(), DateTimeOffset.UtcNow);
            replaced.Cancel(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var cancelled = await context.OrderInvoices.SingleAsync(i => i.Id == original.Id);
            Assert.Equal((OrderInvoiceStatus.Cancelled, CfdiCancellationMotive.ErrorsWithReplacement, replacement.Id),
                (cancelled.Status, cancelled.CancellationMotive!.Value, cancelled.ReplacedByInvoiceId!.Value));
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWhileAnInvoiceIsReplaced_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var original = Stamped(seed, seed.OrderId, 1);
        var replacement = Requested(seed, seed.OrderId, 2);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.OrderInvoices.Add(original);
            await context.SaveChangesAsync();
            original.ReplaceWith(replacement);
            await context.SaveChangesAsync();
            context.OrderInvoices.Add(replacement);
            await context.SaveChangesAsync();
        }

        async Task StopsAsync(string what)
        {
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeSubstitutionMigration));
            Assert.True(stopped is SqlException { Number: 50043 }, $"{what}: {stopped?.Message ?? "it was not stopped"}");
        }

        // Replaced and standing, or already cancelled with relation: each stops it.
        await StopsAsync("Replaced, waiting for its cancellation");
        await ExecuteAsync(database, $"""
            UPDATE [OrderInvoices] SET [Status] = 4, [CancellationMotive] = 1, [CancellationRequestedAt] = SYSDATETIMEOFFSET(),
                [CancellationRequestedByUserId] = NEWID(), [CancelledAt] = SYSDATETIMEOFFSET()
            WHERE [Id] = {original.Id}
            """);
        await StopsAsync("Replaced and cancelled");
        await using (var context = database.CreateContext())
            Assert.Equal(Migration, await LatestMigrationAsync(context));

        await ExecuteAsync(database, $"""
            UPDATE [OrderInvoices] SET [ReplacedByInvoiceId] = NULL, [CancellationMotive] = 2 WHERE [Id] = {original.Id}
            """);
        await MigrateAsync(database, BeforeSubstitutionMigration);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeSubstitutionMigration, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 2), (
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.OrderInvoices') AND [name] = N'ReplacedByInvoiceId'
                    """),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_OrderInvoices_Replacement'
                    """),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = N'IX_OrderInvoices_ReplacedByInvoiceId'
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OrderInvoices]")));
            Assert.Equal("([Status] IN ((0), (1), (3)))", await context.Database.SqlQuery<string>($"""
                SELECT [filter_definition] AS [Value] FROM sys.indexes WHERE [name] = N'IX_OrderInvoices_TenantId_OrderId_Live'
                """).SingleAsync());
        }

        // Rolling back again does nothing, and forward again no invoice is replaced.
        await MigrateAsync(database, BeforeSubstitutionMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.All(await forward.OrderInvoices.ToListAsync(), invoice => Assert.Null(invoice.ReplacedByInvoiceId));
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid OrderId, Guid OtherOrderId);

    /// <summary>Two businesses, the first with two $200 orders paid in cash, as any version stores them.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeInvoiceSubstitution.CreateContext(database);
        var tenants = Enumerable.Range(0, 2).Select(_ =>
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        }).ToList();
        foreach (var tenant in tenants)
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        var orders = Enumerable.Range(1, 2).Select(number =>
        {
            var order = new Order(tenants[0].Id, null, null, true, $"ORD-00000{number}", BusinessType.Retail, OrderType.Retail, "Karla",
                "+525512345678", null, null, pricesIncludeTax: true);
            order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
            order.RegisterPayment(200m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-1), null, null, null, null);
            return order;
        }).ToList();
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
        return new Seed(tenants[0].Id, tenants[1].Id, orders[0].Id, orders[1].Id);
    }

    private static OrderInvoice Requested(Seed seed, Guid orderId, long folio) => OrderInvoice.Request(
        seed.TenantId, orderId, "F", folio, CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"),
        "01", "{}", 172.41m, 27.59m, 200m, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static OrderInvoice Stamped(Seed seed, Guid orderId, long folio)
    {
        var invoice = Requested(seed, orderId, folio);
        invoice.Stamp(Guid.NewGuid().ToString(), $"pac-{folio}", DateTimeOffset.UtcNow, $"invoices/{folio}.xml");
        return invoice;
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
