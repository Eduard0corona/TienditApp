using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddOnlineOrderReminderAndReviewEmails (Fase 31K, ADR 0091): when the business was reminded of an online order still waiting
/// to be accepted, and whether a member gets an email with each new review. The orders and members that exist were not
/// reminded and have not chosen, the version before it keeps saving both, and rolling back drops only the two columns, never
/// an order or a member.
/// </summary>
public class OnlineOrderReminderMigrationSqlServerTests
{
    // The migration right before it.
    private const string BeforeMigration = "20261112154235_AddAccountCartsAndPostalCode";
    private const string Migration = "20261112161350_AddOnlineOrderReminderAndReviewEmails";

    [SqlServerFact]
    public async Task TheOrdersAndMembersThatExist_ShouldNotBeRemindedNorHaveChosen_AndBothAreKept()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var shop = await SeedAsync(BeforeOnlineOrderReminder.CreateContext(database));

        await MigrateAsync(database, Migration);

        await using var context = database.CreateContext(shop.TenantId);
        Assert.Null((await context.Orders.AsNoTracking().SingleAsync(o => o.Id == shop.OrderId)).ConfirmReminderSentAt);
        var member = await context.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == shop.MemberId);
        Assert.Equal(((bool?)null, true), (member.ReviewEmails, member.GetsReviewEmails));
        Assert.Equal("datetimeoffset|1|", await ColumnAsync(context, "Orders", "ConfirmReminderSentAt"));
        Assert.Equal("bit|1|", await ColumnAsync(context, "TenantUsers", "ReviewEmails"));

        // Reminded 16 minutes after it arrived, and chosen from now on: kept.
        var remindedAt = DateTimeOffset.UtcNow;
        await context.Database.ExecuteSqlAsync(
            $"UPDATE [Orders] SET [CreatedAt] = {remindedAt.AddMinutes(-16)} WHERE [Id] = {shop.OrderId}");
        await using (var change = database.CreateContext(shop.TenantId))
        {
            Assert.True((await change.Orders.SingleAsync(o => o.Id == shop.OrderId)).RemindToConfirm(remindedAt));
            (await change.TenantUsers.SingleAsync(m => m.Id == shop.MemberId)).ChooseReviewEmails(false);
            await change.SaveChangesAsync();
        }

        await using var check = database.CreateContext(shop.TenantId);
        Assert.Equal(remindedAt, (await check.Orders.AsNoTracking().SingleAsync(o => o.Id == shop.OrderId)).ConfirmReminderSentAt);
        Assert.False((await check.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == shop.MemberId)).ReviewEmails);
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingOrdersAndMembers_NotRemindedNorChosen()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version saves a business, a member and an order, unaware of the columns.
        var shop = await SeedAsync(BeforeOnlineOrderReminder.CreateContext(database));

        await using var context = database.CreateContext(shop.TenantId);
        Assert.Null((await context.Orders.AsNoTracking().SingleAsync(o => o.Id == shop.OrderId)).ConfirmReminderSentAt);
        Assert.Null((await context.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == shop.MemberId)).ReviewEmails);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheColumns_AndForwardAgainNothingIsRemindedNorChosen()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        // On a database at this migration: the model before the Fase 31L, and the ones after it, without their columns.
        var shop = await SeedAsync(BeforeWhatsAppMessageMedia.CreateContext(database));
        await using (var change = database.CreateContext(shop.TenantId))
        {
            await change.Database.ExecuteSqlAsync(
                $"UPDATE [Orders] SET [CreatedAt] = {DateTimeOffset.UtcNow.AddMinutes(-20)} WHERE [Id] = {shop.OrderId}");
            Assert.True((await change.Orders.SingleAsync(o => o.Id == shop.OrderId)).RemindToConfirm(DateTimeOffset.UtcNow));
            (await change.TenantUsers.SingleAsync(m => m.Id == shop.MemberId)).ChooseReviewEmails(true);
            await change.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeOnlineOrderReminder.CreateContext(database, shop.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.Orders', N'ConfirmReminderSentAt') AS [Value]").SingleAsync());
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.TenantUsers', N'ReviewEmails') AS [Value]").SingleAsync());
            // The order and the member stay as they were.
            var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == shop.OrderId);
            Assert.Equal((OrderChannel.Online, OrderStatus.Pending, 117.75m), (order.Channel, order.Status, order.Total));
            var member = await context.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == shop.MemberId);
            Assert.Equal((TenantUserRole.Owner, TenantUserStatus.Active), (member.Role, member.Status));
        }

        // Rolling back again does nothing; forward again, nothing is reminded nor chosen.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(shop.TenantId);
        Assert.Null((await forward.Orders.AsNoTracking().SingleAsync(o => o.Id == shop.OrderId)).ConfirmReminderSentAt);
        Assert.Null((await forward.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == shop.MemberId)).ReviewEmails);
    }

    private sealed record Shop(Guid TenantId, Guid MemberId, Guid OrderId);

    /// <summary>A business with its owner and an order of its online menu, still waiting, saved by <paramref name="seed"/>.</summary>
    private static async Task<Shop> SeedAsync(ApplicationDbContext seed)
    {
        await using (seed)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Cocina {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
            var email = $"duena.{slug}@demo.com";
            var account = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                FirstName = "Dueña",
                LastName = "Cocina",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Role = UserRole.TenantAdmin,
                IsActive = true
            };
            var member = new TenantUser(tenant.Id, account.Id, TenantUserRole.Owner, TenantUserStatus.Active);
            var customer = new TemporaryCustomer(tenant.Id, "5522107765", "Valeria Morales");
            var order = new Order(
                tenant.Id, null, customer.Id, false, "ORD-000731", BusinessType.Food, OrderType.Retail, "Valeria Morales",
                "+525522107765", null, null, pricesIncludeTax: true, channel: OrderChannel.Online);
            order.AddItem(Guid.NewGuid(), "Torta de jamón", 1, 117.75m);
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food), account, member, customer, order);
            await seed.SaveChangesAsync();
            return new Shop(tenant.Id, member.Id, order.Id);
        }
    }

    /// <summary>The column's type, whether it takes NULL (1) and its default ('' for none).</summary>
    private static Task<string> ColumnAsync(ApplicationDbContext context, string table, string column)
        => context.Database.SqlQuery<string>($"""
            SELECT CONCAT(TYPE_NAME(c.[user_type_id]), N'|', c.[is_nullable], N'|', OBJECT_DEFINITION(c.[default_object_id])) AS [Value]
            FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'dbo.' + {table}) AND c.[name] = {column}
            """).SingleAsync();

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
