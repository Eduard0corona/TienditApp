using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
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
/// AddCfdiWhatsApp (Fase 28M, ADR 0058): the WhatsApp message of a CFDI goes through the queue with which CFDI it carries, of
/// which invoice and its link; the invoice keeps its WhatsApp and the business its template. The messages already queued
/// stay as they are, the database refuses what the domain never writes, and rolling back stops with error 50046 while any
/// of it exists.
/// </summary>
public class CfdiWhatsAppMigrationSqlServerTests
{
    // The migration right before AddCfdiWhatsApp, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261029163102_AddGlobalInvoiceWriteOffNotes";
    private const string Migration = "20261030181829_AddCfdiWhatsApp";

    [SqlServerFact]
    public async Task CfdiMessage_ShouldBeSavedAfter_TheQueuedOnesStay_AndTheDatabaseRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        // A notice already in the queue when the migration runs (of the business seeded after it).
        var queuedId = Guid.NewGuid();
        await ExecuteAsync(database, $"""
            INSERT INTO [OutgoingMessages] ([Id], [TenantId], [Purpose], [Phone], [Body], [LanguageCode], [ParametersJson], [Status],
                [Attempts], [NextAttemptAt], [LockToken], [CreatedAt])
            VALUES ({queuedId}, NEWID(), 2, '+525512345678', 'Tu cita es mañana', 'es_MX', '[]', 0, 0, SYSDATETIMEOFFSET(),
                NEWID(), SYSDATETIMEOFFSET())
            """);

        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        await ExecuteAsync(database, $"UPDATE [OutgoingMessages] SET [TenantId] = {seed.TenantId} WHERE [Id] = {queuedId}");
        var message = CfdiMessage(seed.Invoice, "+525598765432", null);
        var byHand = CfdiMessage(seed.Invoice, "+525598765432", Guid.NewGuid());
        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            var queued = await context.OutgoingMessages.SingleAsync(m => m.Id == queuedId);
            Assert.Equal((MessagePurpose.AppointmentNotice, (Guid?)null, (string?)null), (queued.Purpose, queued.CfdiId, queued.LinkToken));

            context.OutgoingMessages.AddRange(message, byHand);
            var invoice = await context.OrderInvoices.SingleAsync(i => i.Id == seed.Invoice.Id);
            invoice.SendWhatsAppTo("+525598765432");
            var profile = InvoicingProfile.Create(seed.TenantId);
            profile.UseWhatsAppTemplate(Guid.NewGuid());
            context.InvoicingProfiles.Add(profile);
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var saved = await context.OutgoingMessages.SingleAsync(m => m.Id == message.Id);
            Assert.Equal(
                (MessagePurpose.CfdiDocument, (CfdiEmailDocument?)CfdiEmailDocument.Invoice, (Guid?)seed.Invoice.Id, (Guid?)seed.Invoice.Id,
                    (Guid?)null, message.LinkToken),
                (saved.Purpose, saved.CfdiDocument, saved.CfdiId, saved.OrderInvoiceId, saved.RequestedByUserId, saved.LinkToken));
            Assert.Equal(byHand.RequestedByUserId, (await context.OutgoingMessages.SingleAsync(m => m.Id == byHand.Id)).RequestedByUserId);
            Assert.Equal("+525598765432", (await context.OrderInvoices.SingleAsync(i => i.Id == seed.Invoice.Id)).ReceiverWhatsApp);
            Assert.NotNull((await context.InvoicingProfiles.SingleAsync()).WhatsAppTemplateId);
        }
        // Another business does not see them.
        await using (var foreign = database.CreateContext(seed.OtherTenantId))
        {
            Assert.Empty(await foreign.OutgoingMessages.ToListAsync());
            Assert.Empty(await foreign.InvoicingProfiles.ToListAsync());
        }

        (FormattableString Sql, int Number)[] never =
        [
            // A CFDI's message without its link, or without its CFDI.
            ($"UPDATE [OutgoingMessages] SET [LinkToken] = NULL WHERE [Id] = {message.Id}", 547),
            ($"UPDATE [OutgoingMessages] SET [CfdiId] = NULL WHERE [Id] = {message.Id}", 547),
            // Any other message with a CFDI, a link or someone who asked for it.
            ($"UPDATE [OutgoingMessages] SET [CfdiId] = {seed.Invoice.Id} WHERE [Id] = {queuedId}", 547),
            ($"UPDATE [OutgoingMessages] SET [RequestedByUserId] = NEWID() WHERE [Id] = {queuedId}", 547),
            ($"UPDATE [OutgoingMessages] SET [Purpose] = 2 WHERE [Id] = {message.Id}", 547),
            // Neither an invoice, a complement nor a credit note.
            ($"UPDATE [OutgoingMessages] SET [CfdiDocument] = 3 WHERE [Id] = {message.Id}", 547),
            // The message of an invoice carries that invoice.
            ($"UPDATE [OutgoingMessages] SET [CfdiId] = NEWID() WHERE [Id] = {message.Id}", 547),
            // A link of another shape.
            ($"UPDATE [OutgoingMessages] SET [LinkToken] = 'corto' WHERE [Id] = {message.Id}", 547),
            // One link is one message.
            ($"UPDATE [OutgoingMessages] SET [LinkToken] = {message.LinkToken} WHERE [Id] = {byHand.Id}", 2601),
            // An invoice that is not there.
            ($"UPDATE [OutgoingMessages] SET [CfdiDocument] = 1, [OrderInvoiceId] = NEWID() WHERE [Id] = {message.Id}", 547)
        ];
        foreach (var (sql, number) in never)
        {
            var refused = await Record.ExceptionAsync(() => ExecuteAsync(database, sql));
            Assert.True(refused is SqlException e && e.Number == number, $"{sql.Format}: {refused?.Message ?? "it was not refused"}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWhileAnyOfItExists_AndThenTakeTheColumnsAway()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var message = CfdiMessage(seed.Invoice, "+525598765432", null);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.OutgoingMessages.Add(message);
            await context.SaveChangesAsync();
        }

        // A CFDI's message, even one that failed; an invoice with its WhatsApp; a business with its template: each one stops it.
        FormattableString[] leaveOnlyOne =
        [
            $"UPDATE [OutgoingMessages] SET [Status] = 2 WHERE [Id] = {message.Id}",
            $"DELETE FROM [OutgoingMessages] WHERE [Id] = {message.Id}; UPDATE [OrderInvoices] SET [ReceiverWhatsApp] = '+525598765432'",
            $"""
            UPDATE [OrderInvoices] SET [ReceiverWhatsApp] = NULL;
            INSERT INTO [InvoicingProfiles] ([Id], [TenantId], [Serie], [PaymentSerie], [CreditNoteSerie], [SelfInvoicing], [WhatsAppTemplateId], [CreatedAt])
            VALUES (NEWID(), {seed.TenantId}, 'F', 'P', 'NC', 0, NEWID(), SYSDATETIMEOFFSET())
            """
        ];
        foreach (var sql in leaveOnlyOne)
        {
            await ExecuteAsync(database, sql);
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
            Assert.True(stopped is SqlException { Number: 50046 }, $"{sql.Format}: {stopped?.Message ?? "it was not stopped"}");
            await using var context = database.CreateContext();
            Assert.Equal(Migration, await LatestMigrationAsync(context));
        }

        await ExecuteAsync(database, $"UPDATE [InvoicingProfiles] SET [WhatsAppTemplateId] = NULL");
        await MigrateAsync(database, BeforeMigration);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE ([object_id] = OBJECT_ID(N'dbo.OutgoingMessages')
                        AND [name] IN (N'CfdiDocument', N'CfdiId', N'OrderInvoiceId', N'RequestedByUserId', N'LinkToken'))
                    OR ([object_id] = OBJECT_ID(N'dbo.OrderInvoices') AND [name] = N'ReceiverWhatsApp')
                    OR ([object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND [name] = N'WhatsAppTemplateId')
                """));
        }

        // Rolling back again does nothing, and forward again the CFDI's message is saved.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        forward.OutgoingMessages.Add(CfdiMessage(seed.Invoice, "+525598765432", null));
        await forward.SaveChangesAsync();
        Assert.Equal(1, await forward.OutgoingMessages.CountAsync(m => m.CfdiId == seed.Invoice.Id));
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, OrderInvoice Invoice);

    /// <summary>Two businesses; the first with a stamped invoice of an order.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var tenants = Enumerable.Range(0, 2).Select(_ =>
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        }).ToList();
        foreach (var tenant in tenants)
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        var tenantId = tenants[0].Id;
        var order = new Order(tenantId, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Karla",
            "+525512345678", null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        order.RegisterPayment(200m, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow.AddDays(-1), null, null, null, null);
        var invoice = OrderInvoice.Request(
            tenantId, order.Id, "F", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"),
            "01", "{}", 172.41m, 27.59m, 200m, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
        invoice.Stamp(Guid.NewGuid().ToString(), "pac-1", DateTimeOffset.UtcNow.AddDays(-1), "invoices/1.xml");
        context.Orders.Add(order);
        context.Add(invoice);
        await context.SaveChangesAsync();
        return new Seed(tenantId, tenants[1].Id, invoice);
    }

    private static OutgoingMessage CfdiMessage(OrderInvoice invoice, string phone, Guid? requestedBy)
        => OutgoingMessage.ForCfdi(
            invoice, phone, "Karla Fuente Nolasco", "Hola: Tienda Luna te envía la factura F-1.", null, "es_MX", [],
            Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray())
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            requestedBy, DateTimeOffset.UtcNow);

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
