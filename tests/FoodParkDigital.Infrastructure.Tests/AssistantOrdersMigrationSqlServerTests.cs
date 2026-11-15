using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.AssistantOrders;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddAssistantOrders (Fase 32B, ADR 0096): whether a business takes orders from AI assistants and the orders they prepared.
/// Every business that exists takes them; the database refuses what the aggregate never writes (a link that is not one or
/// is another's, a negative total, a postal code that is not one, a sent order without its order or one waiting with it, two
/// prepared orders sent as the same order, or one of a business or an order that does not exist); the version before it
/// keeps saving businesses; rolling back stops with error 50064 while a business turned them off, one was sent or a link is
/// open, and otherwise drops only what it added. The cleanup deletes, of every business, the ones expired a week ago.
/// </summary>
public class AssistantOrdersMigrationSqlServerTests
{
    // The migration right before AddAssistantOrders, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261114111542_AddAppointmentDeposits";
    private const string Migration = "20261115151611_AddAssistantOrders";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly Guid Concha = Guid.NewGuid();

    [SqlServerFact]
    public async Task WhatExists_ShouldTakeOrdersFromAssistants_AndTheDatabaseShouldRefuseWhatTheAggregateNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var seed = await SeedAsync(BeforeAssistantOrders.CreateContext(database));

        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.True((await context.TenantSettings.AsNoTracking().SingleAsync()).AssistantOrders);
            Assert.Empty(await context.AssistantOrders.AsNoTracking().ToListAsync());
        }

        // What the domain writes is kept: one waiting with its cart and where to deliver it, and one sent as the order.
        Guid waiting;
        Guid sent;
        await using (var write = database.CreateContext(seed.TenantId))
        {
            (await write.TenantSettings.SingleAsync()).SetAssistantOrders(false);
            var open = Prepared(seed.TenantId, 'a', Now, postalCode: "80000");
            var confirmed = Prepared(seed.TenantId, 'b', Now);
            confirmed.Confirm(seed.OrderId, Now.AddMinutes(5));
            write.AssistantOrders.AddRange(open, confirmed);
            await write.SaveChangesAsync();
            (waiting, sent) = (open.Id, confirmed.Id);
        }
        await using var check = database.CreateContext(seed.TenantId);
        Assert.False((await check.TenantSettings.AsNoTracking().SingleAsync()).AssistantOrders);
        var stored = await check.AssistantOrders.AsNoTracking().SingleAsync(o => o.Id == waiting);
        Assert.Equal((AssistantOrderStatus.Pending, "80000", 74.50m, "Claude"), (stored.Status, stored.PostalCode, stored.ItemsTotal, stored.Source));
        Assert.Equal(new AccountCartLine(Concha, [], 2, "sin azúcar"), Assert.Single(stored.Lines));
        var storedSent = await check.AssistantOrders.AsNoTracking().SingleAsync(o => o.Id == sent);
        Assert.Equal((AssistantOrderStatus.Confirmed, (Guid?)seed.OrderId), (storedSent.Status, storedSent.OrderId));
        Assert.Empty(storedSent.Lines);

        foreach (var (sql, constraint) in new (string, string)[]
        {
            ($"UPDATE [AssistantOrders] SET [Token] = REPLICATE('c', 42) WHERE [Id] = '{waiting}'", "CK_AssistantOrders_Token"),
            ($"UPDATE [AssistantOrders] SET [Token] = REPLICATE('b', 43) WHERE [Id] = '{waiting}'", "IX_AssistantOrders_Token"),
            ($"UPDATE [AssistantOrders] SET [ItemsTotal] = -1 WHERE [Id] = '{waiting}'", "CK_AssistantOrders_ItemsTotal"),
            ($"UPDATE [AssistantOrders] SET [PostalCode] = '800' WHERE [Id] = '{waiting}'", "CK_AssistantOrders_PostalCode"),
            // Waiting, it was never sent; sent, it is an order.
            ($"UPDATE [AssistantOrders] SET [ConfirmedAt] = SYSDATETIMEOFFSET() WHERE [Id] = '{waiting}'", "CK_AssistantOrders_Outcome"),
            ($"UPDATE [AssistantOrders] SET [Status] = 0 WHERE [Id] = '{sent}'", "CK_AssistantOrders_Outcome"),
            ($"UPDATE [AssistantOrders] SET [OrderId] = NULL WHERE [Id] = '{sent}'", "CK_AssistantOrders_Outcome"),
            // An order that does not exist, a business that does not exist, and a second one sent as the same order.
            ($"UPDATE [AssistantOrders] SET [OrderId] = '{Guid.NewGuid()}' WHERE [Id] = '{sent}'", "FK_AssistantOrders_Orders_OrderId"),
            ($"UPDATE [AssistantOrders] SET [TenantId] = '{Guid.NewGuid()}' WHERE [Id] = '{waiting}'", "FK_AssistantOrders_Tenants_TenantId"),
            ($"""
              UPDATE [AssistantOrders] SET [Status] = 1, [OrderId] = '{seed.OrderId}', [ConfirmedAt] = SYSDATETIMEOFFSET(), [Lines] = N'[]'
              WHERE [Id] = '{waiting}'
              """, "IX_AssistantOrders_OrderId"),
        })
        {
            var error = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(sql));
            Assert.True(error is SqlException, $"Not refused ({error?.GetType().Name}): {sql}");
            Assert.Contains(constraint, error!.Message);
        }
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingBusinesses_ThatThenTakeOrdersFromAssistants()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version signs up a business, unaware of the column.
        var seed = await SeedAsync(BeforeAssistantOrders.CreateContext(database));

        await using var context = database.CreateContext(seed.TenantId);
        Assert.True((await context.TenantSettings.AsNoTracking().SingleAsync()).AssistantOrders);
        Assert.Equal(1, await context.Orders.CountAsync());
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileABusinessTurnedThemOff_OneWasSentOrALinkIsOpen_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database.CreateContext());

        // Each of the three alone stops it: a business that turned them off, a link still open, and one sent days ago.
        var steps = new Func<ApplicationDbContext, Task>[]
        {
            async context => (await context.TenantSettings.SingleAsync()).SetAssistantOrders(false),
            async context =>
            {
                (await context.TenantSettings.SingleAsync()).SetAssistantOrders(true);
                context.AssistantOrders.Add(Prepared(seed.TenantId, 'a', Now));
            },
            async context =>
            {
                context.AssistantOrders.Remove(await context.AssistantOrders.SingleAsync());
                var sent = Prepared(seed.TenantId, 'b', Now.AddDays(-3));
                sent.Confirm(seed.OrderId, Now.AddDays(-3).AddMinutes(5));
                context.AssistantOrders.Add(sent);
            }
        };
        foreach (var leaveOnlyOne in steps)
        {
            await using (var context = database.CreateContext(seed.TenantId))
            {
                await leaveOnlyOne(context);
                await context.SaveChangesAsync();
            }
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
            Assert.True(stopped is SqlException { Number: 50064 }, stopped?.Message ?? "it was not stopped");
            await using var after = database.CreateContext(seed.TenantId);
            Assert.Equal(Migration, await LatestMigrationAsync(after));
            Assert.Equal(1, await after.AssistantOrders.CountAsync() + (await after.TenantSettings.AnyAsync(s => !s.AssistantOrders) ? 1 : 0));
        }

        // One that expired unsent never reached the business: it goes with the table.
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.AssistantOrders.Remove(await context.AssistantOrders.SingleAsync());
            context.AssistantOrders.Add(Prepared(seed.TenantId, 'c', Now.AddDays(-2)));
            await context.SaveChangesAsync();
        }
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAssistantOrders.CreateContext(database, seed.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQueryRaw<int?>("SELECT COL_LENGTH(N'dbo.TenantSettings', N'AssistantOrders') AS [Value]").SingleAsync());
            Assert.Null(await context.Database.SqlQueryRaw<int?>("SELECT OBJECT_ID(N'dbo.AssistantOrders') AS [Value]").SingleAsync());
            Assert.Equal(1, await context.TenantSettings.AsNoTracking().CountAsync());
            Assert.Equal(1, await context.Orders.AsNoTracking().CountAsync());
        }

        // Rolling back again does nothing; forward again, the business takes them.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.True((await forward.TenantSettings.AsNoTracking().SingleAsync()).AssistantOrders);
    }

    [SqlServerFact]
    public async Task Cleanup_ShouldDeleteTheOnesExpiredAWeekAgo_OfEveryBusiness_SentOrNot()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var first = await SeedAsync(database.CreateContext());
        var second = await SeedAsync(database.CreateContext());

        Guid recent;
        Guid open;
        await using (var context = database.CreateContext())
        {
            // Expired eight days ago, sent and not, of each business; expired six days ago; and open.
            var sent = Prepared(first.TenantId, 'a', Now.AddDays(-9));
            sent.Confirm(first.OrderId, Now.AddDays(-9).AddMinutes(5));
            var lately = Prepared(first.TenantId, 'c', Now.AddDays(-7));
            var waiting = Prepared(second.TenantId, 'd', Now);
            context.AssistantOrders.AddRange(sent, Prepared(second.TenantId, 'b', Now.AddDays(-9)), lately, waiting);
            await context.SaveChangesAsync();
            (recent, open) = (lately.Id, waiting.Id);
        }

        await using var services = new ServiceCollection()
            .AddScoped<IApplicationDbContext>(_ => database.CreateContext())
            .BuildServiceProvider();
        var cleanup = new AssistantOrdersCleanupService(
            services.GetRequiredService<IServiceScopeFactory>(), new Clock(Now), NullLogger<AssistantOrdersCleanupService>.Instance);

        Assert.Equal(2, await cleanup.DeleteExpiredAsync(CancellationToken.None));
        await using var check = database.CreateContext();
        Assert.Equal(new[] { recent, open }.Order(), (await check.AssistantOrders.IgnoreQueryFilters().Select(o => o.Id).ToListAsync()).Order());
        // The order that was sent stays: it is the business's.
        Assert.Equal(2, await check.Orders.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await cleanup.DeleteExpiredAsync(CancellationToken.None));
    }

    private sealed record Seed(Guid TenantId, Guid OrderId);

    /// <summary>A bakery with its settings and an order, saved by <paramref name="seed"/>.</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext seed)
    {
        await using (seed)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Panadería {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
            var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail, "Mariana", "+525522223333", null, null);
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail), order);
            await seed.SaveChangesAsync();
            return new Seed(tenant.Id, order.Id);
        }
    }

    private static AssistantOrder Prepared(Guid tenantId, char token, DateTimeOffset at, string? postalCode = null)
        => AssistantOrder.Prepare(tenantId, new string(token, 43), [new AccountCartLine(Concha, [], 2, "sin azúcar")],
            postalCode, 74.50m, "Claude", at);

    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private sealed class Clock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
