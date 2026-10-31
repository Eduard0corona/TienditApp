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
/// AddCfdiCancellationNotices (Fase 28N, ADR 0059): an email of a CFDI says whether it carries its files or the notice that
/// the SAT cancelled it. The emails already sent, and those the previous version keeps asking for, carry the files; the
/// database refuses what the domain never writes, and rolling back stops with error 50047 while a notice exists.
/// </summary>
public class CfdiCancellationNoticesMigrationSqlServerTests
{
    // The migration right before AddCfdiCancellationNotices, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261030181829_AddCfdiWhatsApp";
    private const string Migration = "20261030215308_AddCfdiCancellationNotices";

    [SqlServerFact]
    public async Task Notice_ShouldBeSavedAfter_TheEmailsCarryTheirFiles_AndTheDatabaseRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var seed = await SeedAsync(database);
        var sent = CfdiEmail.Request(seed.Invoice, "karla@ejemplo.mx", null, DateTimeOffset.UtcNow);
        sent.Sent(withoutPdf: true, DateTimeOffset.UtcNow);
        await using (var previous = BeforeCfdiCancellationNotices.CreateContext(database, seed.TenantId))
        {
            previous.CfdiEmails.Add(sent);
            await previous.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        // The deployed version keeps asking for emails while this one is published.
        var byHand = CfdiEmail.Request(seed.Invoice, "karla@ejemplo.mx", seed.UserId, DateTimeOffset.UtcNow);
        await using (var previous = BeforeCfdiCancellationNotices.CreateContext(database, seed.TenantId))
        {
            previous.CfdiEmails.Add(byHand);
            await previous.SaveChangesAsync();
        }
        var cancelled = await CancelAsync(database, seed);
        var notice = CfdiEmail.RequestCancellationNotice(cancelled, "karla@ejemplo.mx", DateTimeOffset.UtcNow);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.All(await context.CfdiEmails.ToListAsync(), email => Assert.Equal(CfdiEmailKind.Documents, email.Kind));
            Assert.True((await context.CfdiEmails.SingleAsync(e => e.Id == sent.Id)).SentWithoutPdf);

            context.CfdiEmails.Add(notice);
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var saved = await context.CfdiEmails.SingleAsync(e => e.Id == notice.Id);
            Assert.Equal((CfdiEmailKind.Cancellation, CfdiEmailDocument.Invoice, seed.Invoice.Id, (Guid?)null),
                (saved.Kind, saved.Document, saved.CfdiId, saved.RequestedByUserId));
            saved.Sent(withoutPdf: false, DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        // Another business does not see them.
        await using (var foreign = database.CreateContext(seed.OtherTenantId))
            Assert.Empty(await foreign.CfdiEmails.ToListAsync());

        (FormattableString Sql, int Number)[] never =
        [
            // Neither the files nor a notice.
            ($"UPDATE [CfdiEmails] SET [Kind] = 2 WHERE [Id] = {byHand.Id}", 547),
            // A notice carries no files, and only TienditApp asks for it.
            ($"UPDATE [CfdiEmails] SET [SentWithoutPdf] = 1 WHERE [Id] = {notice.Id}", 547),
            ($"UPDATE [CfdiEmails] SET [Kind] = 1 WHERE [Id] = {byHand.Id}", 547),
            ($"UPDATE [CfdiEmails] SET [Kind] = 1 WHERE [Id] = {sent.Id}", 547),
            ($"UPDATE [CfdiEmails] SET [Kind] = NULL WHERE [Id] = {sent.Id}", 515)
        ];
        foreach (var (sql, number) in never)
        {
            var refused = await Record.ExceptionAsync(() => ExecuteAsync(database, sql));
            Assert.True(refused is SqlException e && e.Number == number, $"{sql.Format}: {refused?.Message ?? "it was not refused"}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWhileANoticeExists_AndThenTakeTheColumnAway()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var cancelled = await CancelAsync(database, seed);
        var notice = CfdiEmail.RequestCancellationNotice(cancelled, "karla@ejemplo.mx", DateTimeOffset.UtcNow);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.CfdiEmails.AddRange(notice, CfdiEmail.Request(seed.Complement, "karla@ejemplo.mx", null, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        // A notice stops it, even one that failed.
        await ExecuteAsync(database, $"UPDATE [CfdiEmails] SET [FailedAt] = SYSDATETIMEOFFSET(), [Failure] = 1 WHERE [Id] = {notice.Id}");
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50047 }, stopped?.Message ?? "it was not stopped");
        await using (var context = database.CreateContext())
            Assert.Equal(Migration, await LatestMigrationAsync(context));

        await ExecuteAsync(database, $"DELETE FROM [CfdiEmails] WHERE [Id] = {notice.Id}");
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeCfdiCancellationNotices.CreateContext(database, seed.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.CfdiEmails') AND [name] = N'Kind'
                """));
            // The emails of the files stay.
            Assert.Equal(seed.Complement.Id, (await context.CfdiEmails.SingleAsync()).CfdiId);
        }

        // Rolling back again does nothing, and forward again the notice is saved.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.Equal(CfdiEmailKind.Documents, (await forward.CfdiEmails.SingleAsync()).Kind);
        forward.CfdiEmails.Add(CfdiEmail.RequestCancellationNotice(cancelled, "karla@ejemplo.mx", DateTimeOffset.UtcNow));
        await forward.SaveChangesAsync();
        Assert.Equal(1, await forward.CfdiEmails.CountAsync(e => e.Kind == CfdiEmailKind.Cancellation));
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid UserId, OrderInvoice Invoice, PaymentComplement Complement);

    /// <summary>
    /// Two businesses; the first with the stamped PPD invoice of an order and its stamped payment complement, as any version
    /// stores them.
    /// </summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeCfdiCancellationNotices.CreateContext(database);
        var tenants = Enumerable.Range(0, 2).Select(_ =>
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        }).ToList();
        foreach (var tenant in tenants)
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        var tenantId = tenants[0].Id;
        var userId = Guid.NewGuid();
        var order = new Order(tenantId, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla",
            "+525512345678", null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        var payment = order.RegisterPayment(200m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-1), null, null, null, null);
        var invoice = OrderInvoice.Request(
            tenantId, order.Id, "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"),
            "99", "{}", 172.41m, 27.59m, 200m, userId, DateTimeOffset.UtcNow.AddDays(-2), "PPD");
        invoice.Stamp(Guid.NewGuid().ToString(), "pac-1", DateTimeOffset.UtcNow.AddDays(-2), "invoices/1.xml");
        var complement = PaymentComplement.Request(invoice, payment, "P", 1, 1, 200m, "{}", null, DateTimeOffset.UtcNow.AddDays(-1));
        complement.Stamp(Guid.NewGuid().ToString(), "pac-p1", DateTimeOffset.UtcNow.AddDays(-1), "complements/1.xml");
        context.Orders.Add(order);
        context.AddRange(invoice, complement);
        await context.SaveChangesAsync();
        return new Seed(tenantId, tenants[1].Id, userId, invoice, complement);
    }

    /// <summary>The SAT cancels the seeded invoice (errors without replacement), as the version after the migration does.</summary>
    private static async Task<OrderInvoice> CancelAsync(SqlServerTestDatabase database, Seed seed)
    {
        await using var context = database.CreateContext(seed.TenantId);
        var invoice = await context.OrderInvoices.SingleAsync(i => i.Id == seed.Invoice.Id);
        invoice.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, seed.UserId, DateTimeOffset.UtcNow);
        invoice.Cancel(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
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
        await context.Database.ExecuteSqlAsync(sql);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql)
        => context.Database.SqlQuery<int>(sql).SingleAsync();

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
