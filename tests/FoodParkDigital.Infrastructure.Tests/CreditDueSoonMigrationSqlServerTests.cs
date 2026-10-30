using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
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
/// AddCreditDueSoon (Fase 26E, ADR 0043): the notice before the credit comes due; what the deployed version stores meanwhile
/// keeps it off, two days before; the check refuses what the domain never writes; and rolling back stops with error 50034
/// once a business used the notice, and otherwise drops only the columns and their check.
/// </summary>
public class CreditDueSoonMigrationSqlServerTests
{
    // The migration right before AddCreditDueSoon, where its rollback.sql takes the database.
    private const string BeforeDueSoon = "20261022033401_AddPlatformWithholdingCertificates";
    private const string Migration = "20261022153116_AddCreditDueSoon";

    [SqlServerFact]
    public async Task Columns_ShouldKeepTheNotice_WhileThePreviousVersionStoresItOff()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeDueSoon);
        var tenant = await SeedTenantAsync(database);

        await MigrateAsync(database, Migration);

        // The version deployed before does not know it: what it saves keeps the notice off, two days before.
        await using (var previous = BeforeCreditDueSoon.CreateContext(database, tenant.Id))
        {
            (await previous.TenantSettings.SingleAsync()).UpdateCreditReminders(true, 5, null);
            await previous.SaveChangesAsync();
        }
        var template = new MessageTemplate(tenant.Id, "Por vencer", MessagePurpose.CreditDueSoon,
            "Hola {nombre}, el {fecha} vence {porvencer}.", "por_vencer");
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database, tenant.Id))
        {
            var settings = await context.TenantSettings.SingleAsync();
            Assert.Equal((false, 2, (Guid?)null, true, 5),
                (settings.CreditDueSoonEnabled, settings.CreditDueSoonDaysBefore, settings.CreditDueSoonTemplateId,
                    settings.CreditRemindersEnabled, settings.CreditReminderEveryDays));

            context.MessageTemplates.Add(template);
            settings.UpdateCreditDueSoon(true, 3, template.Id);
            await context.SaveChangesAsync();
        }

        await using var check = BeforeCfdiWhatsApp.CreateContext(database, tenant.Id);
        var saved = await check.TenantSettings.SingleAsync();
        Assert.Equal((true, 3, (Guid?)template.Id), (saved.CreditDueSoonEnabled, saved.CreditDueSoonDaysBefore, saved.CreditDueSoonTemplateId));
        Assert.Equal(MessagePurpose.CreditDueSoon, (await check.MessageTemplates.SingleAsync()).Purpose);
    }

    [SqlServerFact]
    public async Task Check_ShouldRefuse_DaysBeforeOutOfRange()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        await SeedTenantAsync(database);

        await using var check = BeforeCfdiWhatsApp.CreateContext(database);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [TenantSettings] SET [CreditDueSoonDaysBefore] = 8"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [TenantSettings] SET [CreditDueSoonDaysBefore] = 0"))).Number);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceTheNoticeWasUsed_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);

        // Each trace of the notice stops it: turned on, a template chosen, a template of theirs, a notice queued.
        var template = new MessageTemplate(tenant.Id, "Por vencer", MessagePurpose.CreditDueSoon, "Hola {nombre}");
        Func<ApplicationDbContext, Task>[] traces =
        [
            async context => (await context.TenantSettings.SingleAsync()).UpdateCreditDueSoon(true, 2, null),
            async context => (await context.TenantSettings.SingleAsync()).UpdateCreditDueSoon(false, 2, Guid.NewGuid()),
            context =>
            {
                context.MessageTemplates.Add(template);
                return Task.CompletedTask;
            },
            context =>
            {
                context.OutgoingMessages.Add(new OutgoingMessage(
                    tenant.Id, MessagePurpose.CreditDueSoon, "+525512345678", "Luis", "Hola", null, "es_MX", [],
                    DateTimeOffset.UtcNow, "creditsoon:+525512345678:20261024:20261022"));
                return Task.CompletedTask;
            }
        ];
        foreach (var trace in traces)
        {
            await using (var context = BeforeCfdiWhatsApp.CreateContext(database))
            {
                Assert.Equal(Migration, await LatestMigrationAsync(context));
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE [TenantSettings] SET [CreditDueSoonEnabled] = 0, [CreditDueSoonTemplateId] = NULL; " +
                    "DELETE FROM [MessageTemplates]; DELETE FROM [OutgoingMessages];");
            }
            await using (var context = BeforeCfdiWhatsApp.CreateContext(database, tenant.Id))
            {
                await trace(context);
                await context.SaveChangesAsync();
            }

            var used = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeDueSoon));
            Assert.Equal(50034, used.Number);
            Assert.Contains("notice before the credit comes due", used.Message);
        }

        // The overdue reminders of the Fase 26C are not this migration's: they do not stop it.
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OutgoingMessages] WHERE [Purpose] = 4"));
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM [OutgoingMessages]; UPDATE [TenantSettings] SET [CreditRemindersEnabled] = 1, [CreditReminderEveryDays] = 5;");
        }

        await MigrateAsync(database, BeforeDueSoon);
        await using (var context = BeforeCfdiWhatsApp.CreateContext(database))
        {
            Assert.Equal(BeforeDueSoon, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] IN (N'CreditDueSoonEnabled', N'CreditDueSoonDaysBefore', N'CreditDueSoonTemplateId')"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_TenantSettings_CreditDueSoonDaysBefore'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [TenantSettings] WHERE [CreditRemindersEnabled] = 1 AND [CreditReminderEveryDays] = 5")));
        }

        // Rolling back again does nothing, and forward again the notice is off.
        await MigrateAsync(database, BeforeDueSoon);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCfdiWhatsApp.CreateContext(database, tenant.Id);
        var settings = await forward.TenantSettings.SingleAsync();
        Assert.Equal((false, 2), (settings.CreditDueSoonEnabled, settings.CreditDueSoonDaysBefore));
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Tienda {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
        await using var seed = BeforeCreditDueSoon.CreateContext(database);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
