using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
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
/// AddQuoteDeliveries (Fase 29D, ADR 0053 point 16): the quotes sent from TienditApp. The version before it keeps queuing its
/// messages and saving businesses over the new columns; the queue refuses a quote's message without its version, or anyone
/// else's that says who asked for it; rolling back stops with error 50052 once a quote was sent from TienditApp or a business
/// picked a template for them, and otherwise drops only what it added and puts the queue's rule back, and a second run does
/// nothing.
/// </summary>
public class QuoteDeliveriesMigrationSqlServerTests
{
    // The migration right before AddQuoteDeliveries, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261101055834_AddQuotes";
    private const string Migration = "20261101181148_AddQuoteDeliveries";
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepQueuingItsMessages_AndSavingBusinesses_OverTheNewColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version signs up a business and queues a message, unaware of the new columns.
        Guid tenantId;
        Guid messageId;
        await using (var before = BeforeQuoteDeliveries.CreateContext(database))
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
            var notice = new OutgoingMessage(tenant.Id, MessagePurpose.AppointmentNotice, "+525522107765", "Valeria",
                "Tu visita quedó para mañana", null, "es_MX", [], Now);
            before.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments), notice);
            await before.SaveChangesAsync();
            (tenantId, messageId) = (tenant.Id, notice.Id);
        }

        await using var context = database.CreateContext(tenantId);
        Assert.Null((await context.TenantSettings.SingleAsync()).QuoteWhatsAppTemplateId);
        var message = await context.OutgoingMessages.SingleAsync(m => m.Id == messageId);
        Assert.Equal((MessagePurpose.AppointmentNotice, (Guid?)null, (Guid?)null), (message.Purpose, message.QuoteId, message.RequestedByUserId));
    }

    [SqlServerFact]
    public async Task TheQueue_ShouldRefuse_AQuotesMessageWithoutItsVersion_AndAnyOtherThatSaysWhoAskedForIt()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, quote) = await SeedAsync(database);

        await using (var context = database.CreateContext(tenantId))
        {
            context.OutgoingMessages.Add(OutgoingMessage.ForQuote(quote, "+525522107765", "Valeria", "Tu cotización", null, "es_MX", [],
                Guid.NewGuid(), Today, Now));
            context.OutgoingMessages.Add(new OutgoingMessage(tenantId, MessagePurpose.Campaign, "+525522107766", "Luis", "Promo", null, "es_MX", [], Now));
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenantId);
        var withoutVersion = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(
            "UPDATE [OutgoingMessages] SET [QuoteId] = NULL WHERE [Purpose] = 6"));
        Assert.Contains("CK_OutgoingMessages_Quote", withoutVersion?.Message);
        var withoutWho = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(
            "UPDATE [OutgoingMessages] SET [RequestedByUserId] = NULL WHERE [Purpose] = 6"));
        Assert.Contains("CK_OutgoingMessages_Quote", withoutWho?.Message);
        var campaignWithQuote = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(
            $"UPDATE [OutgoingMessages] SET [QuoteId] = '{quote.Id}' WHERE [Purpose] = 0"));
        Assert.Contains("CK_OutgoingMessages_Quote", campaignWithQuote?.Message);
        var campaignWithWho = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(
            "UPDATE [OutgoingMessages] SET [RequestedByUserId] = NEWID() WHERE [Purpose] = 0"));
        Assert.Contains("CK_OutgoingMessages_Cfdi", campaignWithWho?.Message);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAQuoteWasSentFromTienditApp_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, quote) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenantId))
        {
            // An email still pending: it would be lost too.
            context.QuoteEmails.Add(QuoteEmail.Request(quote, "valeria@correo.mx", Guid.NewGuid(), Today, Now));
            await context.SaveChangesAsync();
        }

        // The email stops it alone; then, removed on purpose, a quote's WhatsApp alone, and a business's template alone.
        foreach (var cleanUp in new[]
        {
            "DELETE FROM [QuoteEmails]; "
                + $"INSERT INTO [OutgoingMessages] ([Id], [TenantId], [Purpose], [Phone], [Body], [LanguageCode], [ParametersJson], [Status], [Attempts], [NextAttemptAt], [LockToken], [CreatedAt], [QuoteId], [RequestedByUserId]) "
                + $"VALUES (NEWID(), '{tenantId}', 6, '+525522107765', N'Tu cotización', 'es_MX', N'[]', 0, 0, SYSDATETIMEOFFSET(), NEWID(), SYSDATETIMEOFFSET(), '{quote.Id}', NEWID())",
            $"DELETE FROM [OutgoingMessages]; UPDATE [TenantSettings] SET [QuoteWhatsAppTemplateId] = NEWID()",
            "UPDATE [TenantSettings] SET [QuoteWhatsAppTemplateId] = NULL",
        })
        {
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
            Assert.True(stopped is SqlException { Number: 50052 }, stopped?.Message ?? "it was not stopped");
            await using var context = database.CreateContext(tenantId);
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync(cleanUp);
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeQuoteDeliveries.CreateContext(database))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>(
                $"SELECT OBJECT_ID(N'dbo.QuoteEmails') AS [Value]").SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE ([object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'QuoteWhatsAppTemplateId')
                    OR ([object_id] = OBJECT_ID(N'dbo.OutgoingMessages') AND [name] = N'QuoteId')
                """).SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_OutgoingMessages_Quote'").SingleAsync());
            // The queue's rule is the earlier one again: nobody but a CFDI's message says who asked for it.
            Assert.Contains("[RequestedByUserId] IS NULL AND [LinkToken] IS NULL", await context.Database.SqlQuery<string>(
                $"SELECT [definition] AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_OutgoingMessages_Cfdi'").SingleAsync());
            // The business and its quote stay.
            Assert.Equal((1, 1), (await context.TenantSettings.IgnoreQueryFilters().CountAsync(), await context.Quotes.IgnoreQueryFilters().CountAsync()));
        }

        // Rolling back again does nothing, and forward again it starts empty.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenantId);
        Assert.Equal((0, (Guid?)null), (await forward.QuoteEmails.CountAsync(), (await forward.TenantSettings.SingleAsync()).QuoteWhatsAppTemplateId));
    }

    /// <summary>A trade with a quote sent today.</summary>
    private static async Task<(Guid TenantId, Quote Quote)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var quote = Quote.Start(tenant.Id, Folios.QuoteNumber(1), new QuoteCustomer(null, "Valeria", "5522107765", null), null, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        quote.Send(Today, new string('q', 43), null, Now);
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments), quote);
        await seed.SaveChangesAsync();
        return (tenant.Id, quote);
    }

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
