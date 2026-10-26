using FoodParkDigital.Domain.Customers.Entities;
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
/// AddCfdiEmails (Fase 28E, ADR 0049): the emails of the CFDI of the orders' invoices to their customers, and the addresses
/// the invoice and the customer keep for them. The version deployed before keeps storing invoices and customers (without an
/// address), and forgetting a customer's fiscal data, also one with an address; the checks refuse what the domain never
/// writes; and rolling back stops with error 50041 once there is an email or an address, and otherwise drops only what it
/// added.
/// </summary>
public class CfdiEmailsMigrationSqlServerTests
{
    // The migration right before AddCfdiEmails, where its rollback.sql takes the database.
    private const string BeforeEmails = "20261024084435_AddCreditNotes";
    private const string Migration = "20261026032721_AddCfdiEmails";

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepStoringInvoicesAndCustomers_WithoutAnAddress()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeEmails);
        var seed = await SeedAsync(database);
        await AddCustomerAsync(BeforeCfdiEmails.CreateContext(database, seed.TenantId), seed.TenantId, "Karla");

        await MigrateAsync(database, Migration);

        // The deployed version keeps invoicing and keeping fiscal data.
        await AddCustomerAsync(BeforeCfdiEmails.CreateContext(database, seed.TenantId), seed.TenantId, "Pepe");
        await using (var previous = BeforeCfdiEmails.CreateContext(database, seed.TenantId))
        {
            previous.OrderInvoices.Add(StampedInvoice(seed));
            await previous.SaveChangesAsync();
        }

        await using var context = BeforeSelfInvoicing.CreateContext(database, seed.TenantId);
        Assert.All(await context.Customers.Where(c => !c.IsPublicGeneral).ToListAsync(), customer =>
        {
            Assert.NotNull(customer.FiscalData);
            Assert.Null(customer.InvoiceEmail);
        });
        Assert.Null((await context.OrderInvoices.SingleAsync()).ReceiverEmail);
        Assert.Empty(await context.CfdiEmails.ToListAsync());

        // It forgets the fiscal data of a customer this version gave an address to: it does not know the address, and no
        // check of the database refuses it (the domain of this version forgets both).
        var withAddress = await AddCustomerAsync(BeforeSelfInvoicing.CreateContext(database, seed.TenantId), seed.TenantId, "Lupita", "facturas@ejemplo.mx");
        await using (var previous = BeforeCfdiEmails.CreateContext(database, seed.TenantId))
        {
            (await previous.Customers.SingleAsync(c => c.Id == withAddress)).SetFiscalData(null, null);
            await previous.SaveChangesAsync();
        }
        await using var after = BeforeSelfInvoicing.CreateContext(database, seed.TenantId);
        Assert.Null((await after.Customers.AsNoTracking().SingleAsync(c => c.Id == withAddress)).FiscalData);
    }

    [SqlServerFact]
    public async Task Database_ShouldKeepTheEmailsAndAddresses_AndRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var invoice = StampedInvoice(seed);
        invoice.SendTo("karla@ejemplo.mx");
        var email = CfdiEmail.Request(invoice, invoice.ReceiverEmail, null, DateTimeOffset.UtcNow);
        var customerId = await AddCustomerAsync(BeforeSelfInvoicing.CreateContext(database, seed.TenantId), seed.TenantId, "Karla", "facturas@ejemplo.mx");
        await using (var context = BeforeSelfInvoicing.CreateContext(database, seed.TenantId))
        {
            context.OrderInvoices.Add(invoice);
            context.CfdiEmails.Add(email);
            await context.SaveChangesAsync();
        }

        await using (var context = BeforeSelfInvoicing.CreateContext(database, seed.TenantId))
        {
            var sent = await context.CfdiEmails.SingleAsync();
            Assert.Equal((invoice.Id, CfdiEmailDocument.Invoice, invoice.Id, "karla@ejemplo.mx", true),
                (sent.OrderInvoiceId, sent.Document, sent.CfdiId, sent.To, sent.IsPending));
            sent.AttemptFailed(DateTimeOffset.UtcNow);
            sent.Sent(withoutPdf: true, DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
            Assert.Equal("karla@ejemplo.mx", (await context.OrderInvoices.SingleAsync()).ReceiverEmail);
            Assert.Equal("facturas@ejemplo.mx", (await context.Customers.SingleAsync(c => c.Id == customerId)).InvoiceEmail);
        }

        // Another business does not see them.
        await using (var other = BeforeSelfInvoicing.CreateContext(database, seed.OtherTenantId))
            Assert.Empty(await other.CfdiEmails.ToListAsync());

        FormattableString[] never =
        [
            $"UPDATE [CfdiEmails] SET [Document] = 3 WHERE [Id] = {email.Id}",
            // The email of an invoice carries that invoice.
            $"UPDATE [CfdiEmails] SET [CfdiId] = NEWID() WHERE [Id] = {email.Id}",
            // Sent and refused at once, refused without why, or why without being refused.
            $"UPDATE [CfdiEmails] SET [FailedAt] = SYSDATETIMEOFFSET(), [Failure] = 0 WHERE [Id] = {email.Id}",
            $"UPDATE [CfdiEmails] SET [SentAt] = NULL, [SentWithoutPdf] = 0, [FailedAt] = SYSDATETIMEOFFSET() WHERE [Id] = {email.Id}",
            $"UPDATE [CfdiEmails] SET [Failure] = 1 WHERE [Id] = {email.Id}",
            $"UPDATE [CfdiEmails] SET [SentAt] = NULL, [SentWithoutPdf] = 0, [FailedAt] = SYSDATETIMEOFFSET(), [Failure] = 4 WHERE [Id] = {email.Id}",
            // Without the PDF only once it went out.
            $"UPDATE [CfdiEmails] SET [SentAt] = NULL WHERE [Id] = {email.Id}",
            $"UPDATE [CfdiEmails] SET [FailedAttempts] = 0 WHERE [Id] = {email.Id}",
            $"UPDATE [CfdiEmails] SET [LastFailedAttemptAt] = NULL WHERE [Id] = {email.Id}"
        ];
        foreach (var sql in never)
        {
            var refused = await Record.ExceptionAsync(() => ExecuteAsync(database, sql));
            Assert.True(refused is SqlException { Number: 547 }, $"{sql.Format}: {refused?.Message ?? "it was not refused"}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithAnEmailOrAnAddress_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var invoice = StampedInvoice(seed);
        await using (var context = BeforeSelfInvoicing.CreateContext(database, seed.TenantId))
        {
            context.OrderInvoices.Add(invoice);
            context.CfdiEmails.Add(CfdiEmail.Request(invoice, "karla@ejemplo.mx", seed.UserId, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }
        var customerId = await AddCustomerAsync(BeforeSelfInvoicing.CreateContext(database, seed.TenantId), seed.TenantId, "Karla", "facturas@ejemplo.mx");

        async Task StopsAsync(string what)
        {
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeEmails));
            Assert.True(stopped is SqlException { Number: 50041 }, $"{what}: {stopped?.Message ?? "it was not stopped"}");
        }

        // An email, the address of a customer or of an invoice: each one alone stops it.
        await ExecuteAsync(database, $"UPDATE [Customers] SET [InvoiceEmail] = NULL WHERE [Id] = {customerId}");
        await ExecuteAsync(database, $"UPDATE [OrderInvoices] SET [ReceiverEmail] = NULL");
        await StopsAsync("An email alone");
        await ExecuteAsync(database, $"DELETE FROM [CfdiEmails]");
        await ExecuteAsync(database, $"UPDATE [Customers] SET [InvoiceEmail] = N'facturas@ejemplo.mx' WHERE [Id] = {customerId}");
        await StopsAsync("The address of a customer alone");
        await ExecuteAsync(database, $"UPDATE [Customers] SET [InvoiceEmail] = NULL WHERE [Id] = {customerId}");
        await ExecuteAsync(database, $"UPDATE [OrderInvoices] SET [ReceiverEmail] = N'karla@ejemplo.mx'");
        await StopsAsync("The address of an invoice alone");
        await using (var context = BeforeSelfInvoicing.CreateContext(database))
            Assert.Equal(Migration, await LatestMigrationAsync(context));

        await ExecuteAsync(database, $"UPDATE [OrderInvoices] SET [ReceiverEmail] = NULL");
        await MigrateAsync(database, BeforeEmails);
        await using (var context = BeforeSelfInvoicing.CreateContext(database))
        {
            Assert.Equal(BeforeEmails, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 1, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'CfdiEmails'"),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.OrderInvoices') AND [name] = N'ReceiverEmail'
                    """),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.Customers') AND [name] = N'InvoiceEmail'
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OrderInvoices]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Customers] WHERE [FiscalData_Rfc] IS NOT NULL")));
        }

        // Rolling back again does nothing, and forward again it is there, empty.
        await MigrateAsync(database, BeforeEmails);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeSelfInvoicing.CreateContext(database, seed.TenantId);
        Assert.Empty(await forward.CfdiEmails.ToListAsync());
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid OrderId, Guid UserId);

    /// <summary>A business with a $200 order paid in cash (and another business), as any version stores them.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeCfdiEmails.CreateContext(database);
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
        return new Seed(tenants[0].Id, tenants[1].Id, order.Id, Guid.NewGuid());
    }

    private static OrderInvoice StampedInvoice(Seed seed)
    {
        var invoice = OrderInvoice.Request(seed.TenantId, seed.OrderId, "F", 1, Receiver(), "01", "{\"serie\":\"F\"}",
            172.41m, 27.59m, 200m, seed.UserId, DateTimeOffset.UtcNow.AddDays(-1));
        invoice.Stamp("5F0B7C1E-2A3D-4E5F-8A9B-0C1D2E3F4A5B", "pac-1", DateTimeOffset.UtcNow.AddDays(-1), "f-1.xml");
        return invoice;
    }

    private static CfdiReceiver Receiver() => CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03");

    /// <summary>A customer with fiscal data, kept by <paramref name="context"/> (and disposed with it).</summary>
    private static async Task<Guid> AddCustomerAsync(ApplicationDbContext context, Guid tenantId, string name, string? invoiceEmail = null)
    {
        await using (context)
        {
            var customer = new Customer(tenantId, name, null, null);
            customer.SetFiscalData(Receiver(), invoiceEmail);
            context.Customers.Add(customer);
            await context.SaveChangesAsync();
            return customer.Id;
        }
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
