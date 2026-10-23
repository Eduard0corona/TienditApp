using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddOrderInvoicing (Fase 28A, ADR 0045): the invoices of the orders, the business's CSD and serie, and the customers' fiscal
/// data. The version deployed before keeps storing customers without fiscal data; an order has one live invoice; folios go
/// per serie; the checks refuse what the domain never writes; and rolling back stops with error 50037 once there is an
/// invoice, a CSD or a customer's fiscal data, and otherwise drops only what it added.
/// </summary>
public class OrderInvoicingMigrationSqlServerTests
{
    // The migration right before AddOrderInvoicing, where its rollback.sql takes the database.
    private const string BeforeInvoicing = "20261022213253_AddOnlinePaymentReconciliation";
    private const string Migration = "20261023064133_AddOrderInvoicing";

    [SqlServerFact]
    public async Task Columns_ShouldLeaveEveryCustomerWithoutFiscalData_WhileThePreviousVersionStoresThem()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeInvoicing);
        var seed = await SeedAsync(database);

        await MigrateAsync(database, Migration);

        await using (var previous = BeforeOrderInvoicing.CreateContext(database, seed.TenantId))
        {
            previous.Customers.Add(new Customer(seed.TenantId, "Luis", "+525511112222", null));
            await previous.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.All(await context.Customers.ToListAsync(), customer => Assert.Null(customer.FiscalData));
            var karla = await context.Customers.SingleAsync(c => c.Name == "Karla");
            karla.SetFiscalData(Receiver());
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(seed.TenantId);
        Assert.Equal("XIQB891116QE4", (await check.Customers.SingleAsync(c => c.Name == "Karla")).FiscalData!.Rfc);
        Assert.Null((await check.Customers.SingleAsync(c => c.Name == "Luis")).FiscalData);
        // Clientes lists it, projected in SQL.
        var listed = await new GetCustomersQueryHandler(check).Handle(new GetCustomersQuery(), CancellationToken.None);
        Assert.Equal(("XIQB891116QE4", "KARLA FUENTE NOLASCO", "26015", "612", "G03"),
            listed.Items.Where(c => c.Name == "Karla").Select(c => (c.FiscalData!.Rfc, c.FiscalData.Name, c.FiscalData.PostalCode, c.FiscalData.FiscalRegime, c.FiscalData.CfdiUse)).Single());
        Assert.Null(listed.Items.Single(c => c.Name == "Luis").FiscalData);
    }

    [SqlServerFact]
    public async Task Database_ShouldKeepOneLiveInvoicePerOrder_FoliosPerSerie_AndRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);

        long first, second, otherSerie;
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var folios = new FolioGenerator(context);
            first = await folios.NextInvoiceFolioAsync(seed.TenantId, "F", CancellationToken.None);
            second = await folios.NextInvoiceFolioAsync(seed.TenantId, "F", CancellationToken.None);
            otherSerie = await folios.NextInvoiceFolioAsync(seed.TenantId, "A", CancellationToken.None);
            var refused = Invoice(seed, first);
            refused.Refuse("CFDI40145 - El nombre del receptor no coincide.", DateTimeOffset.UtcNow);
            context.OrderInvoices.AddRange(refused, Invoice(seed, second));
            await context.SaveChangesAsync();
        }
        Assert.Equal((1L, 2L, 1L), (first, second, otherSerie));

        // A second live invoice of the same order, or the same folio of the serie, is refused by the database.
        var live = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, seed, Invoice(seed, 3)));
        Assert.Contains("IX_OrderInvoices_TenantId_OrderId_Live", live.InnerException!.Message);
        var sameFolio = Invoice(seed, second);
        sameFolio.Refuse("Duplicada", DateTimeOffset.UtcNow);
        var folio = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, seed, sameFolio));
        Assert.Contains("IX_OrderInvoices_TenantId_Serie_Folio", folio.InnerException!.Message);

        Guid invoiceId;
        await using (var context = database.CreateContext(seed.TenantId))
            invoiceId = await context.OrderInvoices.Where(i => i.Status == OrderInvoiceStatus.Stamping).Select(i => i.Id).SingleAsync();
        FormattableString[] never =
        [
            $"UPDATE [OrderInvoices] SET [Status] = 9 WHERE [Id] = {invoiceId}",
            $"UPDATE [OrderInvoices] SET [Status] = 1 WHERE [Id] = {invoiceId}",
            $"UPDATE [OrderInvoices] SET [Uuid] = N'5FB2822E-396D-4725-8521-CDC4BDD20CCF' WHERE [Id] = {invoiceId}",
            $"UPDATE [OrderInvoices] SET [Status] = 2 WHERE [Id] = {invoiceId}",
            $"UPDATE [OrderInvoices] SET [CancellationMotive] = 1 WHERE [Id] = {invoiceId}",
            $"UPDATE [OrderInvoices] SET [PaymentMethod] = N'PPD' WHERE [Id] = {invoiceId}",
            $"UPDATE [OrderInvoices] SET [Total] = [Total] + 1 WHERE [Id] = {invoiceId}",
            $"UPDATE [OrderInvoices] SET [Folio] = 0 WHERE [Id] = {invoiceId}",
            $"UPDATE [InvoicingProfiles] SET [CertificateRfc] = NULL WHERE [TenantId] = {seed.TenantId}",
            $"UPDATE [Customers] SET [FiscalData_Rfc] = N'XIQB891116QE4' WHERE [TenantId] = {seed.TenantId}"
        ];
        foreach (var sql in never)
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, sql))).Number);

        // A folio counter created after invoices exist starts after the highest folio of its serie.
        await ExecuteAsync(database, $"DELETE FROM [FolioSequences] WHERE [TenantId] = {seed.TenantId} AND [Scope] = N'CFDI-F'");
        await using var again = database.CreateContext(seed.TenantId);
        Assert.Equal(3L, await new FolioGenerator(again).NextInvoiceFolioAsync(seed.TenantId, "F", CancellationToken.None));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceThereIsAnInvoiceACsdOrFiscalData_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);

        // Each one stops it: an invoice (even refused), a CSD, a customer's fiscal data.
        var refused = Invoice(seed, 1);
        refused.Refuse("Rechazada", DateTimeOffset.UtcNow);
        await AddAsync(database, seed, refused);
        await AssertRollbackStopsAsync(database);
        await ExecuteAsync(database, $"DELETE FROM [OrderInvoices]");

        await AssertRollbackStopsAsync(database);
        await ExecuteAsync(database, $"""
            UPDATE [InvoicingProfiles] SET [CertificateNumber] = NULL, [CertificateRfc] = NULL, [CertificateValidFrom] = NULL,
                [CertificateValidTo] = NULL, [CertificateUploadedAt] = NULL, [CertificateUploadedByUserId] = NULL
            """);

        await using (var context = database.CreateContext(seed.TenantId))
        {
            (await context.Customers.SingleAsync()).SetFiscalData(Receiver());
            await context.SaveChangesAsync();
        }
        await AssertRollbackStopsAsync(database);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            (await context.Customers.SingleAsync()).SetFiscalData(null);
            await context.SaveChangesAsync();
        }

        // A serie without a CSD is not one of them.
        await MigrateAsync(database, BeforeInvoicing);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeInvoicing, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 1), (
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN (N'OrderInvoices', N'InvoicingProfiles')
                    """),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.Customers') AND [name] LIKE N'FiscalData[_]%'
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_Customers_FiscalData'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Customers]")));
        }

        // Rolling back again does nothing, and forward again every customer has no fiscal data.
        await MigrateAsync(database, BeforeInvoicing);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.All(await forward.Customers.ToListAsync(), customer => Assert.Null(customer.FiscalData));
    }

    private static async Task AssertRollbackStopsAsync(SqlServerTestDatabase database)
    {
        var used = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeInvoicing));
        Assert.Equal(50037, used.Number);
        await using var context = database.CreateContext();
        Assert.Equal(Migration, await LatestMigrationAsync(context));
    }

    private sealed record Seed(Guid TenantId, Guid OrderId, Guid UserId);

    /// <summary>A business with a paid order, a customer and a CSD in force, as the previous version stores the first two.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        Guid tenantId, orderId;
        await using (var context = BeforeOrderInvoicing.CreateContext(database))
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            var tenant = new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
            var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla", "+525512345678",
                null, null, pricesIncludeTax: true);
            order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
            context.Orders.Add(order);
            context.Customers.Add(new Customer(tenant.Id, "Karla", "+525512345678", null));
            await context.SaveChangesAsync();
            (tenantId, orderId) = (tenant.Id, order.Id);
        }

        var userId = Guid.NewGuid();
        await using (var history = database.CreateContext())
        {
            if (await LatestMigrationAsync(history) != Migration)
                return new Seed(tenantId, orderId, userId);
        }
        {
            await using var context = database.CreateContext(tenantId);
            var profile = InvoicingProfile.Create(tenantId);
            profile.UseCertificate("30001000000400002434", "EKU9003173C9", DateTimeOffset.UtcNow.AddYears(-1),
                DateTimeOffset.UtcNow.AddYears(3), userId, DateTimeOffset.UtcNow);
            context.InvoicingProfiles.Add(profile);
            await context.SaveChangesAsync();
        }
        return new Seed(tenantId, orderId, userId);
    }

    private static CfdiReceiver Receiver() => CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03");

    private static OrderInvoice Invoice(Seed seed, long folio)
        => OrderInvoice.Request(seed.TenantId, seed.OrderId, "F", folio, Receiver(), "04", "{\"serie\":\"F\"}",
            172.41m, 27.59m, 200m, seed.UserId, DateTimeOffset.UtcNow);

    private static async Task AddAsync(SqlServerTestDatabase database, Seed seed, OrderInvoice invoice)
    {
        await using var context = database.CreateContext(seed.TenantId);
        context.OrderInvoices.Add(invoice);
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
