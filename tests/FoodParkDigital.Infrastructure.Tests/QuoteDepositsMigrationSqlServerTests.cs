using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.OnlinePayments.Entities;
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
/// AddQuoteDeposits (Fase 31N, ADR 0094): the share of a deposit a business asks, the deposit each version asks once sent and
/// the quote a checkout pays. What exists asks none; the database refuses what the aggregates never write (a share outside 10
/// to 100, a deposit under $10, over the total or on a draft, a checkout of an order and a quote at once, or of neither); the
/// version before it keeps saving quotes and checkouts; rolling back stops with error 50062 while a business asks a deposit,
/// a version asks one or a checkout paid one, and otherwise drops only what it added.
/// </summary>
public class QuoteDepositsMigrationSqlServerTests
{
    // The migration right before AddQuoteDeposits, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261112165041_AddWhatsAppMessageMedia";
    private const string Migration = "20261113033616_AddQuoteDeposits";
    private const long ProviderUserId = 1234567890;
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 13);

    [SqlServerFact]
    public async Task WhatExists_ShouldAskNoDeposit_AndTheDatabaseShouldRefuseWhatTheAggregatesNeverWrite()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var seed = await SeedAsync(BeforeQuoteDeposits.CreateContext(database));

        await MigrateAsync(database, Migration);

        await using (var context = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId))
        {
            Assert.Null((await context.TenantSettings.AsNoTracking().SingleAsync()).QuoteDepositPercent);
            Assert.Null((await context.Quotes.AsNoTracking().SingleAsync(q => q.Id == seed.QuoteId)).DepositAmount);
            Assert.Null((await context.OnlineCheckouts.AsNoTracking().SingleAsync()).QuoteId);
        }

        // What the domain writes is kept: the share, a version sent asking 348, and the checkout that pays it.
        Guid asking;
        await using (var write = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId))
        {
            (await write.TenantSettings.SingleAsync()).SetQuoteDepositPercent(30);
            var quote = Sent(seed.TenantId, 2, deposit: 348m);
            write.Quotes.Add(quote);
            write.OnlineCheckouts.Add(OnlineCheckout.ForQuote(seed.TenantId, ProviderUserId, quote.Id, 348m, Now));
            await write.SaveChangesAsync();
            asking = quote.Id;
        }
        await using var check = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId);
        Assert.Equal(30, (await check.TenantSettings.AsNoTracking().SingleAsync()).QuoteDepositPercent);
        Assert.Equal(348m, (await check.Quotes.AsNoTracking().SingleAsync(q => q.Id == asking)).DepositAmount);
        Assert.Single(await check.OnlineCheckouts.AsNoTracking().Where(c => c.QuoteId == asking).ToListAsync());

        foreach (var (sql, constraint) in new (string, string)[]
        {
            ($"UPDATE [TenantSettings] SET [QuoteDepositPercent] = 5 WHERE [TenantId] = '{seed.TenantId}'", "CK_TenantSettings_QuoteDepositPercent"),
            ($"UPDATE [TenantSettings] SET [QuoteDepositPercent] = 101 WHERE [TenantId] = '{seed.TenantId}'", "CK_TenantSettings_QuoteDepositPercent"),
            ($"UPDATE [Quotes] SET [DepositAmount] = 9.99 WHERE [Id] = '{asking}'", "CK_Quotes_Deposit"),
            ($"UPDATE [Quotes] SET [DepositAmount] = [Total] + 0.01 WHERE [Id] = '{asking}'", "CK_Quotes_Deposit"),
            ($"UPDATE [Quotes] SET [DepositAmount] = 100 WHERE [Id] = '{seed.DraftId}'", "CK_Quotes_Deposit"),
            // An order's checkout that also pays a quote, and one of neither while it waits.
            ($"UPDATE [OnlineCheckouts] SET [QuoteId] = '{asking}' WHERE [Request] IS NOT NULL", "CK_OnlineCheckouts_Quote"),
            ($"UPDATE [OnlineCheckouts] SET [QuoteId] = NULL WHERE [QuoteId] = '{asking}'", "CK_OnlineCheckouts_Outcome"),
            // A quote that does not exist.
            ($"UPDATE [OnlineCheckouts] SET [QuoteId] = '{Guid.NewGuid()}' WHERE [QuoteId] = '{asking}'", "FK_OnlineCheckouts_Quotes_QuoteId"),
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlRawAsync(sql));
            Assert.Contains(constraint, error.Message);
        }
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingQuotesAndCheckouts_OverTheNewColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version, unaware of the columns.
        var seed = await SeedAsync(BeforeQuoteDeposits.CreateContext(database));

        await using var context = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId);
        var quote = await context.Quotes.AsNoTracking().SingleAsync(q => q.Id == seed.QuoteId);
        Assert.Equal((QuoteStatus.Sent, (decimal?)null), (quote.Status, quote.DepositAmount));
        Assert.Null((await context.OnlineCheckouts.AsNoTracking().SingleAsync()).QuoteId);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileADepositIsAskedOrPaid_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(BeforeAppointmentDeposits.CreateContext(database));
        Guid asking;
        await using (var write = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId))
        {
            (await write.TenantSettings.SingleAsync()).SetQuoteDepositPercent(30);
            var quote = Sent(seed.TenantId, 2, deposit: 348m);
            write.Quotes.Add(quote);
            write.OnlineCheckouts.Add(OnlineCheckout.ForQuote(seed.TenantId, ProviderUserId, quote.Id, 348m, Now));
            await write.SaveChangesAsync();
            asking = quote.Id;
        }

        // Each of the three alone stops it: the business's share, a version asking one, and a checkout of a quote.
        foreach (var leaveOnlyOne in new[]
        {
            $"DELETE FROM [OnlineCheckouts] WHERE [QuoteId] IS NOT NULL; UPDATE [Quotes] SET [DepositAmount] = NULL WHERE [Id] = '{asking}'",
            $"UPDATE [TenantSettings] SET [QuoteDepositPercent] = NULL; UPDATE [Quotes] SET [DepositAmount] = 348 WHERE [Id] = '{asking}'",
            $"UPDATE [Quotes] SET [DepositAmount] = NULL WHERE [Id] = '{asking}'"
        })
        {
            await using (var context = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId))
            {
                await context.Database.ExecuteSqlRawAsync(leaveOnlyOne);
                if (!await context.OnlineCheckouts.AnyAsync(c => c.QuoteId != null)
                    && !await context.Quotes.AnyAsync(q => q.DepositAmount != null)
                    && !await context.TenantSettings.AnyAsync(t => t.QuoteDepositPercent != null))
                {
                    context.OnlineCheckouts.Add(OnlineCheckout.ForQuote(seed.TenantId, ProviderUserId, asking, 348m, Now));
                    await context.SaveChangesAsync();
                }
            }
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
            Assert.True(stopped is SqlException { Number: 50062 }, stopped?.Message ?? "it was not stopped");
            await using var after = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId);
            Assert.Equal(Migration, await LatestMigrationAsync(after));
        }
        await using (var clear = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId))
            await clear.Database.ExecuteSqlRawAsync("DELETE FROM [OnlineCheckouts] WHERE [QuoteId] IS NOT NULL");

        // Without any, it drops only what it added: the quotes and the checkout of the order stay.
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeQuoteDeposits.CreateContext(database, seed.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            foreach (var (table, column) in new[] { ("TenantSettings", "QuoteDepositPercent"), ("Quotes", "DepositAmount"), ("OnlineCheckouts", "QuoteId") })
                Assert.Null(await context.Database.SqlQueryRaw<int?>($"SELECT COL_LENGTH(N'dbo.{table}', N'{column}') AS [Value]").SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.check_constraints
                WHERE [name] IN (N'CK_TenantSettings_QuoteDepositPercent', N'CK_Quotes_Deposit', N'CK_OnlineCheckouts_Quote')
                """).SingleAsync());
            // The outcome check is back as it was: a checkout waiting has its order.
            Assert.DoesNotContain("QuoteId", await context.Database.SqlQuery<string>($"""
                SELECT [definition] AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_OnlineCheckouts_Outcome'
                """).SingleAsync());
            Assert.Equal(3, await context.Quotes.AsNoTracking().CountAsync());
            Assert.Single(await context.OnlineCheckouts.AsNoTracking().ToListAsync());
        }

        // Rolling back again does nothing; forward again, nothing asks a deposit.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId);
        Assert.All(await forward.Quotes.AsNoTracking().ToListAsync(), quote => Assert.Null(quote.DepositAmount));
    }

    private sealed record Seed(Guid TenantId, Guid QuoteId, Guid DraftId);

    /// <summary>A trade with a version sent, a draft and the checkout of an order of its menu, saved by <paramref name="seed"/>.</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext seed)
    {
        await using (seed)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
            var sent = Sent(tenant.Id, 1, deposit: null);
            var draft = Quote.Start(tenant.Id, Folios.QuoteNumber(3), new QuoteCustomer(null, "Luis", "5522107766", null), null, true, 15, Now);
            draft.ReplaceLines([new QuoteLineInput(null, "Revisión", 1m, 500m, VatRate.General)], Now);
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments), sent, draft,
                OnlineCheckout.Start(tenant.Id, ProviderUserId, 63.80m, "{\"items\":[]}", null, "+525522107765", Now));
            await seed.SaveChangesAsync();
            return new Seed(tenant.Id, sent.Id, draft.Id);
        }
    }

    private static Quote Sent(Guid tenantId, int number, decimal? deposit)
    {
        var quote = Quote.Start(tenantId, Folios.QuoteNumber(number), new QuoteCustomer(null, "Valeria", "5522107765", null), null, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        quote.Send(Today, new string((char)('a' + number), 43), null, Now, deposit);
        return quote;
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
