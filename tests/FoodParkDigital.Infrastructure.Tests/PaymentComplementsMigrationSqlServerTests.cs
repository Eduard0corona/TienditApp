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
/// AddPaymentComplements (Fase 28C, ADR 0047): invoices in installments (PPD with form 99), the payment complements of their
/// payments and the serie of the complements. The version deployed before keeps storing profiles (they take «P») and
/// invoices paid in one exhibition; a payment has one live complement and an invoice counts each installment once; the
/// checks refuse what the domain never writes; and rolling back stops with error 50039 once there is a complement or an
/// invoice in installments, and otherwise drops only what it added.
/// </summary>
public class PaymentComplementsMigrationSqlServerTests
{
    // The migration right before AddPaymentComplements, where its rollback.sql takes the database.
    private const string BeforeComplements = "20261023175930_AddGlobalInvoices";
    private const string Migration = "20261024014559_AddPaymentComplements";

    [SqlServerFact]
    public async Task PaymentSerie_ShouldBePForEveryProfile_WhileThePreviousVersionStoresThem()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeComplements);
        var seed = await SeedAsync(database);
        await using (var previous = BeforePaymentComplements.CreateContext(database, seed.TenantId))
        {
            previous.InvoicingProfiles.Add(InvoicingProfile.Create(seed.TenantId));
            await previous.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        // The deployed version keeps creating profiles and invoices paid in one exhibition.
        await using (var previous = BeforePaymentComplements.CreateContext(database, seed.OtherTenantId))
        {
            previous.InvoicingProfiles.Add(InvoicingProfile.Create(seed.OtherTenantId));
            await previous.SaveChangesAsync();
        }
        await AddAsync(database, seed, Invoice(seed, 1, "PUE", "01"));

        foreach (var tenantId in new[] { seed.TenantId, seed.OtherTenantId })
        {
            await using var context = BeforeCreditNotes.CreateContext(database, tenantId);
            Assert.Equal("P", (await context.InvoicingProfiles.SingleAsync()).PaymentSerie);
        }
        await using var check = BeforeCfdiEmails.CreateContext(database, seed.TenantId);
        Assert.Equal(CfdiInvoice.PaidInOneExhibition, (await check.OrderInvoices.SingleAsync()).PaymentMethod);
    }

    [SqlServerFact]
    public async Task Database_ShouldKeepOneLiveComplementPerPayment_EachInstallmentOnce_AndRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var invoice = Invoice(seed, 1, "PPD", "99");
        invoice.Stamp("5F0B7C1E-2A3D-4E5F-8A9B-0C1D2E3F4A5B", "pac-1", DateTimeOffset.UtcNow, "f-1.xml");
        await AddAsync(database, seed, invoice);

        var first = Complement(invoice, seed.FirstPayment, 1, 1, 200m);
        await AddAsync(database, seed, first);

        // Another live complement of the same payment, or a second first installment, is refused by the database…
        var again = await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddAsync(database, seed, Complement(invoice, seed.FirstPayment, 2, 2, 200m)));
        Assert.Contains("IX_PaymentComplements_TenantId_OrderPaymentId_Live", again.InnerException!.Message);
        var sameInstallment = await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddAsync(database, seed, Complement(invoice, seed.SecondPayment, 3, 1, 150m)));
        Assert.Contains("IX_PaymentComplements_OrderInvoiceId_Installment_Live", sameInstallment.InnerException!.Message);

        // …until the first one is refused: it stays, and the payment gets another one.
        await using (var context = BeforeCfdiEmails.CreateContext(database, seed.TenantId))
        {
            var refused = await context.PaymentComplements.SingleAsync(c => c.Id == first.Id);
            refused.Refuse("CRP20206 - La suma de ImpPagado no es igual a Monto.", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        var second = Complement(invoice, seed.FirstPayment, 4, 1, 200m);
        await AddAsync(database, seed, second);
        await using (var context = BeforeCfdiEmails.CreateContext(database, seed.TenantId))
            Assert.Equal(2, await context.PaymentComplements.CountAsync(c => c.OrderPaymentId == seed.FirstPayment.Id));

        FormattableString[] never =
        [
            $"UPDATE [OrderInvoices] SET [PaymentForm] = N'04' WHERE [Id] = {invoice.Id}",
            $"UPDATE [OrderInvoices] SET [PaymentMethod] = N'PUE' WHERE [Id] = {invoice.Id}",
            $"UPDATE [OrderInvoices] SET [PaymentMethod] = N'PPX' WHERE [Id] = {invoice.Id}",
            $"UPDATE [PaymentComplements] SET [Status] = 9 WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [Status] = 1 WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [Amount] = 0, [RemainingBalance] = [PreviousBalance] WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [RemainingBalance] = [RemainingBalance] + 1 WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [Amount] = [PreviousBalance] + 1, [RemainingBalance] = -1 WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [Installment] = 0 WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [Installment] = 1000 WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [PaymentForm] = '99' WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [PaymentForm] = '30' WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [CancellationMotive] = 4 WHERE [Id] = {second.Id}",
            $"UPDATE [PaymentComplements] SET [Folio] = 0 WHERE [Id] = {second.Id}"
        ];
        foreach (var sql in never)
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, sql))).Number);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithAnInvoiceInInstallmentsOrAComplement_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        // Refused, the invoice in installments still blocks: the old check would refuse it.
        var refused = Invoice(seed, 1, "PPD", "99");
        refused.Refuse("Rechazada", DateTimeOffset.UtcNow);
        await AddAsync(database, seed, refused);
        await AddAsync(database, seed, Invoice(seed, 2, "PUE", "01"));

        var used = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeComplements));
        Assert.Equal(50039, used.Number);
        await using (var context = database.CreateContext())
            Assert.Equal(Migration, await LatestMigrationAsync(context));

        await ExecuteAsync(database, $"DELETE FROM [OrderInvoices] WHERE [PaymentMethod] = N'PPD'");
        await MigrateAsync(database, BeforeComplements);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeComplements, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'PaymentComplements'"),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND [name] = N'PaymentSerie'
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OrderInvoices]")));
        }
        // The old check is back.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() =>
            ExecuteAsync(database, $"UPDATE [OrderInvoices] SET [PaymentMethod] = N'PPD', [PaymentForm] = N'99'"))).Number);

        // Rolling back again does nothing, and forward again it is there, empty.
        await MigrateAsync(database, BeforeComplements);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCfdiEmails.CreateContext(database, seed.TenantId);
        Assert.Empty(await forward.PaymentComplements.ToListAsync());
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid OrderId, Guid UserId, OrderPayment FirstPayment, OrderPayment SecondPayment);

    /// <summary>A business with a $200 order paid $50 and then $50 (and another business), as any version stores them.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforePaymentComplements.CreateContext(database);
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
        var first = order.RegisterPayment(50m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-2), null, null, null, null);
        var second = order.RegisterPayment(50m, OrderPaymentMethod.Transfer, DateTimeOffset.UtcNow.AddDays(-1), null, null, null, null);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return new Seed(tenants[0].Id, tenants[1].Id, order.Id, Guid.NewGuid(), first, second);
    }

    private static OrderInvoice Invoice(Seed seed, long folio, string method, string form)
        => OrderInvoice.Request(seed.TenantId, seed.OrderId, "F", folio,
            CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"), form, "{\"serie\":\"F\"}",
            172.41m, 27.59m, 200m, seed.UserId, DateTimeOffset.UtcNow, method);

    private static PaymentComplement Complement(
        OrderInvoice invoice, OrderPayment payment, long folio, int installment, decimal previousBalance)
        => PaymentComplement.Request(invoice, payment, "P", folio, installment, previousBalance, "{\"serie\":\"P\"}", null, DateTimeOffset.UtcNow);

    private static async Task AddAsync(SqlServerTestDatabase database, Seed seed, OrderInvoice invoice)
    {
        await using var context = BeforeCfdiEmails.CreateContext(database, seed.TenantId);
        context.OrderInvoices.Add(invoice);
        await context.SaveChangesAsync();
    }

    private static async Task AddAsync(SqlServerTestDatabase database, Seed seed, PaymentComplement complement)
    {
        await using var context = BeforeCfdiEmails.CreateContext(database, seed.TenantId);
        context.PaymentComplements.Add(complement);
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
