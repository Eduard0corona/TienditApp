using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
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
/// AddQuoteConversion (Fase 29E, ADR 0053 point 17): the accepted quote that becomes a service order. The version before it
/// keeps saving its quotes over the new columns; the database refuses a conversion the aggregate never writes (of a version
/// not accepted, half recorded, two quotes of one order, or applying more of the visit than was fixed); rolling back stops
/// with error 50053 once a quote became an order, and otherwise drops only what it added, and a second run does nothing.
/// </summary>
public class QuoteConversionMigrationSqlServerTests
{
    // The database here is at most at this migration: every context is the model of the version after it, which does not know
    // what later migrations added either (BeforeAppointmentWork and the ones it chains).

    // The migration right before AddQuoteConversion, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261101181148_AddQuoteDeliveries";
    private const string Migration = "20261102014702_AddQuoteConversion";
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingItsQuotes_OverTheNewColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version records that the customer accepted, unaware of the new columns.
        Guid tenantId;
        Guid quoteId;
        await using (var before = BeforeQuoteConversion.CreateContext(database))
        {
            var (tenant, settings) = Business();
            var quote = Accepted(tenant.Id, 1);
            before.AddRange(tenant, settings, quote);
            await before.SaveChangesAsync();
            (tenantId, quoteId) = (tenant.Id, quote.Id);
        }

        await using var context = BeforeAppointmentWork.CreateContext(database, tenantId);
        var saved = await context.Quotes.SingleAsync(q => q.Id == quoteId);
        Assert.Equal((QuoteStatus.Accepted, (Guid?)null, (DateTimeOffset?)null, (decimal?)null),
            (saved.Status, saved.OrderId, saved.ConvertedAt, saved.AppliedVisitCredit));
    }

    [SqlServerFact]
    public async Task TheDatabase_ShouldRefuse_AConversionTheAggregateNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, accepted, order) = await SeedAsync(database);
        Guid sentId;
        Guid withoutVisitId;
        await using (var context = BeforeAppointmentWork.CreateContext(database, tenantId))
        {
            var sent = Quote.Start(tenantId, Folios.QuoteNumber(2), new QuoteCustomer(null, "Luis", "5522107766", null), null, true, 15, Now);
            sent.ReplaceLines([new QuoteLineInput(null, "Revisión", 1m, 500m, VatRate.General)], Now);
            sent.Send(Today, new string('s', 43), null, Now);
            // Accepted, and it takes nothing off for a visit.
            var withoutVisit = Accepted(tenantId, 3);
            context.Quotes.AddRange(sent, withoutVisit);
            await context.SaveChangesAsync();
            (sentId, withoutVisitId) = (sent.Id, withoutVisit.Id);
        }

        await using var check = BeforeAppointmentWork.CreateContext(database, tenantId);
        foreach (var sql in new[]
        {
            // A version not accepted.
            $"UPDATE [Quotes] SET [OrderId] = '{order}', [ConvertedAt] = SYSDATETIMEOFFSET() WHERE [Id] = '{sentId}'",
            // Half recorded: which order without when, when without which order, or what it applied without either.
            $"UPDATE [Quotes] SET [OrderId] = '{order}' WHERE [Id] = '{accepted}'",
            $"UPDATE [Quotes] SET [ConvertedAt] = SYSDATETIMEOFFSET() WHERE [Id] = '{accepted}'",
            $"UPDATE [Quotes] SET [AppliedVisitCredit] = 100 WHERE [Id] = '{accepted}'",
            // Applying nothing, or less than nothing, is stored as nothing applied.
            $"UPDATE [Quotes] SET [OrderId] = '{order}', [ConvertedAt] = SYSDATETIMEOFFSET(), [AppliedVisitCredit] = 0 WHERE [Id] = '{accepted}'",
            $"UPDATE [Quotes] SET [OrderId] = '{order}', [ConvertedAt] = SYSDATETIMEOFFSET(), [AppliedVisitCredit] = -1 WHERE [Id] = '{accepted}'",
            // Applying the visit of an accepted quote that takes nothing off for it.
            $"UPDATE [Quotes] SET [OrderId] = '{order}', [ConvertedAt] = SYSDATETIMEOFFSET(), [AppliedVisitCredit] = 100 WHERE [Id] = '{withoutVisitId}'",
            // Applying more of the visit than was fixed (406).
            $"UPDATE [Quotes] SET [OrderId] = '{order}', [ConvertedAt] = SYSDATETIMEOFFSET(), [AppliedVisitCredit] = 406.01 WHERE [Id] = '{accepted}'",
            // Applying more than its total (1,160), even within what was fixed for the visit.
            $"UPDATE [Quotes] SET [VisitCreditAmount] = 2000, [OrderId] = '{order}', [ConvertedAt] = SYSDATETIMEOFFSET(), [AppliedVisitCredit] = 1160.01 WHERE [Id] = '{accepted}'",
        })
        {
            var refused = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(sql));
            Assert.Contains("CK_Quotes_Conversion", refused?.Message);
        }

        // What the aggregate writes goes in: the accepted one with all it applied of the visit.
        await check.Database.ExecuteSqlRawAsync(
            $"UPDATE [Quotes] SET [OrderId] = '{order}', [ConvertedAt] = SYSDATETIMEOFFSET(), [AppliedVisitCredit] = 406 WHERE [Id] = '{accepted}'");

        // Two quotes of one order.
        await check.Database.ExecuteSqlRawAsync(
            $"UPDATE [Quotes] SET [Status] = 2, [ClosedAt] = SYSDATETIMEOFFSET(), [ReplyChannel] = 0 WHERE [Id] = '{sentId}'");
        var twice = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(
            $"UPDATE [Quotes] SET [OrderId] = '{order}', [ConvertedAt] = SYSDATETIMEOFFSET() WHERE [Id] = '{sentId}'"));
        Assert.Contains("IX_Quotes_OrderId", twice?.Message);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAQuoteBecameAnOrder_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, quoteId, orderId) = await SeedAsync(database);
        await using (var context = BeforeAppointmentWork.CreateContext(database, tenantId))
        {
            var quote = await context.Quotes.SingleAsync(q => q.Id == quoteId);
            quote.ConvertToOrder(orderId, 406m, Now);
            await context.SaveChangesAsync();
        }

        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50053 }, stopped?.Message ?? "it was not stopped");
        await using (var context = BeforeAppointmentWork.CreateContext(database, tenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            var converted = await context.Quotes.SingleAsync(q => q.Id == quoteId);
            Assert.Equal((orderId, (decimal?)406m), (converted.OrderId, converted.AppliedVisitCredit));
            // Removed on purpose, so it rolls back.
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE [Quotes] SET [OrderId] = NULL, [ConvertedAt] = NULL, [AppliedVisitCredit] = NULL");
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeQuoteConversion.CreateContext(database))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.Quotes') AND [name] IN (N'OrderId', N'ConvertedAt', N'AppliedVisitCredit')
                """).SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT (SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_Quotes_Conversion')
                    + (SELECT COUNT(*) FROM sys.indexes WHERE [name] = N'IX_Quotes_OrderId')
                    + (SELECT COUNT(*) FROM sys.foreign_keys WHERE [name] = N'FK_Quotes_Orders_OrderId') AS [Value]
                """).SingleAsync());
            // The quote and the orders stay.
            Assert.Equal((1, 2), (await context.Quotes.IgnoreQueryFilters().CountAsync(), await context.Orders.IgnoreQueryFilters().CountAsync()));
        }

        // Rolling back again does nothing, and forward again the quote has not become anything.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeAppointmentWork.CreateContext(database, tenantId);
        Assert.Null((await forward.Quotes.SingleAsync()).OrderId);
    }

    private static (Tenant Tenant, TenantSettings Settings) Business()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        return (tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments));
    }

    private static Quote Accepted(Guid tenantId, int number)
    {
        var quote = Quote.Start(tenantId, Folios.QuoteNumber(number), new QuoteCustomer(null, "Valeria", "5522107765", null), null, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        quote.Send(Today, new string((char)('a' + number), 43), null, Now);
        quote.Accept(Today, QuoteReplyChannel.WhatsApp, null, Now);
        return quote;
    }

    /// <summary>
    /// A trade with a quote of a visit the customer accepted, which takes off the 406 paid for the visit, and the service order
    /// it can become.
    /// </summary>
    private static async Task<(Guid TenantId, Guid QuoteId, Guid OrderId)> SeedAsync(SqlServerTestDatabase database)
    {
        var (tenant, settings) = Business();
        var visit = new Appointment(tenant.Id, "Valeria", "5522107765", "Visita y diagnóstico", Now, Now.AddHours(1));
        var visitSale = Order(tenant.Id, "ORD-000001", "Visita y diagnóstico", 406m);
        var quote = Quote.Start(tenant.Id, Folios.QuoteNumber(1), new QuoteCustomer(null, "Valeria", "5522107765", null), visit.Id, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        quote.SetVisitCredit(new QuoteVisitCredit(visitSale.Id, 406m), Now);
        quote.Send(Today, new string('q', 43), null, Now);
        quote.Accept(Today, QuoteReplyChannel.WhatsApp, null, Now);
        var order = Order(tenant.Id, "ORD-000002", "Cambio de calentador", 1160m);
        // Its appointments, as the version of this migration saves them (without what later versions added).
        await using var seed = BeforeAppointmentWork.CreateContext(database, tenant.Id);
        seed.AddRange(tenant, settings, visit, visitSale, quote, order);
        await seed.SaveChangesAsync();
        return (tenant.Id, quote.Id, order.Id);
    }

    private static Order Order(Guid tenantId, string number, string line, decimal price)
    {
        var order = new Order(tenantId, null, null, true, number, BusinessType.Service, OrderType.Service, "Valeria",
            "+525522107765", null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), line, 1, price);
        return order;
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
