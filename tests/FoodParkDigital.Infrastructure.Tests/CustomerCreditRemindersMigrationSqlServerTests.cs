using FoodParkDigital.Domain.Customers.Entities;
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
/// AddCustomerCreditReminders (Fase 26C, ADR 0035): a customer's own term and the business's credit reminders; what the
/// deployed version stores meanwhile has no term of its own and keeps reminders off; the checks refuse what the domain never
/// writes; and rolling back stops with error 50028 once a customer has a term or a business used the reminders, and
/// otherwise drops only the columns and their checks. Its database stops before the notice of Fase 26E, whose columns the
/// settings of this version do not have.
/// </summary>
public class CustomerCreditRemindersMigrationSqlServerTests
{
    // The migration right before AddCustomerCreditReminders, where its rollback.sql takes the database.
    private const string BeforeReminders = "20261020003342_AddCustomerWriteOffs";
    private const string Migration = "20261020055548_AddCustomerCreditReminders";

    [SqlServerFact]
    public async Task Columns_ShouldKeepTheCustomersTerm_AndTheReminders_WhileThePreviousVersionStoresNeither()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeReminders);
        var tenant = await SeedTenantAsync(database);
        await using (var before = BeforeCustomerCreditReminders.CreateContext(database, tenant.Id))
        {
            before.Customers.Add(new Customer(tenant.Id, "Luis Pérez", "+525512345678", null));
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        // The version deployed before does not know them: what it stores has no term of its own, and reminders stay off.
        await using (var previous = BeforeCustomerCreditReminders.CreateContext(database, tenant.Id))
        {
            previous.Customers.Add(new Customer(tenant.Id, "Ana López", "+525587654321", null));
            (await previous.TenantSettings.SingleAsync()).SetCreditTermDays(15);
            await previous.SaveChangesAsync();
        }
        var template = new MessageTemplate(tenant.Id, "Saldo vencido", MessagePurpose.CreditReminder,
            "Hola {nombre}, debes {vencido} vencido.", "saldo_vencido");
        await using (var context = BeforeCreditDueSoon.CreateContext(database, tenant.Id))
        {
            var customers = await context.Customers.OrderBy(c => c.Name).ToListAsync();
            Assert.Equal(2, customers.Count);
            Assert.All(customers, customer => Assert.Null(customer.CreditTermDays));
            var settings = await context.TenantSettings.SingleAsync();
            Assert.Equal((false, 7, (Guid?)null, (int?)15),
                (settings.CreditRemindersEnabled, settings.CreditReminderEveryDays, settings.CreditReminderTemplateId, settings.CreditTermDays));

            customers.Single(c => c.Name == "Luis Pérez").SetCreditTermDays(7);
            context.MessageTemplates.Add(template);
            settings.UpdateCreditReminders(true, 3, template.Id);
            await context.SaveChangesAsync();
        }

        await using var check = BeforeCreditDueSoon.CreateContext(database, tenant.Id);
        Assert.Equal(7, (await check.Customers.SingleAsync(c => c.Name == "Luis Pérez")).CreditTermDays);
        Assert.Null((await check.Customers.SingleAsync(c => c.Name == "Ana López")).CreditTermDays);
        var saved = await check.TenantSettings.SingleAsync();
        Assert.Equal((true, 3, (Guid?)template.Id), (saved.CreditRemindersEnabled, saved.CreditReminderEveryDays, saved.CreditReminderTemplateId));
        Assert.Equal(MessagePurpose.CreditReminder, (await check.MessageTemplates.SingleAsync()).Purpose);
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_ACustomerTermOutOfRange_AndACadenceOutOfRange()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await using (var context = BeforeCreditDueSoon.CreateContext(database, tenant.Id))
        {
            context.Customers.Add(new Customer(tenant.Id, "Luis Pérez", "+525512345678", null));
            await context.SaveChangesAsync();
        }

        await using var check = BeforeCreditDueSoon.CreateContext(database);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [Customers] SET [CreditTermDays] = 121"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [Customers] SET [CreditTermDays] = 0"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [TenantSettings] SET [CreditReminderEveryDays] = 31"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [TenantSettings] SET [CreditReminderEveryDays] = 0"))).Number);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceACustomerHasATermOrRemindersWereUsed_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await using (var context = BeforeCreditDueSoon.CreateContext(database, tenant.Id))
        {
            var customer = new Customer(tenant.Id, "Luis Pérez", "+525512345678", null);
            customer.SetCreditTermDays(7);
            context.Customers.Add(customer);
            await context.SaveChangesAsync();
        }

        // A customer's term alone stops it.
        var term = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeReminders));
        Assert.Equal(50028, term.Number);
        Assert.Contains("their own credit term", term.Message);

        // And so does each trace of the reminders: turned on, a template of theirs, a reminder queued.
        Func<ApplicationDbContext, Task>[] traces =
        [
            async context => (await context.TenantSettings.SingleAsync()).UpdateCreditReminders(true, 7, null),
            context =>
            {
                context.MessageTemplates.Add(new MessageTemplate(tenant.Id, "Saldo", MessagePurpose.CreditReminder, "Hola {nombre}"));
                return Task.CompletedTask;
            },
            context =>
            {
                context.OutgoingMessages.Add(new OutgoingMessage(
                    tenant.Id, MessagePurpose.CreditReminder, "+525512345678", "Luis", "Hola", null, "es_MX", [],
                    DateTimeOffset.UtcNow, "credit:+525512345678:20261020"));
                return Task.CompletedTask;
            }
        ];
        foreach (var trace in traces)
        {
            await using (var context = BeforeCreditDueSoon.CreateContext(database))
            {
                Assert.Equal(Migration, await LatestMigrationAsync(context));
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE [Customers] SET [CreditTermDays] = NULL; UPDATE [TenantSettings] SET [CreditRemindersEnabled] = 0; " +
                    "DELETE FROM [MessageTemplates]; DELETE FROM [OutgoingMessages];");
            }
            await using (var context = BeforeCreditDueSoon.CreateContext(database, tenant.Id))
            {
                await trace(context);
                await context.SaveChangesAsync();
            }

            var used = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeReminders));
            Assert.Equal(50028, used.Number);
        }

        await using (var context = BeforeCreditDueSoon.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OutgoingMessages] WHERE [Purpose] = 3"));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [OutgoingMessages]");
        }

        await MigrateAsync(database, BeforeReminders);
        await using (var context = BeforeCreditDueSoon.CreateContext(database))
        {
            Assert.Equal(BeforeReminders, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Customers') AND [name] = N'CreditTermDays'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] IN (N'CreditRemindersEnabled', N'CreditReminderEveryDays', N'CreditReminderTemplateId')"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] IN (N'CK_Customers_CreditTermDays', N'CK_TenantSettings_CreditReminderEveryDays')"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Customers] WHERE [Name] = N'Luis Pérez'")));
        }

        // Rolling back again does nothing, and forward again the customer is there, with the business's term.
        await MigrateAsync(database, BeforeReminders);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCreditDueSoon.CreateContext(database, tenant.Id);
        Assert.Null((await forward.Customers.SingleAsync()).CreditTermDays);
        Assert.False((await forward.TenantSettings.SingleAsync()).CreditRemindersEnabled);
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Tienda {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
        await using var seed = BeforeCustomerCreditReminders.CreateContext(database);
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
