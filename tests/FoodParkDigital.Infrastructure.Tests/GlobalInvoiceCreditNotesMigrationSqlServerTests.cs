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
/// AddGlobalInvoiceCreditNotes (Fase 28I, ADR 0054): a credit note credits an order's invoice or, for a refund of one of its
/// sales, a global invoice. The version deployed before keeps issuing the credit notes of its invoices with the database
/// migrated; the database refuses what the domain never writes; and rolling back stops with error 50044 while a credit note
/// of a global invoice exists, and otherwise leaves the credit notes as they were.
/// </summary>
public class GlobalInvoiceCreditNotesMigrationSqlServerTests
{
    // The migration right before AddGlobalInvoiceCreditNotes, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261026201123_AddIssuedCfdiDateIndexes";
    private const string Migration = "20261028013618_AddGlobalInvoiceCreditNotes";

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepIssuingTheCreditNotesOfItsInvoices_WithTheDatabaseMigrated()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var seed = await SeedAsync(database);
        await using (var previous = BeforeGlobalInvoiceCreditNotes.CreateContext(database, seed.TenantId))
        {
            previous.CreditNotes.Add(InvoiceNote(seed, seed.Refunds[0], 1));
            await previous.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal((1, 1), (await NullableAsync(context, "OrderInvoiceId"), await NullableAsync(context, "GlobalInvoiceId")));
        }

        // The deployed version keeps issuing them, and reads them as before.
        await using (var previous = BeforeGlobalInvoiceCreditNotes.CreateContext(database, seed.TenantId))
        {
            previous.CreditNotes.Add(InvoiceNote(seed, seed.Refunds[1], 2));
            await previous.SaveChangesAsync();
        }
        await using (var previous = BeforeGlobalInvoiceCreditNotes.CreateContext(database, seed.TenantId))
            Assert.Equal([1L, 2L], await previous.CreditNotes.OrderBy(n => n.Folio).Select(n => n.Folio).ToListAsync());
        await using var context2 = database.CreateContext(seed.TenantId);
        Assert.All(await context2.CreditNotes.ToListAsync(), note => Assert.Equal(
            ((Guid?)seed.InvoiceId, (Guid?)null, false), (note.OrderInvoiceId, note.GlobalInvoiceId, note.IsForGlobalInvoice)));
    }

    [SqlServerFact]
    public async Task GlobalInvoicesCreditNote_ShouldBeSaved_AndTheDatabaseRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var invoiceNote = InvoiceNote(seed, seed.Refunds[0], 1);
        var globalNote = GlobalNote(seed, seed.Refunds[2], 2);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.CreditNotes.AddRange(invoiceNote, globalNote);
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var saved = await context.CreditNotes.SingleAsync(n => n.Id == globalNote.Id);
            Assert.Equal(((Guid?)null, (Guid?)seed.GlobalInvoiceId, seed.SaleOrderId, (Guid?)seed.Refunds[2].Id, true),
                (saved.OrderInvoiceId, saved.GlobalInvoiceId, saved.OrderId, saved.OrderRefundId, saved.IsForGlobalInvoice));
            Assert.Equal([globalNote.Id], await context.CreditNotes.Where(n => n.GlobalInvoiceId == seed.GlobalInvoiceId)
                .Select(n => n.Id).ToListAsync());
        }
        // Another business does not see them.
        await using (var foreign = database.CreateContext(seed.OtherTenantId))
            Assert.Empty(await foreign.CreditNotes.ToListAsync());

        (FormattableString Sql, int Number)[] never =
        [
            // Both an order's invoice and a global invoice.
            ($"UPDATE [CreditNotes] SET [GlobalInvoiceId] = {seed.GlobalInvoiceId} WHERE [Id] = {invoiceNote.Id}", 547),
            // Neither.
            ($"UPDATE [CreditNotes] SET [OrderInvoiceId] = NULL WHERE [Id] = {invoiceNote.Id}", 547),
            // A global invoice is paid in one exhibition: no installment before its credit notes.
            ($"UPDATE [CreditNotes] SET [AfterInstallment] = 1 WHERE [Id] = {globalNote.Id}", 547),
            // A global invoice that does not exist.
            ($"UPDATE [CreditNotes] SET [GlobalInvoiceId] = NEWID() WHERE [Id] = {globalNote.Id}", 547)
        ];
        foreach (var (sql, number) in never)
        {
            var refused = await Record.ExceptionAsync(() => ExecuteAsync(database, sql));
            Assert.True(refused is SqlException e && e.Number == number, $"{sql.Format}: {refused?.Message ?? "it was not refused"}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWhileAGlobalInvoicesCreditNoteExists_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var globalNote = GlobalNote(seed, seed.Refunds[2], 2);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.CreditNotes.AddRange(InvoiceNote(seed, seed.Refunds[0], 1), globalNote);
            await context.SaveChangesAsync();
            // Refused by the PAC, it still stops it: its row stays.
            globalNote.Refuse("CFDI40161 - El UsoCFDI no corresponde con el régimen del receptor.", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50044 }, stopped?.Message ?? "it was not stopped");
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(2, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [CreditNotes]"));
        }

        await ExecuteAsync(database, $"DELETE FROM [CreditNotes] WHERE [Id] = {globalNote.Id}");
        await MigrateAsync(database, BeforeMigration);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 0, 1), (
                await NullableAsync(context, "OrderInvoiceId"),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.CreditNotes') AND [name] = N'GlobalInvoiceId'
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_CreditNotes_Invoice'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = N'IX_CreditNotes_GlobalInvoiceId'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [CreditNotes]")));
        }
        // The index of an invoice's credit notes is still there.
        await using (var context = database.CreateContext())
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = N'IX_CreditNotes_OrderInvoiceId'"));

        // Rolling back again does nothing, and forward again the credit note of the invoice is still its.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.Equal(((Guid?)seed.InvoiceId, (Guid?)null), await forward.CreditNotes.Select(n => new ValueTuple<Guid?, Guid?>(
            n.OrderInvoiceId, n.GlobalInvoiceId)).SingleAsync());
    }

    private sealed record Seed(
        Guid TenantId,
        Guid OtherTenantId,
        Guid InvoiceId,
        Guid GlobalInvoiceId,
        Guid SaleOrderId,
        IReadOnlyList<OrderRefund> Refunds,
        OrderInvoice Invoice,
        GlobalInvoice Global);

    /// <summary>
    /// Two businesses; the first with an order paid in cash and invoiced (two refunds), and a sale paid in cash that a stamped
    /// global invoice covers (one refund), as any version stores them.
    /// </summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeGlobalInvoiceCreditNotes.CreateContext(database);
        var tenants = Enumerable.Range(0, 2).Select(_ =>
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        }).ToList();
        foreach (var tenant in tenants)
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        var tenantId = tenants[0].Id;
        var orders = Enumerable.Range(1, 2).Select(number =>
        {
            var order = new Order(tenantId, null, null, true, $"ORD-00000{number}", BusinessType.Retail, OrderType.Retail, "Karla",
                "+525512345678", null, null, pricesIncludeTax: true);
            order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
            order.RegisterPayment(200m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-2), null, null, null, null);
            return order;
        }).ToList();
        var invoice = OrderInvoice.Request(
            tenantId, orders[0].Id, "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"),
            "01", "{}", 172.41m, 27.59m, 200m, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
        invoice.Stamp(Guid.NewGuid().ToString(), "pac-1", DateTimeOffset.UtcNow.AddDays(-1), "invoices/1.xml");
        var global = GlobalInvoice.Request(
            tenantId, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Daily, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2))),
            "F", 2, "01", "{}", 172.41m, 27.59m, 200m, [new GlobalInvoiceSale(orders[1].Id, orders[1].OrderNumber, 200m)],
            Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
        global.Stamp(Guid.NewGuid().ToString(), "pac-2", DateTimeOffset.UtcNow.AddDays(-1), "invoices/2.xml");
        var refunds = new List<OrderRefund> { Refund(orders[0], 20m), Refund(orders[0], 10m), Refund(orders[1], 50m) };
        context.Orders.AddRange(orders);
        context.AddRange(invoice, global);
        await context.SaveChangesAsync();
        return new Seed(tenantId, tenants[1].Id, invoice.Id, global.Id, orders[1].Id, refunds, invoice, global);
    }

    private static OrderRefund Refund(Order order, decimal amount)
        => order.RegisterRefund(amount, OrderPaymentMethod.Cash, "Devolución de un café", DateTimeOffset.UtcNow, "cajero", null, null, false, []);

    private static CreditNote InvoiceNote(Seed seed, OrderRefund refund, long folio)
    {
        var (subTotal, tax) = Split(refund.Amount);
        return CreditNote.ForRefund(seed.Invoice, refund, "NC", folio, subTotal, tax, 0, "{}", null, DateTimeOffset.UtcNow);
    }

    private static CreditNote GlobalNote(Seed seed, OrderRefund refund, long folio)
    {
        var (subTotal, tax) = Split(refund.Amount);
        return CreditNote.ForRefundInGlobalInvoice(
            seed.Global, seed.Global.Orders.Single(), refund, "NC", folio, subTotal, tax, "{}", null, DateTimeOffset.UtcNow);
    }

    private static (decimal SubTotal, decimal Tax) Split(decimal total)
    {
        var subTotal = Math.Round(total / 1.16m, 2);
        return (subTotal, total - subTotal);
    }

    /// <summary>Whether the column of CreditNotes takes nulls (1) or not (0).</summary>
    private static Task<int> NullableAsync(ApplicationDbContext context, string column)
        => context.Database.SqlQuery<int>($"""
            SELECT CAST([is_nullable] AS int) AS [Value] FROM sys.columns
            WHERE [object_id] = OBJECT_ID(N'dbo.CreditNotes') AND [name] = {column}
            """).SingleAsync();

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlAsync(sql);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql)
        => context.Database.SqlQuery<int>(sql).SingleAsync();

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
