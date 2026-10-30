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
/// AddGlobalInvoiceWriteOffNotes (Fase 28J, ADR 0055): a global invoice's credit note may credit a write-off of one of its
/// sales, not only a refund. Before it the database refuses one; after it, saves it and still refuses what the domain never
/// writes; and rolling back stops with error 50045 while one exists, and otherwise brings the restriction back.
/// </summary>
public class GlobalInvoiceWriteOffNotesMigrationSqlServerTests
{
    // The migration right before AddGlobalInvoiceWriteOffNotes, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261028013618_AddGlobalInvoiceCreditNotes";
    private const string Migration = "20261029163102_AddGlobalInvoiceWriteOffNotes";

    [SqlServerFact]
    public async Task GlobalInvoicesWriteOffNote_ShouldBeRefusedBefore_SavedAfter_AndTheDatabaseRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var seed = await SeedAsync(database);

        // Before: only a refund of a sale takes a credit note of a global invoice.
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database, seed.TenantId))
        {
            context.CreditNotes.Add(WriteOffNote(seed, 1));
            var refused = await Record.ExceptionAsync(() => context.SaveChangesAsync());
            Assert.True(refused is DbUpdateException { InnerException: SqlException { Number: 547 } }, refused?.Message ?? "it was not refused");
        }

        await MigrateAsync(database, Migration);
        var refundNote = RefundNote(seed, 2);
        var writeOffNote = WriteOffNote(seed, 3);
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database, seed.TenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            context.CreditNotes.AddRange(refundNote, writeOffNote);
            await context.SaveChangesAsync();
        }
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database, seed.TenantId))
        {
            var saved = await context.CreditNotes.SingleAsync(n => n.Id == writeOffNote.Id);
            Assert.Equal(((Guid?)null, (Guid?)seed.Global.Id, seed.OwedOrderId, (Guid?)null, (Guid?)seed.WriteOffId, "15", 0), (
                saved.OrderInvoiceId, saved.GlobalInvoiceId, saved.OrderId, saved.OrderRefundId, saved.OrderWriteOffId,
                saved.PaymentForm, saved.AfterInstallment));
        }
        // Another business does not see them.
        await using (var foreign = BeforeCfdiWhatsApp.CreateContext(database, seed.OtherTenantId))
            Assert.Empty(await foreign.CreditNotes.ToListAsync());

        (FormattableString Sql, int Number)[] never =
        [
            // A global invoice is paid in one exhibition: no installment before its credit notes.
            ($"UPDATE [CreditNotes] SET [AfterInstallment] = 1 WHERE [Id] = {writeOffNote.Id}", 547),
            // Both an order's invoice and a global invoice.
            ($"UPDATE [CreditNotes] SET [OrderInvoiceId] = {seed.InvoiceId} WHERE [Id] = {writeOffNote.Id}", 547),
            // Neither.
            ($"UPDATE [CreditNotes] SET [GlobalInvoiceId] = NULL WHERE [Id] = {writeOffNote.Id}", 547),
            // A write-off is settled by «Condonación» (15), never by a form of money.
            ($"UPDATE [CreditNotes] SET [PaymentForm] = '01' WHERE [Id] = {writeOffNote.Id}", 547),
            // A second live credit note of the same write-off.
            ($"UPDATE [CreditNotes] SET [OrderRefundId] = NULL, [OrderWriteOffId] = {seed.WriteOffId}, [PaymentForm] = '15' WHERE [Id] = {refundNote.Id}",
                2601)
        ];
        foreach (var (sql, number) in never)
        {
            var refused = await Record.ExceptionAsync(() => ExecuteAsync(database, sql));
            Assert.True(refused is SqlException e && e.Number == number, $"{sql.Format}: {refused?.Message ?? "it was not refused"}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWhileAGlobalInvoicesWriteOffNoteExists_AndThenBringTheRestrictionBack()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var refundNote = RefundNote(seed, 1);
        var writeOffNote = WriteOffNote(seed, 2);
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database, seed.TenantId))
        {
            context.CreditNotes.AddRange(refundNote, writeOffNote);
            await context.SaveChangesAsync();
            // Refused by the PAC, it still stops it: its row stays.
            writeOffNote.Refuse("CFDI40161 - El UsoCFDI no corresponde con el régimen del receptor.", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50045 }, stopped?.Message ?? "it was not stopped");
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(2, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [CreditNotes]"));
        }

        await ExecuteAsync(database, $"DELETE FROM [CreditNotes] WHERE [Id] = {writeOffNote.Id}");
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            // The refund's credit note of the global invoice stays.
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [CreditNotes] WHERE [GlobalInvoiceId] IS NOT NULL"));
        }
        // And the restriction is back: a global invoice's credit note is only of a refund.
        var refused = await Record.ExceptionAsync(() => ExecuteAsync(database,
            $"UPDATE [CreditNotes] SET [OrderRefundId] = NULL, [OrderWriteOffId] = {seed.WriteOffId}, [PaymentForm] = '15' WHERE [Id] = {refundNote.Id}"));
        Assert.True(refused is SqlException { Number: 547 }, refused?.Message ?? "it was not refused");

        // Rolling back again does nothing, and forward again the write-off takes its credit note.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCfdiWhatsApp.CreateContext(database, seed.TenantId);
        forward.CreditNotes.Add(WriteOffNote(seed, 3));
        await forward.SaveChangesAsync();
        Assert.Equal(2, await forward.CreditNotes.CountAsync(n => n.GlobalInvoiceId == seed.Global.Id));
    }

    private sealed record Seed(
        Guid TenantId,
        Guid OtherTenantId,
        Guid InvoiceId,
        Guid OwedOrderId,
        Guid WriteOffId,
        OrderRefund Refund,
        OrderWriteOff WriteOff,
        GlobalInvoice Global);

    /// <summary>
    /// Two businesses; the first with a stamped global invoice that covers a sale paid in cash (one refund) and a sale with
    /// part of it forgiven, and an order invoiced on its own (the database does not check what a sale owes).
    /// </summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeCfdiWhatsApp.CreateContext(database);
        var tenants = Enumerable.Range(0, 2).Select(_ =>
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        }).ToList();
        foreach (var tenant in tenants)
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        var tenantId = tenants[0].Id;
        var paid = NewOrder(tenantId, 1);
        paid.RegisterPayment(200m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-2), null, null, null, null);
        var refund = paid.RegisterRefund(50m, OrderPaymentMethod.Cash, "Devolución de un café", DateTimeOffset.UtcNow, "cajero", null, null, false, []);
        var owed = NewOrder(tenantId, 2);
        owed.RegisterPayment(100m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-2), null, null, null, null);
        owed.SellOnCredit();
        var writeOff = owed.WriteOff(30m, OrderWriteOffReason.Discount, null, DateTimeOffset.UtcNow.AddDays(-1), "dueña");
        var invoiced = NewOrder(tenantId, 3);
        invoiced.RegisterPayment(200m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-2), null, null, null, null);
        var invoice = OrderInvoice.Request(
            tenantId, invoiced.Id, "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"),
            "01", "{}", 172.41m, 27.59m, 200m, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
        invoice.Stamp(Guid.NewGuid().ToString(), "pac-1", DateTimeOffset.UtcNow.AddDays(-1), "invoices/1.xml");
        var global = GlobalInvoice.Request(
            tenantId, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Daily, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2))),
            "F", 2, "01", "{}", 344.83m, 55.17m, 400m,
            [new GlobalInvoiceSale(paid.Id, paid.OrderNumber, 200m), new GlobalInvoiceSale(owed.Id, owed.OrderNumber, 200m)],
            Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
        global.Stamp(Guid.NewGuid().ToString(), "pac-2", DateTimeOffset.UtcNow.AddDays(-1), "invoices/2.xml");
        context.Orders.AddRange(paid, owed, invoiced);
        context.AddRange(invoice, global);
        await context.SaveChangesAsync();
        return new Seed(tenantId, tenants[1].Id, invoice.Id, owed.Id, writeOff.Id, refund, writeOff, global);
    }

    private static Order NewOrder(Guid tenantId, int number)
    {
        var order = new Order(tenantId, null, null, true, $"ORD-00000{number}", BusinessType.Retail, OrderType.Retail, "Karla",
            "+525512345678", null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        return order;
    }

    private static CreditNote RefundNote(Seed seed, long folio)
    {
        var (subTotal, tax) = Split(seed.Refund.Amount);
        return CreditNote.ForRefundInGlobalInvoice(seed.Global, seed.Global.Orders.Single(o => o.OrderId == seed.Refund.OrderId),
            seed.Refund, "NC", folio, subTotal, tax, "{}", null, DateTimeOffset.UtcNow);
    }

    private static CreditNote WriteOffNote(Seed seed, long folio)
    {
        var (subTotal, tax) = Split(seed.WriteOff.Amount);
        return CreditNote.ForWriteOffInGlobalInvoice(seed.Global, seed.Global.Orders.Single(o => o.OrderId == seed.OwedOrderId),
            seed.WriteOff, "NC", folio, subTotal, tax, "{}", null, DateTimeOffset.UtcNow);
    }

    private static (decimal SubTotal, decimal Tax) Split(decimal total)
    {
        var subTotal = Math.Round(total / 1.16m, 2);
        return (subTotal, total - subTotal);
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = BeforeCfdiWhatsApp.CreateContext(database);
        await context.Database.ExecuteSqlAsync(sql);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql)
        => context.Database.SqlQuery<int>(sql).SingleAsync();

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
