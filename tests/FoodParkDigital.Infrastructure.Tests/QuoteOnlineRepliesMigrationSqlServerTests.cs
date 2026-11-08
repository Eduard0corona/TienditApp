using FoodParkDigital.Domain.Common.ValueObjects;
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
/// AddQuoteOnlineReplies (Fase 29G, ADR 0061): whether the customer answers the quote from its link, and that answer in the
/// rule of the answers. The version before it keeps saving its businesses (on, by the column's default) and the answers it
/// records; the database takes an answer from the link and still refuses a channel nobody writes; rolling back stops with error
/// 50055 once a customer answered from the link or a business turned it off, and otherwise drops only what it added.
/// </summary>
public class QuoteOnlineRepliesMigrationSqlServerTests
{
    // The migration right before AddQuoteOnlineReplies, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261102073115_AddAppointmentWorkOrder";
    private const string Migration = "20261102151555_AddQuoteOnlineReplies";
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingItsBusinessesAndTheAnswersItRecords_WithTheAnswersFromTheLinkOn()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version registers a business and records an answer, unaware of the new column.
        Guid tenantId;
        Guid quoteId;
        await using (var before = BeforeQuoteOnlineReplies.CreateContext(database))
        {
            var (tenant, settings) = Business();
            var quote = SentQuote(tenant.Id, 1);
            quote.Accept(Today, QuoteReplyChannel.InPerson, "En el local", Now);
            before.AddRange(tenant, settings, quote);
            await before.SaveChangesAsync();
            (tenantId, quoteId) = (tenant.Id, quote.Id);
        }

        await using var context = BeforeOrderCancellationEmails.CreateContext(database, tenantId);
        Assert.True((await context.TenantSettings.SingleAsync()).QuoteOnlineReplies);
        var saved = await context.Quotes.SingleAsync(q => q.Id == quoteId);
        Assert.Equal((QuoteStatus.Accepted, QuoteReplyChannel.InPerson), (saved.Status, saved.ReplyChannel!.Value));
    }

    [SqlServerFact]
    public async Task TheDatabase_ShouldTakeAnAnswerFromTheLink_AndRefuseAChannelNobodyWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, quoteId) = await SeedAsync(database, answeredFromTheLink: true);

        await using var check = BeforeOrderCancellationEmails.CreateContext(database, tenantId);
        var saved = await check.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId);
        Assert.Equal((QuoteStatus.Accepted, QuoteReplyChannel.Link, "Los espero"), (saved.Status, saved.ReplyChannel!.Value, saved.CustomerResponse));

        var refused = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(
            $"UPDATE [Quotes] SET [ReplyChannel] = 5 WHERE [Id] = '{quoteId}'"));
        Assert.Contains("CK_Quotes_Reply", refused?.Message);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAnsweredFromTheLinkOrTurnedOff_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, quoteId) = await SeedAsync(database, answeredFromTheLink: true);

        // A customer answered from the link.
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50055 }, stopped?.Message ?? "it was not stopped");
        await using (var context = BeforeOrderCancellationEmails.CreateContext(database, tenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(QuoteReplyChannel.Link, (await context.Quotes.SingleAsync(q => q.Id == quoteId)).ReplyChannel);
            // Changed on purpose, so only the business that turned it off stops it next.
            await context.Database.ExecuteSqlRawAsync("UPDATE [Quotes] SET [ReplyChannel] = 0");
            var settings = await context.TenantSettings.SingleAsync();
            settings.SetQuoteOnlineReplies(false);
            await context.SaveChangesAsync();
        }

        // A business turned it off.
        stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50055 }, stopped?.Message ?? "it was not stopped");
        await using (var context = BeforeOrderCancellationEmails.CreateContext(database, tenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.False((await context.TenantSettings.SingleAsync()).QuoteOnlineReplies);
            await context.Database.ExecuteSqlRawAsync("UPDATE [TenantSettings] SET [QuoteOnlineReplies] = 1");
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeQuoteOnlineReplies.CreateContext(database, tenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'QuoteOnlineReplies'
                """).SingleAsync());
            // The rule of the answers is the one before it again: an answer from the link no longer fits.
            var refused = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(
                $"UPDATE [Quotes] SET [ReplyChannel] = 4 WHERE [Id] = '{quoteId}'"));
            Assert.Contains("CK_Quotes_Reply", refused?.Message);
            // The business and its quote stay.
            Assert.Equal((1, 1), (await context.TenantSettings.IgnoreQueryFilters().CountAsync(), await context.Quotes.IgnoreQueryFilters().CountAsync()));
        }

        // Rolling back again does nothing, and forward again the answers from the link are on.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeOrderCancellationEmails.CreateContext(database, tenantId);
        Assert.True((await forward.TenantSettings.SingleAsync()).QuoteOnlineReplies);
    }

    private static (Tenant Tenant, TenantSettings Settings) Business()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        return (tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments));
    }

    private static Quote SentQuote(Guid tenantId, int number)
    {
        var quote = Quote.Start(tenantId, Folios.QuoteNumber(number), new QuoteCustomer(null, "Valeria", "5522107765", null), null, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        quote.Send(Today, new string((char)('p' + number), 43), null, Now);
        return quote;
    }

    /// <summary>A trade with a quote its customer answered, from its link or as the business recorded it.</summary>
    private static async Task<(Guid TenantId, Guid QuoteId)> SeedAsync(SqlServerTestDatabase database, bool answeredFromTheLink)
    {
        var (tenant, settings) = Business();
        var quote = SentQuote(tenant.Id, 1);
        if (answeredFromTheLink)
            quote.ReplyFromLink(accepted: true, Today, "Los espero", Now);
        else
            quote.Accept(Today, QuoteReplyChannel.WhatsApp, null, Now);
        await using var seed = BeforeOrderCancellationEmails.CreateContext(database, tenant.Id);
        seed.AddRange(tenant, settings, quote);
        await seed.SaveChangesAsync();
        return (tenant.Id, quote.Id);
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
