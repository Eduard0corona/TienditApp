using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The tables of the settlements of delivery platforms (Fase 22G): a settlement keeps its orders and adjustments with
/// their cents, in its business and its adjustments in their order, the database holds one open settlement per platform
/// and one settlement in force per order (and the use cases answer a save that loses that race as someone else's), a
/// stale edit is refused, a settled order is never deleted from under its settlement, and the checks keep every row one
/// the domain could write.
/// </summary>
public class PlatformSettlementsSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 18, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Sunday = new(2026, 10, 11);

    [SqlServerFact]
    public async Task Tables_ShouldHaveTheirIndexesAndKeys()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var indexes = await context.Database.SqlQuery<IndexRow>($"""
            SELECT [name] AS [Name], CAST([is_unique] AS bit) AS [IsUnique], [filter_definition] AS [Filter]
            FROM sys.indexes
            WHERE [object_id] IN (OBJECT_ID(N'dbo.PlatformSettlements'), OBJECT_ID(N'dbo.PlatformSettlementOrders'),
                OBJECT_ID(N'dbo.PlatformSettlementAdjustments')) AND [type] > 0
            """).ToListAsync();
        var open = Assert.Single(indexes, index => index.Name == "IX_PlatformSettlements_TenantId_Channel_Open");
        Assert.True(open.IsUnique);
        Assert.Equal("([Status]=(0))", open.Filter);
        var active = Assert.Single(indexes, index => index.Name == "IX_PlatformSettlementOrders_OrderId_Active");
        Assert.True(active.IsUnique);
        Assert.Equal("([IsActive]=(1))", active.Filter);
        // The place of each adjustment has no index of its own: a unique one would make two saves of a settlement collide
        // on it before its revision says who saved first.
        Assert.False(Assert.Single(indexes, index => index.Name == "IX_PlatformSettlementAdjustments_SettlementId").IsUnique);
        Assert.Equal(9, indexes.Count);

        var keys = await context.Database.SqlQuery<KeyRow>($"""
            SELECT [name] AS [Name], [delete_referential_action_desc] AS [OnDelete]
            FROM sys.foreign_keys
            WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.PlatformSettlementOrders'), OBJECT_ID(N'dbo.PlatformSettlementAdjustments'))
            """).ToListAsync();
        Assert.Equal("NO_ACTION", Assert.Single(keys, key => key.Name == "FK_PlatformSettlementOrders_Orders_OrderId").OnDelete);
        Assert.Equal("CASCADE", Assert.Single(keys, key => key.Name == "FK_PlatformSettlementOrders_PlatformSettlements_SettlementId").OnDelete);
        Assert.Equal("CASCADE", Assert.Single(keys, key => key.Name == "FK_PlatformSettlementAdjustments_PlatformSettlements_SettlementId").OnDelete);
    }

    [SqlServerFact]
    public async Task Settlement_ShouldBeStoredWithItsOrdersAndAdjustments_KeepingTheCents_InItsBusinessOnly()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        var (first, second) = await SeedOrdersAsync(database, tenant.Id);
        Guid settlementId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var settlement = new PlatformSettlement(
                tenant.Id,
                OrderChannel.Rappi,
                new PlatformSettlementDeposit(Monday, Sunday, new DateOnly(2026, 10, 13), 1122.95m, "SPEI 4471", null),
                [Line(first, 561.48m), Line(second, 561.48m)],
                [new PlatformSettlementAdjustmentInput(PlatformSettlementAdjustmentKind.Claim, "Faltó un refresco", -0.01m)],
                Today);
            context.PlatformSettlements.Add(settlement);
            await context.SaveChangesAsync();
            settlementId = settlement.Id;
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var settlement = await context.PlatformSettlements.AsNoTracking()
                .Include(s => s.Orders)
                .Include(s => s.Adjustments)
                .SingleAsync(s => s.Id == settlementId);
            Assert.Equal((OrderChannel.Rappi, PlatformSettlementStatus.Open, 1), (settlement.Channel, settlement.Status, settlement.Revision));
            Assert.Equal((Monday, Sunday, new DateOnly(2026, 10, 13)), (settlement.PeriodFrom, settlement.PeriodTo, settlement.DepositedOn));
            Assert.Equal((1122.95m, 1122.96m, -0.01m, 0m), (settlement.DepositedAmount, settlement.OrdersTotal, settlement.AdjustmentsTotal, settlement.Difference));
            Assert.Equal("SPEI 4471", settlement.Reference);
            Assert.Equal(new[] { first, second }.Order(), settlement.Orders.Select(line => line.OrderId).Order());
            Assert.All(settlement.Orders, line => Assert.Equal((561.48m, true, tenant.Id), (line.ExpectedNet, line.IsActive, line.TenantId)));
            var adjustment = Assert.Single(settlement.Adjustments);
            Assert.Equal((1, PlatformSettlementAdjustmentKind.Claim, "Faltó un refresco", -0.01m),
                (adjustment.Position, adjustment.Kind, adjustment.Description, adjustment.Amount));
        }

        // Another business sees neither the settlement nor its rows.
        await using (var context = database.CreateContext(other.Id))
        {
            Assert.Empty(await context.PlatformSettlements.ToListAsync());
            Assert.Empty(await context.PlatformSettlementOrders.ToListAsync());
            Assert.Empty(await context.PlatformSettlementAdjustments.ToListAsync());
        }
    }

    [SqlServerFact]
    public async Task Update_ShouldDeleteTheOrdersThatLeave_AndReplaceTheAdjustments()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var (first, second) = await SeedOrdersAsync(database, tenant.Id);
        var settlementId = await OpenAsync(database, tenant.Id, [Line(first, 561.48m), Line(second, 561.48m)],
            [new(PlatformSettlementAdjustmentKind.Fee, null, -50m), new(PlatformSettlementAdjustmentKind.Promotion, null, -10m)]);

        await using (var context = database.CreateContext(tenant.Id))
        {
            var settlement = await LoadAsync(context, settlementId);
            settlement.Update(Deposit(561.48m), [Line(first, 561.48m)], [], Today);
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            Assert.Equal(first, Assert.Single(await check.PlatformSettlementOrders.ToListAsync()).OrderId);
            Assert.Empty(await check.PlatformSettlementAdjustments.ToListAsync());
            Assert.Equal(2, (await check.PlatformSettlements.SingleAsync()).Revision);
        }
    }

    [SqlServerFact]
    public async Task Adjustments_ShouldComeBackInTheOrderTheyWereWritten_AfterEachSave()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var written = Enumerable.Range(1, 12)
            .Select(number => new PlatformSettlementAdjustmentInput(PlatformSettlementAdjustmentKind.Other, $"Ajuste {number}", -number))
            .ToList();
        var settlementId = await OpenAsync(database, tenant.Id, [], written, depositedAmount: -78m);

        await using (var check = database.CreateContext(tenant.Id))
            Assert.Equal(written.Select(a => a.Description), (await LoadAsync(check, settlementId)).Adjustments.Select(a => a.Description));

        // Saved again in another order, the new rows take the places of the old ones.
        written.Reverse();
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await LoadAsync(context, settlementId)).Update(Deposit(-78m), [], written, Today);
            await context.SaveChangesAsync();
        }

        await using var reread = database.CreateContext(tenant.Id);
        var adjustments = (await LoadAsync(reread, settlementId)).Adjustments;
        Assert.Equal(written.Select(a => a.Description), adjustments.Select(a => a.Description));
        Assert.Equal(Enumerable.Range(1, 12), adjustments.Select(a => a.Position));
    }

    [SqlServerFact]
    public async Task OpenSettlements_ShouldBeOnePerPlatformAndBusiness()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        var firstId = await OpenAsync(database, tenant.Id, [], [new(PlatformSettlementAdjustmentKind.Fee, null, -20m)], depositedAmount: -20m);

        // Another open one of Rappi in the same business, also written by someone at the same time: refused.
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.PlatformSettlements.Add(new PlatformSettlement(tenant.Id, OrderChannel.Rappi, Deposit(0m), [], [], Today));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        // One of Uber Eats, and one of Rappi in another business, are other settlements.
        await OpenAsync(database, tenant.Id, [], [], channel: OrderChannel.UberEats);
        await OpenAsync(database, other.Id, [], []);

        // Once the first is closed, Rappi takes the next one.
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await LoadAsync(context, firstId)).Close(Now);
            await context.SaveChangesAsync();
        }
        await OpenAsync(database, tenant.Id, [], []);
        await using var count = database.CreateContext(tenant.Id);
        Assert.Equal(3, await count.PlatformSettlements.CountAsync());
    }

    [SqlServerFact]
    public async Task AnOrder_ShouldBeInOneSettlementInForce_UntilItsSettlementIsVoided()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var (first, _) = await SeedOrdersAsync(database, tenant.Id);
        var closedId = await OpenAsync(database, tenant.Id, [Line(first, 561.48m)], [], depositedAmount: 561.48m);
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await LoadAsync(context, closedId)).Close(Now);
            await context.SaveChangesAsync();
        }

        // A save that takes an order of a settlement in force (one that read it as pending a moment before): refused.
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.PlatformSettlements.Add(new PlatformSettlement(tenant.Id, OrderChannel.Rappi, Deposit(561.48m), [Line(first, 561.48m)], [], Today));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        // Voided, the closed one keeps its row as history, and the order enters the new settlement.
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await LoadAsync(context, closedId)).Void("Era de otra semana", Now);
            await context.SaveChangesAsync();
        }
        var newId = await OpenAsync(database, tenant.Id, [Line(first, 561.48m)], [], depositedAmount: 561.48m);
        await using var check = database.CreateContext(tenant.Id);
        var rows = await check.PlatformSettlementOrders.Where(line => line.OrderId == first).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.False(rows.Single(line => line.SettlementId == closedId).IsActive);
        Assert.True(rows.Single(line => line.SettlementId == newId).IsActive);
    }

    [SqlServerFact]
    public async Task EditOverARevisionThatChangedMeanwhile_ShouldBeRefused()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var settlementId = await OpenAsync(database, tenant.Id, [], []);

        await using var mine = database.CreateContext(tenant.Id);
        var mySettlement = await LoadAsync(mine, settlementId);
        await using (var theirs = database.CreateContext(tenant.Id))
        {
            (await LoadAsync(theirs, settlementId)).Update(Deposit(10m), [], [new(PlatformSettlementAdjustmentKind.Other, "Bono", 10m)], Today);
            await theirs.SaveChangesAsync();
        }

        mySettlement.Update(Deposit(20m), [], [new(PlatformSettlementAdjustmentKind.Other, "Otro bono", 20m)], Today);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => mine.SaveChangesAsync());

        await using var check = database.CreateContext(tenant.Id);
        var saved = await check.PlatformSettlements.Include(s => s.Adjustments).SingleAsync();
        Assert.Equal((10m, 2), (saved.DepositedAmount, saved.Revision));
        Assert.Equal("Bono", Assert.Single(saved.Adjustments).Description);
    }

    [SqlServerFact]
    public async Task SettledOrder_ShouldNotBeDeleted_AndASettlementTakesItsRowsWithIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var (first, _) = await SeedOrdersAsync(database, tenant.Id);
        var settlementId = await OpenAsync(database, tenant.Id, [Line(first, 561.48m)], [new(PlatformSettlementAdjustmentKind.Fee, null, -1m)]);

        await using var context = database.CreateContext(tenant.Id);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OrderPayments WHERE OrderId = {first}");
        var error = await Assert.ThrowsAsync<SqlException>(
            () => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Orders WHERE Id = {first}"));
        Assert.Equal(547, error.Number);
        Assert.Contains("FK_PlatformSettlementOrders_Orders_OrderId", error.Message);

        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PlatformSettlements WHERE Id = {settlementId}");
        Assert.Equal(0, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM PlatformSettlementOrders"));
        Assert.Equal(0, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM PlatformSettlementAdjustments"));
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuseRowsTheDomainCannotWrite()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        await using var context = database.CreateContext();

        await RefusedAsync("CK_PlatformSettlements_Channel", () => InsertSettlementAsync(context, tenant.Id, channel: 1));
        await RefusedAsync("CK_PlatformSettlements_Status", () => InsertSettlementAsync(context, tenant.Id, status: 2));
        await RefusedAsync("CK_PlatformSettlements_Dates", () => InsertSettlementAsync(context, tenant.Id, periodTo: Monday.AddDays(-1)));
        await RefusedAsync("CK_PlatformSettlements_Dates", () => InsertSettlementAsync(context, tenant.Id, depositedOn: Monday.AddDays(-1)));
        await RefusedAsync("CK_PlatformSettlements_Closed", () => InsertSettlementAsync(context, tenant.Id, status: 1));
        await RefusedAsync("CK_PlatformSettlements_Voided", () => InsertSettlementAsync(context, tenant.Id, status: 99, voidedAt: Now));
        await RefusedAsync("CK_PlatformSettlements_Revision", () => InsertSettlementAsync(context, tenant.Id, revision: 0));

        var settlementId = Guid.NewGuid();
        await InsertSettlementAsync(context, tenant.Id, id: settlementId);
        await RefusedAsync("CK_PlatformSettlementAdjustments_Kind", () => InsertAdjustmentAsync(context, tenant.Id, settlementId, kind: 7));
        await RefusedAsync("CK_PlatformSettlementAdjustments_Amount", () => InsertAdjustmentAsync(context, tenant.Id, settlementId, amount: 0m));
        await RefusedAsync("CK_PlatformSettlementAdjustments_Other", () => InsertAdjustmentAsync(context, tenant.Id, settlementId, kind: 99, description: null));

        // The rows the domain writes pass: a closed one with its time, a voided one with its time and reason, «Otro» with text.
        await InsertSettlementAsync(context, tenant.Id, channel: 4, status: 1, closedAt: Now);
        await InsertSettlementAsync(context, tenant.Id, channel: 4, status: 99, voidedAt: Now, voidReason: "Duplicada");
        await InsertAdjustmentAsync(context, tenant.Id, settlementId, kind: 99, description: "Bono");
    }

    [SqlServerFact]
    public async Task Create_WhenAnotherIsOpenedMeanwhile_ShouldBeStale_AndKeepTheOther()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var (first, second) = await SeedOrdersAsync(database, tenant.Id);
        // Someone opens a settlement of Rappi after this save read there was none, and before it writes.
        var theirs = Guid.Empty;
        await using var racing = database.CreateProductionLikeContext(Tenant(tenant.Id), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () => theirs = await OpenAsync(database, tenant.Id, [Line(second, 561.48m)], [])));

        var result = await new CreatePlatformSettlementCommandHandler(racing, Tenant(tenant.Id), new FixedClock())
            .Handle(Create([first], 561.48m), CancellationToken.None);

        // The unique index keeps theirs; this one is undone whole.
        Assert.Equal(PlatformSettlementErrors.Stale, result.Error);
        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(theirs, (await check.PlatformSettlements.SingleAsync()).Id);
        Assert.Equal(second, (await check.PlatformSettlementOrders.SingleAsync()).OrderId);
    }

    [SqlServerFact]
    public async Task Create_WhenAnOrderIsTakenMeanwhile_ShouldBeStale()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var (first, _) = await SeedOrdersAsync(database, tenant.Id);
        // Someone settles and closes the order after this save read it as pending, and before it writes.
        await using var racing = database.CreateProductionLikeContext(Tenant(tenant.Id), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                var closedId = await OpenAsync(database, tenant.Id, [Line(first, 561.48m)], [], depositedAmount: 561.48m);
                await using var other = database.CreateContext(tenant.Id);
                (await LoadAsync(other, closedId)).Close(Now);
                await other.SaveChangesAsync();
            }));

        var result = await new CreatePlatformSettlementCommandHandler(racing, Tenant(tenant.Id), new FixedClock())
            .Handle(Create([first], 561.48m), CancellationToken.None);

        Assert.Equal(PlatformSettlementErrors.Stale, result.Error);
        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(PlatformSettlementStatus.Closed, (await check.PlatformSettlements.SingleAsync()).Status);
    }

    [SqlServerFact]
    public async Task Update_SentTwice_ShouldAnswerTheSecondStale_NotAnError()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var (first, second) = await SeedOrdersAsync(database, tenant.Id);
        var settlementId = await OpenAsync(database, tenant.Id, [Line(first, 561.48m)], [], depositedAmount: 561.48m);
        var update = new UpdatePlatformSettlementCommand(
            settlementId, 1, Monday, Sunday, new DateOnly(2026, 10, 13), 1122.96m, null, null, [first, second], []);
        // The same save, sent twice (a double click, a retry): the first lands between what the second read and writes.
        await using var racing = database.CreateProductionLikeContext(Tenant(tenant.Id), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenant.Id);
                var saved = await new UpdatePlatformSettlementCommandHandler(other, Tenant(tenant.Id), new FixedClock())
                    .Handle(update, CancellationToken.None);
                Assert.True(saved.IsSuccess, saved.Error);
            }));

        var result = await new UpdatePlatformSettlementCommandHandler(racing, Tenant(tenant.Id), new FixedClock())
            .Handle(update, CancellationToken.None);

        Assert.Equal(PlatformSettlementErrors.Stale, result.Error);
        await using var check = database.CreateContext(tenant.Id);
        var stored = await LoadAsync(check, settlementId);
        Assert.Equal((2, 2, 1122.96m), (stored.Revision, stored.Orders.Count, stored.OrdersTotal));
    }

    private static PlatformSettlementOrderInput Line(Guid orderId, decimal expectedNet)
        => new(orderId, OrderChannel.Rappi, DateOnly.FromDateTime(Now.UtcDateTime), expectedNet);

    private static PlatformSettlementDeposit Deposit(decimal depositedAmount)
        => new(Monday, Sunday, new DateOnly(2026, 10, 13), depositedAmount, null, null);

    private static async Task<Guid> OpenAsync(
        SqlServerTestDatabase database,
        Guid tenantId,
        IReadOnlyList<PlatformSettlementOrderInput> orders,
        IReadOnlyList<PlatformSettlementAdjustmentInput> adjustments,
        decimal depositedAmount = 0m,
        OrderChannel channel = OrderChannel.Rappi)
    {
        await using var context = database.CreateContext(tenantId);
        var settlement = new PlatformSettlement(tenantId, channel, Deposit(depositedAmount), orders, adjustments, Today);
        context.PlatformSettlements.Add(settlement);
        await context.SaveChangesAsync();
        return settlement.Id;
    }

    private static Task<PlatformSettlement> LoadAsync(ApplicationDbContext context, Guid settlementId)
        => context.PlatformSettlements
            .Include(s => s.Orders)
            .Include(s => s.Adjustments)
            .SingleAsync(s => s.Id == settlementId);

    private static async Task<(Tenant Tenant, Tenant Other)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Cocina sin local", slug, BusinessType.Food, $"{slug}@demo.com");
        var other = new Tenant("Taquería", slug + "b", BusinessType.Food, $"{slug}b@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, TenantCapability.Food), new TenantSettings(other.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    /// <summary>Two orders of Rappi of $1,000 charged by the platform: 30 % of commission on the total, expected net 561.48.</summary>
    private static async Task<(Guid First, Guid Second)> SeedOrdersAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        await using var context = database.CreateContext(tenantId);
        var setting = new DeliveryPlatformSetting(
            tenantId, OrderChannel.Rappi, isActive: true, new PlatformTerms(30m, PlatformCommissionBase.TotalWithTax, 16m, 2.5m, 50m));
        context.DeliveryPlatformSettings.Add(setting);
        var orders = new[] { "ORD-000001", "ORD-000002" }.Select(number =>
        {
            var order = new Order(
                tenantId, null, null, true, number, BusinessType.Food, OrderType.Retail, "Juan", null, null, null,
                pricesIncludeTax: true, channel: OrderChannel.Rappi);
            order.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 1000m);
            order.RegisterPlatformOrder(setting, PlatformOrderNumber.From("BC" + number[^3..]), PlatformCollectedBy.Platform, Now, null);
            order.MarkAsCreated();
            return order;
        }).ToList();
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
        Assert.All(orders, order => Assert.Equal(561.48m, order.PlatformDetail!.ExpectedSettlement));
        return (orders[0].Id, orders[1].Id);
    }

    // Rows written straight to the tables, past the app's own checks.
    private static Task<int> InsertSettlementAsync(
        ApplicationDbContext context, Guid tenantId, Guid? id = null, int channel = 3, int status = 0, DateOnly? periodTo = null,
        DateOnly? depositedOn = null, DateTimeOffset? closedAt = null, DateTimeOffset? voidedAt = null, string? voidReason = null,
        int revision = 1)
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PlatformSettlements (Id, TenantId, Channel, PeriodFrom, PeriodTo, DepositedOn, DepositedAmount, Status,
                OrdersTotal, AdjustmentsTotal, ClosedAt, VoidedAt, VoidReason, Revision, CreatedAt)
            VALUES ({id ?? Guid.NewGuid()}, {tenantId}, {channel}, {Monday}, {periodTo ?? Sunday}, {depositedOn ?? Sunday}, 0, {status},
                0, 0, {closedAt}, {voidedAt}, {voidReason}, {revision}, {DateTimeOffset.UtcNow})
            """);

    private static Task<int> InsertAdjustmentAsync(
        ApplicationDbContext context, Guid tenantId, Guid settlementId, int kind = 4, decimal amount = -10m, string? description = "Tableta")
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PlatformSettlementAdjustments (Id, TenantId, SettlementId, Position, Kind, Description, Amount)
            VALUES ({Guid.NewGuid()}, {tenantId}, {settlementId}, 1, {kind}, {description}, {amount})
            """);

    /// <summary>A save of a settlement by its use case, as the API makes it.</summary>
    private static CreatePlatformSettlementCommand Create(IReadOnlyList<Guid> orderIds, decimal depositedAmount)
        => new(OrderChannel.Rappi, Monday, Sunday, new DateOnly(2026, 10, 13), depositedAmount, "SPEI 4471", null, orderIds, []);

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    /// <summary>The 14th of October in Mexico City: the period of the 5th to the 11th is in the past.</summary>
    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);
    }

    private static async Task RefusedAsync(string constraint, Func<Task> insert)
    {
        var error = await Assert.ThrowsAsync<SqlException>(insert);
        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();

    private sealed record IndexRow(string Name, bool IsUnique, string? Filter);

    private sealed record KeyRow(string Name, string OnDelete);
}
