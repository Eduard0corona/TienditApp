using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
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
/// AddOrderCancellationEmailsAndAnswerReminder (Fase 30B, ADR 0082): whether a member gets an email with each online order its
/// customer cancels, how long before its limit a business is reminded of a request, and the filtered index of the online
/// orders. The members that exist have not chosen, the businesses that exist keep the hour of the Fase 29U, the version before
/// it keeps saving both, and rolling back drops only the columns, their check and the index, never a member or a business.
/// </summary>
public class OrderCancellationEmailsMigrationSqlServerTests
{
    // The migration right before AddOrderCancellationEmailsAndAnswerReminder, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261107190735_AddAppointmentMarkedFakeAt";
    private const string Migration = "20261108150147_AddOrderCancellationEmailsAndAnswerReminder";
    private const string Index = "IX_Orders_OnlineMenu_TenantId_CreatedAt";

    [SqlServerFact]
    public async Task TheMembersAndBusinessesThatExist_ShouldNotHaveChosen_AndKeepTheHour_AndOnlyAChoiceIsKept()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var (tenant, member) = await SeedAsync(database, BeforeOrderCancellationEmails.CreateContext(database));

        await MigrateAsync(database, Migration);

        await using var context = database.CreateContext(tenant.Id);
        var stored = await context.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == member.Id);
        Assert.Equal(((bool?)null, false), (stored.OrderCancellationEmails, stored.GetsOrderCancellationEmails));
        Assert.Equal(60, (await context.TenantSettings.AsNoTracking().SingleAsync()).AnswerReminderMinutes);
        Assert.Equal("bit|1|", await ColumnAsync(context, "TenantUsers", "OrderCancellationEmails"));
        Assert.Equal("int|0|((60))", await ColumnAsync(context, "TenantSettings", "AnswerReminderMinutes"));
        Assert.Equal(
            "([Channel]=(2) AND [CreatedByBusiness]=(0) AND [TableNumber] IS NULL AND [OpenTableId] IS NULL AND [CustomerPhone] IS NOT NULL)|TenantId, CreatedAt|CustomerPhone, Status",
            await IndexAsync(context));

        // Chosen and picked from now on: kept.
        (await context.TenantUsers.SingleAsync(m => m.Id == member.Id)).ChooseOrderCancellationEmails(true);
        (await context.TenantSettings.SingleAsync()).SetAnswerReminder(120);
        await context.SaveChangesAsync();
        await using var check = database.CreateContext(tenant.Id);
        Assert.True((await check.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == member.Id)).OrderCancellationEmails);
        Assert.Equal(120, (await check.TenantSettings.AsNoTracking().SingleAsync()).AnswerReminderMinutes);

        // Only one of the choices: the database refuses another, as the domain does.
        var refused = await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [TenantSettings] SET [AnswerReminderMinutes] = 45 WHERE [TenantId] = {tenant.Id}"));
        Assert.Contains("CK_TenantSettings_AnswerReminderMinutes", refused.Message);
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingMembersAndBusinesses_WithTheHourAndNoChoice()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version saves a business and a member, unaware of the columns.
        var (tenant, member) = await SeedAsync(database, BeforeOrderCancellationEmails.CreateContext(database));

        await using var context = database.CreateContext(tenant.Id);
        Assert.Null((await context.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == member.Id)).OrderCancellationEmails);
        Assert.Equal(60, (await context.TenantSettings.AsNoTracking().SingleAsync()).AnswerReminderMinutes);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheColumnsAndTheIndex_AndForwardAgainNobodyHasChosen()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenant, member) = await SeedAsync(database, database.CreateContext());
        await using (var choose = database.CreateContext(tenant.Id))
        {
            (await choose.TenantUsers.SingleAsync(m => m.Id == member.Id)).ChooseOrderCancellationEmails(true);
            (await choose.TenantSettings.SingleAsync()).SetAnswerReminder(0);
            await choose.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeOrderCancellationEmails.CreateContext(database, tenant.Id))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.TenantUsers', N'OrderCancellationEmails') AS [Value]").SingleAsync());
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.TenantSettings', N'AnswerReminderMinutes') AS [Value]").SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = {Index}").SingleAsync());
            // The member and the business stay as they were.
            var stored = await context.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == member.Id);
            Assert.Equal((TenantUserRole.Staff, TenantUserStatus.Active, (bool?)true), (stored.Role, stored.Status, stored.RequestEmails));
            Assert.Single(await context.TenantSettings.AsNoTracking().ToListAsync());
        }

        // Rolling back again does nothing; forward again, nobody has chosen and the business has the hour.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Null((await forward.TenantUsers.AsNoTracking().SingleAsync(m => m.Id == member.Id)).OrderCancellationEmails);
        Assert.Equal(60, (await forward.TenantSettings.AsNoTracking().SingleAsync()).AnswerReminderMinutes);
    }

    /// <summary>
    /// A business with its settings and a member of its staff, with an account, who chose the agenda's notices, saved by
    /// <paramref name="seed"/>.
    /// </summary>
    private static async Task<(Tenant Tenant, TenantUser Member)> SeedAsync(SqlServerTestDatabase database, ApplicationDbContext seed)
    {
        await using (seed)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Cocina {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
            var email = $"cajero.{slug}@demo.com";
            var account = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                FirstName = "Cajero",
                LastName = "Equipo",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Role = UserRole.Staff,
                IsActive = true
            };
            var member = new TenantUser(tenant.Id, account.Id, TenantUserRole.Staff, TenantUserStatus.Active);
            member.ChooseRequestEmails(true);
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food | TenantCapability.Appointments), account, member);
            await seed.SaveChangesAsync();
            return (tenant, member);
        }
    }

    /// <summary>The column's type, whether it takes NULL (1) and its default ('' for none).</summary>
    private static Task<string> ColumnAsync(ApplicationDbContext context, string table, string column)
        => context.Database.SqlQuery<string>($"""
            SELECT CONCAT(TYPE_NAME(c.[user_type_id]), N'|', c.[is_nullable], N'|', OBJECT_DEFINITION(c.[default_object_id])) AS [Value]
            FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'dbo.' + {table}) AND c.[name] = {column}
            """).SingleAsync();

    /// <summary>The index's filter, its key and its included columns.</summary>
    private static Task<string> IndexAsync(ApplicationDbContext context)
        => context.Database.SqlQuery<string>($"""
            SELECT CONCAT(i.[filter_definition], N'|',
                STRING_AGG(CASE WHEN ic.[is_included_column] = 0 THEN COL_NAME(ic.[object_id], ic.[column_id]) END, N', ')
                    WITHIN GROUP (ORDER BY ic.[index_column_id]), N'|',
                STRING_AGG(CASE WHEN ic.[is_included_column] = 1 THEN COL_NAME(ic.[object_id], ic.[column_id]) END, N', ')
                    WITHIN GROUP (ORDER BY ic.[index_column_id])) AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
            WHERE i.[object_id] = OBJECT_ID(N'dbo.Orders') AND i.[name] = {Index}
            GROUP BY i.[filter_definition]
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
