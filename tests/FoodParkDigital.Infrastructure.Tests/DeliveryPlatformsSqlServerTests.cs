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
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The tables of the orders of delivery platforms (Fase 22F): the settings of each platform in a business and the
/// detail each order copies. Their checks keep every row one the domain could write, the unique index keeps one row
/// per platform and business, the revision refuses a stale edit, and rates and amounts keep their cents.
/// </summary>
public class DeliveryPlatformsSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 18, 30, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Tables_ShouldHaveTheirShape()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Equal(
            new[]
            {
                "DeliveryPlatformSettings.CommissionPercent decimal 5 2", "DeliveryPlatformSettings.CommissionVatPercent decimal 5 2",
                "DeliveryPlatformSettings.IsrWithholdingPercent decimal 5 2", "DeliveryPlatformSettings.VatWithholdingPercent decimal 5 2",
                "PlatformOrderDetails.Commission decimal 18 2", "PlatformOrderDetails.CommissionPercent decimal 5 2",
                "PlatformOrderDetails.CommissionTax decimal 18 2", "PlatformOrderDetails.CommissionVatPercent decimal 5 2",
                "PlatformOrderDetails.ExpectedSettlement decimal 18 2", "PlatformOrderDetails.IsrWithheld decimal 18 2",
                "PlatformOrderDetails.IsrWithholdingPercent decimal 5 2", "PlatformOrderDetails.OrderTotal decimal 18 2",
                "PlatformOrderDetails.VatWithheld decimal 18 2", "PlatformOrderDetails.VatWithholdingPercent decimal 5 2"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT OBJECT_NAME(c.[object_id]) + N'.' + c.[name] + N' ' + t.[name] + N' ' + CAST(c.[precision] AS nvarchar(3))
                    + N' ' + CAST(c.[scale] AS nvarchar(3)) AS [Value]
                FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
                WHERE c.[object_id] IN (OBJECT_ID(N'dbo.DeliveryPlatformSettings'), OBJECT_ID(N'dbo.PlatformOrderDetails'))
                    AND t.[name] = N'decimal' AND c.[is_nullable] = 0
                ORDER BY [Value]
                """).ToListAsync());
        Assert.Equal(1, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
            WHERE c.[object_id] = OBJECT_ID(N'dbo.PlatformOrderDetails') AND c.[name] = N'ExternalOrderNumber'
                AND t.[name] = N'nvarchar' AND c.[max_length] = 60 AND c.[is_nullable] = 0
            """));
        Assert.Equal(1, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns c
            INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
            INNER JOIN sys.default_constraints d ON d.[object_id] = c.[default_object_id]
            WHERE c.[object_id] = OBJECT_ID(N'dbo.DailySalesSnapshots') AND c.[name] = N'PlatformAmount'
                AND t.[name] = N'decimal' AND c.[precision] = 18 AND c.[scale] = 2 AND c.[is_nullable] = 0
                AND d.[definition] = N'((0.0))'
            """));
        Assert.Equal(
            new[]
            {
                "CK_DeliveryPlatformSettings_Channel", "CK_DeliveryPlatformSettings_CommissionBase",
                "CK_DeliveryPlatformSettings_Percents", "CK_DeliveryPlatformSettings_Revision",
                "CK_PlatformOrderDetails_Amounts", "CK_PlatformOrderDetails_BusinessCollected", "CK_PlatformOrderDetails_Channel",
                "CK_PlatformOrderDetails_CollectedBy", "CK_PlatformOrderDetails_CommissionBase", "CK_PlatformOrderDetails_Number",
                "CK_PlatformOrderDetails_Percents"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] AS [Value] FROM sys.check_constraints
                WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.DeliveryPlatformSettings'), OBJECT_ID(N'dbo.PlatformOrderDetails'))
                ORDER BY [name]
                """).ToListAsync());
        Assert.Equal(
            new[]
            {
                "IX_DeliveryPlatformSettings_TenantId_Channel 1", "IX_PlatformOrderDetails_OrderId 1",
                "IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderNumber_RegisteredAt 0",
                "IX_PlatformOrderDetails_TenantId_RegisteredAt 0"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] + N' ' + CAST([is_unique] AS nvarchar(1)) AS [Value] FROM sys.indexes
                WHERE [object_id] IN (OBJECT_ID(N'dbo.DeliveryPlatformSettings'), OBJECT_ID(N'dbo.PlatformOrderDetails'))
                    AND [name] LIKE N'IX[_]%'
                ORDER BY [name]
                """).ToListAsync());
        // The detail goes with its order, like the detail of a service order.
        Assert.Equal(1, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
            WHERE [name] = N'FK_PlatformOrderDetails_Orders_OrderId' AND [referenced_object_id] = OBJECT_ID(N'dbo.Orders')
                AND [delete_referential_action_desc] = N'CASCADE'
            """));
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuseRowsTheDomainCannotWrite()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        Guid orderId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = NewOrder(tenant.Id, "ORD-000001", OrderChannel.Counter);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            await RefusedAsync("CK_DeliveryPlatformSettings_Channel", () => InsertSettingAsync(context, tenant.Id, channel: 2));
            await RefusedAsync("CK_DeliveryPlatformSettings_Channel", () => InsertSettingAsync(context, tenant.Id, channel: 6));
            await RefusedAsync("CK_DeliveryPlatformSettings_CommissionBase", () => InsertSettingAsync(context, tenant.Id, commissionBase: 2));
            await RefusedAsync("CK_DeliveryPlatformSettings_Percents", () => InsertSettingAsync(context, tenant.Id, commission: 100.01m));
            await RefusedAsync("CK_DeliveryPlatformSettings_Percents", () => InsertSettingAsync(context, tenant.Id, commission: -0.01m));
            await RefusedAsync("CK_DeliveryPlatformSettings_Percents", () => InsertSettingAsync(context, tenant.Id, isr: 101m));
            await RefusedAsync("CK_DeliveryPlatformSettings_Revision", () => InsertSettingAsync(context, tenant.Id, revision: 0));
            // The database allows up to 100: the limits of policy live in the domain.
            await InsertSettingAsync(context, tenant.Id, commission: 100m);

            await RefusedAsync("CK_PlatformOrderDetails_Channel", () => InsertDetailAsync(context, tenant.Id, orderId, channel: 0));
            await RefusedAsync("CK_PlatformOrderDetails_CollectedBy",
                () => InsertDetailAsync(context, tenant.Id, orderId, collectedBy: 2, isrWithheld: 0m, vatWithheld: 0m, settlement: -348m));
            await RefusedAsync("CK_PlatformOrderDetails_CommissionBase", () => InsertDetailAsync(context, tenant.Id, orderId, commissionBase: -1));
            await RefusedAsync("CK_PlatformOrderDetails_Number", () => InsertDetailAsync(context, tenant.Id, orderId, number: ""));
            await RefusedAsync("CK_PlatformOrderDetails_Number", () => InsertDetailAsync(context, tenant.Id, orderId, number: "   "));
            await RefusedAsync("CK_PlatformOrderDetails_Percents", () => InsertDetailAsync(context, tenant.Id, orderId, vatWithholding: 100.5m));
            await RefusedAsync("CK_PlatformOrderDetails_Amounts", () => InsertDetailAsync(context, tenant.Id, orderId, orderTotal: 0m));
            await RefusedAsync("CK_PlatformOrderDetails_Amounts", () => InsertDetailAsync(context, tenant.Id, orderId, commission: -1m));
            await RefusedAsync("CK_PlatformOrderDetails_Amounts", () => InsertDetailAsync(context, tenant.Id, orderId, vatWithheld: -0.01m));
            // When the business charged, the platform withheld nothing.
            await RefusedAsync("CK_PlatformOrderDetails_BusinessCollected",
                () => InsertDetailAsync(context, tenant.Id, orderId, collectedBy: 1, isrWithheld: 21.55m, vatWithheld: 0m, settlement: -348m));

            // What the domain writes goes in, a negative expected settlement included.
            await InsertDetailAsync(context, tenant.Id, orderId, collectedBy: 1, isrWithheld: 0m, vatWithheld: 0m, settlement: -348m);
            var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertDetailAsync(context, tenant.Id, orderId));
            Assert.Equal(2601, duplicate.Number);
            Assert.Contains("IX_PlatformOrderDetails_OrderId", duplicate.Message);
        }
    }

    [SqlServerFact]
    public async Task PlatformOrder_ShouldBeStoredWithItsDetailAndPayment_KeepingTheCents_InItsBusinessOnly()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        Guid orderId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var setting = new DeliveryPlatformSetting(
                tenant.Id, OrderChannel.Rappi, isActive: true, new PlatformTerms(27.35m, PlatformCommissionBase.TotalWithTax, 16m, 2.5m, 50m));
            context.DeliveryPlatformSettings.Add(setting);
            var order = NewOrder(tenant.Id, "ORD-000001", OrderChannel.Rappi);
            order.RegisterPlatformOrder(setting, PlatformOrderNumber.From("#bc 953"), PlatformCollectedBy.Platform, Now, null);
            order.MarkAsCreated();
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await context.Orders.AsNoTracking()
                .Include(o => o.PlatformDetail)
                .Include(o => o.Payments)
                .SingleAsync(o => o.Id == orderId);
            var detail = order.PlatformDetail!;
            Assert.Equal(OrderChannel.Rappi, order.Channel);
            Assert.Equal("BC953", detail.ExternalOrderNumber);
            Assert.Equal(PlatformCollectedBy.Platform, detail.CollectedBy);
            Assert.Equal(1, detail.SettingsRevision);
            Assert.Equal((27.35m, 16m, 2.5m, 50m), (detail.CommissionPercent, detail.CommissionVatPercent, detail.IsrWithholdingPercent, detail.VatWithholdingPercent));
            // $1,000 with IVA included: 27.35 % = 273.50, its IVA 43.76, ISR 21.55, IVA withheld 68.97.
            Assert.Equal(1000m, detail.OrderTotal);
            Assert.Equal((273.50m, 43.76m, 21.55m, 68.97m), (detail.Commission, detail.CommissionTax, detail.IsrWithheld, detail.VatWithheld));
            Assert.Equal(592.22m, detail.ExpectedSettlement);
            Assert.Equal(detail.OrderTotal - detail.Commission - detail.CommissionTax - detail.IsrWithheld - detail.VatWithheld, detail.ExpectedSettlement);
            Assert.Equal(Now, detail.RegisteredAt);
            var payment = Assert.Single(order.Payments);
            Assert.Equal((OrderPaymentMethod.Platform, 1000m, "BC953", Now), (payment.Method, payment.Amount, payment.Reference, payment.PaidAt));
            Assert.Equal(1000m, order.PaidAmount);
            Assert.Equal(27.35m, (await context.DeliveryPlatformSettings.AsNoTracking().SingleAsync()).CommissionPercent);
        }

        // Another business sees neither the settings nor the detail.
        await using (var context = database.CreateContext(other.Id))
        {
            Assert.Empty(await context.DeliveryPlatformSettings.ToListAsync());
            Assert.Empty(await context.PlatformOrderDetails.ToListAsync());
            Assert.Null(await context.Orders.Include(o => o.PlatformDetail).SingleOrDefaultAsync(o => o.Id == orderId));
        }

        // Deleting the order (as the cascade of a business would) takes its detail with it.
        await using (var context = database.CreateContext(tenant.Id))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OrderPayments WHERE OrderId = {orderId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Orders WHERE Id = {orderId}");
            Assert.Equal(0, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM PlatformOrderDetails"));
        }
    }

    [SqlServerFact]
    public async Task Settings_ShouldKeepOneRowPerPlatform_AndRefuseAStaleEdit()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);

        // Two people save the first settings of Rappi at the same time: the database keeps the first.
        await using (var first = database.CreateContext(tenant.Id))
        await using (var second = database.CreateContext(tenant.Id))
        {
            first.DeliveryPlatformSettings.Add(new DeliveryPlatformSetting(tenant.Id, OrderChannel.Rappi, true, PlatformTerms.WithFiscalDefaults(30m)));
            await first.SaveChangesAsync();

            second.DeliveryPlatformSettings.Add(new DeliveryPlatformSetting(tenant.Id, OrderChannel.Rappi, false, PlatformTerms.WithFiscalDefaults(25m)));
            var taken = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(taken.InnerException).Number);
        }

        // Another platform of the same business, and the same platform in another business, are other rows.
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryPlatformSettings.Add(new DeliveryPlatformSetting(tenant.Id, OrderChannel.UberEats, true, PlatformTerms.WithFiscalDefaults(29m)));
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(other.Id))
        {
            context.DeliveryPlatformSettings.Add(new DeliveryPlatformSetting(other.Id, OrderChannel.Rappi, true, PlatformTerms.WithFiscalDefaults(30m)));
            await context.SaveChangesAsync();
        }

        // Two people edit Rappi: the second one saves over a revision that already changed.
        await using (var first = database.CreateContext(tenant.Id))
        await using (var second = database.CreateContext(tenant.Id))
        {
            var firstRappi = await first.DeliveryPlatformSettings.SingleAsync(s => s.Channel == OrderChannel.Rappi);
            var secondRappi = await second.DeliveryPlatformSettings.SingleAsync(s => s.Channel == OrderChannel.Rappi);

            Assert.True(firstRappi.Update(isActive: true, PlatformTerms.WithFiscalDefaults(28m)));
            await first.SaveChangesAsync();

            Assert.True(secondRappi.Update(isActive: false, PlatformTerms.WithFiscalDefaults(30m)));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var settings = await context.DeliveryPlatformSettings.AsNoTracking().OrderBy(s => s.Channel).ToListAsync();
            Assert.Equal(new[] { OrderChannel.Rappi, OrderChannel.UberEats }, settings.Select(s => s.Channel));
            var rappi = settings[0];
            Assert.Equal((true, 28m, 2), (rappi.IsActive, rappi.CommissionPercent, rappi.Revision));
            Assert.Equal(PlatformTerms.WithFiscalDefaults(28m), rappi.Terms);
        }
        await using (var context = database.CreateContext(other.Id))
            Assert.Equal(30m, Assert.Single(await context.DeliveryPlatformSettings.ToListAsync()).CommissionPercent);
    }

    [SqlServerFact]
    public async Task SaveOfTheFirstTerms_AtTheSameTimeAsAnother_ShouldKeepOne_AndAnswerTheOtherStale()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        // Someone saves Rappi's first terms after this save read that there were none, and before it writes.
        await using var racing = database.CreateProductionLikeContext(Tenant(tenant.Id), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenant.Id);
                other.DeliveryPlatformSettings.Add(new DeliveryPlatformSetting(tenant.Id, OrderChannel.Rappi, true, PlatformTerms.WithFiscalDefaults(25m)));
                await other.SaveChangesAsync();
            }));

        var result = await new SaveDeliveryPlatformCommandHandler(racing, Tenant(tenant.Id), new Owner())
            .Handle(Rappi(revision: 0, commission: 30m), CancellationToken.None);

        // The unique index keeps the other one; this one is undone whole, its activity entry included.
        Assert.Equal(DeliveryPlatformErrors.Stale, result.Error);
        await using var context = database.CreateContext(tenant.Id);
        var stored = Assert.Single(await context.DeliveryPlatformSettings.ToListAsync());
        Assert.Equal((25m, 1), (stored.CommissionPercent, stored.Revision));
        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task EditOverARevisionThatChangedMeanwhile_ShouldBeStale_AndLeaveTheOtherEdit()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            Assert.True((await new SaveDeliveryPlatformCommandHandler(seed, Tenant(tenant.Id), new Owner())
                .Handle(Rappi(revision: 0, commission: 30m), CancellationToken.None)).IsSuccess);
        }

        // Someone saves Rappi after this edit read revision 1, and before it writes.
        await using var racing = database.CreateProductionLikeContext(Tenant(tenant.Id), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenant.Id);
                var rappi = await other.DeliveryPlatformSettings.SingleAsync();
                rappi.Update(isActive: true, PlatformTerms.WithFiscalDefaults(28m));
                await other.SaveChangesAsync();
            }));
        var result = await new SaveDeliveryPlatformCommandHandler(racing, Tenant(tenant.Id), new Owner())
            .Handle(Rappi(revision: 1, commission: 25m), CancellationToken.None);

        Assert.Equal(DeliveryPlatformErrors.Stale, result.Error);
        await using var context = database.CreateContext(tenant.Id);
        var stored = await context.DeliveryPlatformSettings.SingleAsync();
        Assert.Equal((28m, 2), (stored.CommissionPercent, stored.Revision));
        // Only the first save is recorded: the refused edit took its activity entry with it.
        Assert.Single(await context.AuditLogs.ToListAsync());
    }

    private static SaveDeliveryPlatformCommand Rappi(int revision, decimal commission)
        => new(OrderChannel.Rappi, revision, true, commission, PlatformCommissionBase.TotalWithTax, 16m, 2.5m, 50m);

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    /// <summary>Runs <paramref name="save"/> once, right before the context it is attached to writes.</summary>
    private sealed class SavedMeanwhile(Func<Task> save) : SaveChangesInterceptor
    {
        private bool _saved;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_saved)
            {
                _saved = true;
                await save();
            }

            return result;
        }
    }

    private sealed class Owner : ICurrentUserService
    {
        public string? UserId => "4f6c2a0e-8d8b-4a8f-9a55-0c4b1f3f8a10";
        public string? UserName => "duena@demo.com";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

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

    /// <summary>A paquete of $1,000 with IVA included.</summary>
    private static Order NewOrder(Guid tenantId, string number, OrderChannel channel)
    {
        var order = new Order(
            tenantId, null, null, true, number, BusinessType.Food, OrderType.Retail, "Juan", null, null, null,
            pricesIncludeTax: true, channel: channel);
        order.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 1000m);
        return order;
    }

    // Rows written straight to the tables, past the app's own checks.
    private static Task<int> InsertSettingAsync(
        ApplicationDbContext context, Guid tenantId, int channel = 3, int commissionBase = 0, decimal commission = 30m,
        decimal isr = 2.5m, int revision = 1)
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO DeliveryPlatformSettings (Id, TenantId, Channel, IsActive, CommissionPercent, CommissionBase, CommissionVatPercent,
                IsrWithholdingPercent, VatWithholdingPercent, Revision, CreatedAt)
            VALUES ({Guid.NewGuid()}, {tenantId}, {channel}, 1, {commission}, {commissionBase}, 16, {isr}, 50, {revision}, {DateTimeOffset.UtcNow})
            """);

    private static Task<int> InsertDetailAsync(
        ApplicationDbContext context, Guid tenantId, Guid orderId, int channel = 3, string number = "BC953", int collectedBy = 0,
        int commissionBase = 0, decimal vatWithholding = 50m, decimal orderTotal = 1000m, decimal commission = 300m,
        decimal isrWithheld = 21.55m, decimal vatWithheld = 68.97m, decimal settlement = 561.48m)
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PlatformOrderDetails (Id, TenantId, OrderId, Channel, ExternalOrderNumber, CollectedBy, SettingsRevision,
                CommissionPercent, CommissionBase, CommissionVatPercent, IsrWithholdingPercent, VatWithholdingPercent,
                OrderTotal, Commission, CommissionTax, IsrWithheld, VatWithheld, ExpectedSettlement, RegisteredAt)
            VALUES ({Guid.NewGuid()}, {tenantId}, {orderId}, {channel}, {number}, {collectedBy}, 1,
                30, {commissionBase}, 16, 2.5, {vatWithholding},
                {orderTotal}, {commission}, 48, {isrWithheld}, {vatWithheld}, {settlement}, {Now})
            """);

    private static async Task RefusedAsync(string constraint, Func<Task> insert)
    {
        var error = await Assert.ThrowsAsync<SqlException>(insert);
        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
