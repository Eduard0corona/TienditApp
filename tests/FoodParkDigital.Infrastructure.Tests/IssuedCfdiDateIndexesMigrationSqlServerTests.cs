using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddIssuedCfdiDateIndexes (Fase 28H, ADR 0052): a business's CFDI by date in each of the four tables, so a month of
/// Reportes › CFDI emitidos is read without going through every CFDI the business has stamped. Only indexes: the CFDI
/// stamped before stay as they were, and rolling back takes the indexes away and nothing else.
/// </summary>
public class IssuedCfdiDateIndexesMigrationSqlServerTests
{
    // The migration right before AddIssuedCfdiDateIndexes, where its rollback.sql takes the database.
    private const string BeforeIndexesMigration = "20261026145713_AddInvoiceSubstitution";
    private const string Migration = "20261026201123_AddIssuedCfdiDateIndexes";

    private static readonly string[] Indexes =
    [
        "IX_CreditNotes_TenantId_IssuedAt", "IX_GlobalInvoices_TenantId_IssuedAt", "IX_OrderInvoices_TenantId_IssuedAt",
        "IX_PaymentComplements_TenantId_IssuedAt"
    ];

    [SqlServerFact]
    public async Task Migration_ShouldIndexEachTableByBusinessAndDate_KeepingTheInvoices_AndRollBackToJustThat()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeIndexesMigration);
        var (tenantId, invoiceId) = await SeedStampedInvoiceAsync(database);

        await MigrateAsync(database, Migration);
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(
                Indexes.Select(name => $"{name}: TenantId, IssuedAt, not unique, no filter"),
                await IndexesAsync(context));
        }
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database, tenantId))
            Assert.Equal(invoiceId, (await context.OrderInvoices.SingleAsync()).Id);

        await MigrateAsync(database, BeforeIndexesMigration);
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database))
        {
            Assert.Equal(BeforeIndexesMigration, await LatestMigrationAsync(context));
            Assert.Empty(await IndexesAsync(context));
            Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [OrderInvoices]").SingleAsync());
        }

        // Rolling back again does nothing, and forward again the indexes are back.
        await MigrateAsync(database, BeforeIndexesMigration);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCfdiWhatsApp.CreateContext(database);
        Assert.Equal(4, (await IndexesAsync(forward)).Count);
    }

    /// <summary>A business with an order paid in cash and its stamped invoice, as any version stores them.</summary>
    private static async Task<(Guid TenantId, Guid InvoiceId)> SeedStampedInvoiceAsync(SqlServerTestDatabase database)
    {
        var slug = $"tienda-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla",
            "+525512345678", null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        order.RegisterPayment(200m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-1), null, null, null, null);
        var invoice = OrderInvoice.Request(
            tenant.Id, order.Id, "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"),
            "01", "{}", 172.41m, 27.59m, 200m, Guid.NewGuid(), DateTimeOffset.UtcNow);
        invoice.Stamp(Guid.NewGuid().ToString(), "pac-1", DateTimeOffset.UtcNow, "invoices/1.xml");

        await using var context = BeforeCfdiWhatsApp.CreateContext(database, tenant.Id);
        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail), order, invoice);
        await context.SaveChangesAsync();
        return (tenant.Id, invoice.Id);
    }

    /// <summary>The date indexes there are, with their columns in order.</summary>
    private static Task<List<string>> IndexesAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT i.[name] + N': ' + STUFF((
                    SELECT N', ' + c.[name]
                    FROM sys.index_columns ic
                    INNER JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                    WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                    ORDER BY ic.[key_ordinal]
                    FOR XML PATH(N'')), 1, 2, N'')
                + CASE WHEN i.[is_unique] = 1 THEN N', unique' ELSE N', not unique' END
                + CASE WHEN i.[has_filter] = 1 THEN N', filtered' ELSE N', no filter' END AS [Value]
            FROM sys.indexes i
            WHERE i.[name] LIKE N'IX[_]%[_]TenantId[_]IssuedAt'
            ORDER BY i.[name]
            """).ToListAsync();

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
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
