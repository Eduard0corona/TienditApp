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
/// AddCreditNotes (Fase 28D, ADR 0048): the credit notes (CFDI de egreso) of the refunds and write-offs of an invoiced order,
/// and their serie. The version deployed before keeps storing profiles (they take «NC»); a refund and a write-off have one
/// live credit note each; the checks refuse what the domain never writes; and rolling back stops with error 50040 once there
/// is a credit note, and otherwise drops only what it added.
/// </summary>
public class CreditNotesMigrationSqlServerTests
{
    // The migration right before AddCreditNotes, where its rollback.sql takes the database.
    private const string BeforeNotes = "20261024014559_AddPaymentComplements";
    private const string Migration = "20261024084435_AddCreditNotes";
    private const string InvoiceUuid = "5F0B7C1E-2A3D-4E5F-8A9B-0C1D2E3F4A5B";

    [SqlServerFact]
    public async Task CreditNoteSerie_ShouldBeNcForEveryProfile_WhileThePreviousVersionStoresThem()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeNotes);
        var seed = await SeedAsync(database);
        await using (var previous = BeforeCreditNotes.CreateContext(database, seed.TenantId))
        {
            previous.InvoicingProfiles.Add(InvoicingProfile.Create(seed.TenantId));
            await previous.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        // The deployed version keeps creating profiles, and they take «NC» too.
        await using (var previous = BeforeCreditNotes.CreateContext(database, seed.OtherTenantId))
        {
            previous.InvoicingProfiles.Add(InvoicingProfile.Create(seed.OtherTenantId));
            await previous.SaveChangesAsync();
        }

        foreach (var tenantId in new[] { seed.TenantId, seed.OtherTenantId })
        {
            await using var context = database.CreateContext(tenantId);
            var profile = await context.InvoicingProfiles.SingleAsync();
            Assert.Equal((CreditNote.DefaultSerie, "P"), (profile.CreditNoteSerie, profile.PaymentSerie));
        }
    }

    [SqlServerFact]
    public async Task Database_ShouldKeepOneLiveCreditNotePerAdjustment_AndRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var invoice = await AddStampedInvoiceAsync(database, seed);

        var refundNote = ForRefund(invoice, seed, 1);
        await AddAsync(database, seed, refundNote);
        var writeOffNote = ForWriteOff(invoice, seed, 2);
        await AddAsync(database, seed, writeOffNote);

        // Another live credit note of the same refund or write-off is refused by the database…
        var again = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, seed, ForRefund(invoice, seed, 3)));
        Assert.Contains("IX_CreditNotes_OrderRefundId_Live", again.InnerException!.Message);
        var forgivenAgain = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, seed, ForWriteOff(invoice, seed, 4)));
        Assert.Contains("IX_CreditNotes_OrderWriteOffId_Live", forgivenAgain.InnerException!.Message);

        // …until the first one is refused: it stays, and the refund gets another one, with a folio of its own.
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var refused = await context.CreditNotes.SingleAsync(n => n.Id == refundNote.Id);
            refused.Refuse("CFDI40108 - La FormaPago no es válida.", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        var sameFolio = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, seed, ForRefund(invoice, seed, 2)));
        Assert.Contains("IX_CreditNotes_TenantId_Serie_Folio", sameFolio.InnerException!.Message);
        var second = ForRefund(invoice, seed, 5);
        await AddAsync(database, seed, second);
        await using (var context = database.CreateContext(seed.TenantId))
            Assert.Equal(2, await context.CreditNotes.CountAsync(n => n.OrderRefundId == seed.Refund.Id));

        FormattableString[] never =
        [
            $"UPDATE [CreditNotes] SET [Status] = 9 WHERE [Id] = {second.Id}",
            // Stamped without its UUID, or refused without why.
            $"UPDATE [CreditNotes] SET [Status] = 1 WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [Status] = 2 WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [Total] = 0, [SubTotal] = 0, [TaxTotal] = 0 WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [TaxTotal] = [TaxTotal] + 1 WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [SubTotal] = [Total] + 1, [TaxTotal] = -1 WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [AfterInstallment] = -1 WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [AfterInstallment] = 1000 WHERE [Id] = {second.Id}",
            // A refund is never settled «Por definir», as an advance nor by «Condonación»…
            $"UPDATE [CreditNotes] SET [PaymentForm] = '99' WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [PaymentForm] = '30' WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [PaymentForm] = '15' WHERE [Id] = {second.Id}",
            // …a write-off is, and a credit note credits exactly one of them.
            $"UPDATE [CreditNotes] SET [PaymentForm] = '01' WHERE [Id] = {writeOffNote.Id}",
            $"UPDATE [CreditNotes] SET [OrderWriteOffId] = {seed.WriteOff.Id} WHERE [Id] = {refundNote.Id}",
            $"UPDATE [CreditNotes] SET [OrderWriteOffId] = NULL WHERE [Id] = {writeOffNote.Id}",
            $"UPDATE [CreditNotes] SET [CancellationMotive] = 1 WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [CancellationMotive] = 4 WHERE [Id] = {second.Id}",
            $"UPDATE [CreditNotes] SET [Folio] = 0 WHERE [Id] = {second.Id}"
        ];
        foreach (var sql in never)
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, sql))).Number);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithACreditNote_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var invoice = await AddStampedInvoiceAsync(database, seed);
        // Refused, the credit note still blocks: it is part of the order's fiscal history.
        var refused = ForWriteOff(invoice, seed, 1);
        refused.Refuse("Rechazada", DateTimeOffset.UtcNow);
        await AddAsync(database, seed, refused);

        var used = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeNotes));
        Assert.Equal(50040, used.Number);
        await using (var context = database.CreateContext())
            Assert.Equal(Migration, await LatestMigrationAsync(context));

        await ExecuteAsync(database, $"DELETE FROM [CreditNotes]");
        await MigrateAsync(database, BeforeNotes);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeNotes, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 1, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'CreditNotes'"),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND [name] = N'CreditNoteSerie'
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'PaymentComplements'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OrderInvoices]")));
        }

        // Rolling back again does nothing, and forward again it is there, empty.
        await MigrateAsync(database, BeforeNotes);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.Empty(await forward.CreditNotes.ToListAsync());
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid OrderId, Guid UserId, OrderRefund Refund, OrderWriteOff WriteOff);

    /// <summary>
    /// A business with a $200 order sold on credit, paid $100, with $20 of it refunded and $30 forgiven (and another
    /// business), as any version stores them.
    /// </summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeCreditNotes.CreateContext(database);
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
        order.RegisterPayment(100m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-3), null, null, null, null);
        order.SellOnCredit();
        var refund = order.RegisterRefund(
            20m, OrderPaymentMethod.Cash, "Devolución de un café", DateTimeOffset.UtcNow.AddDays(-2), "cajero", null, null, false, []);
        var writeOff = order.WriteOff(30m, OrderWriteOffReason.WontBeCollected, null, DateTimeOffset.UtcNow.AddDays(-1), "dueña");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return new Seed(tenants[0].Id, tenants[1].Id, order.Id, Guid.NewGuid(), refund, writeOff);
    }

    private static async Task<OrderInvoice> AddStampedInvoiceAsync(SqlServerTestDatabase database, Seed seed)
    {
        var invoice = OrderInvoice.Request(seed.TenantId, seed.OrderId, "F", 1,
            CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"), "99", "{\"serie\":\"F\"}",
            172.41m, 27.59m, 200m, seed.UserId, DateTimeOffset.UtcNow.AddDays(-3), "PPD");
        invoice.Stamp(InvoiceUuid, "pac-1", DateTimeOffset.UtcNow.AddDays(-3), "f-1.xml");
        await using var context = database.CreateContext(seed.TenantId);
        context.OrderInvoices.Add(invoice);
        await context.SaveChangesAsync();
        return invoice;
    }

    private static CreditNote ForRefund(OrderInvoice invoice, Seed seed, long folio)
        => CreditNote.ForRefund(invoice, seed.Refund, "NC", folio, 17.24m, 2.76m, 0, "{\"serie\":\"NC\"}", seed.UserId, DateTimeOffset.UtcNow);

    private static CreditNote ForWriteOff(OrderInvoice invoice, Seed seed, long folio)
        => CreditNote.ForWriteOff(invoice, seed.WriteOff, "NC", folio, 25.86m, 4.14m, 1, "{\"serie\":\"NC\"}", null, DateTimeOffset.UtcNow);

    private static async Task AddAsync(SqlServerTestDatabase database, Seed seed, CreditNote note)
    {
        await using var context = database.CreateContext(seed.TenantId);
        context.CreditNotes.Add(note);
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
