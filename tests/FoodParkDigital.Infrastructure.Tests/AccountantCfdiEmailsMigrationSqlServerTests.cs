using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
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
/// AddAccountantCfdiEmails (Fase 28O, ADR 0060): the invoicing profile keeps the accountant's email and whether the month's
/// CFDI go on their own, and each email of a month to the accountant is kept with how it ended. The profiles that were there,
/// and those the previous version keeps saving, have no accountant; the database refuses what the domain never writes, one
/// month goes on its own once, and rolling back stops with error 50048 while an email exists.
/// </summary>
public class AccountantCfdiEmailsMigrationSqlServerTests
{
    // The migration right before AddAccountantCfdiEmails, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261030215308_AddCfdiCancellationNotices";
    private const string Migration = "20261031053720_AddAccountantCfdiEmails";
    private const string Accountant = "contador@despacho.mx";

    [SqlServerFact]
    public async Task Emails_ShouldBeSavedAfter_TheProfilesHaveNoAccountant_AndTheDatabaseRefuseWhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var seed = await SeedAsync(database);

        await MigrateAsync(database, Migration);

        // The deployed version keeps changing the profiles while this one is published.
        await using (var previous = BeforeAccountantCfdiEmails.CreateContext(database, seed.TenantId))
        {
            (await previous.InvoicingProfiles.SingleAsync()).ChangeSerie("A");
            await previous.SaveChangesAsync();
        }
        var byHand = CfdiMonthEmail.Request(seed.TenantId, 2026, 10, Accountant, seed.UserId, DateTimeOffset.UtcNow);
        var monthly = CfdiMonthEmail.Request(seed.TenantId, 2026, 10, Accountant, null, DateTimeOffset.UtcNow);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            var profile = await context.InvoicingProfiles.SingleAsync();
            Assert.Equal(("A", (string?)null, false), (profile.Serie, profile.AccountantEmail, profile.SendsMonthToAccountant));

            profile.UseAccountant(Accountant, monthly: true);
            context.CfdiMonthEmails.AddRange(byHand, monthly);
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(seed.TenantId))
        {
            var profile = await context.InvoicingProfiles.SingleAsync();
            Assert.Equal((Accountant, true), (profile.AccountantEmail, profile.SendsMonthToAccountant));
            var saved = await context.CfdiMonthEmails.SingleAsync(e => e.Id == monthly.Id);
            Assert.Equal((2026, 10, Accountant, (Guid?)null, true), (saved.Year, saved.Month, saved.To, saved.RequestedByUserId, saved.IsMonthly));
            saved.Sent(128, DateTimeOffset.UtcNow);
            (await context.CfdiMonthEmails.SingleAsync(e => e.Id == byHand.Id)).Fail(CfdiMonthEmailFailure.TooLarge, DateTimeOffset.UtcNow);
            // A person sends the month again as often as needed.
            context.CfdiMonthEmails.Add(CfdiMonthEmail.Request(seed.TenantId, 2026, 10, Accountant, seed.UserId, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            Assert.Equal(3, await context.CfdiMonthEmails.CountAsync());
        }
        // Another business does not see them.
        await using (var foreign = database.CreateContext(seed.OtherTenantId))
            Assert.Empty(await foreign.CfdiMonthEmails.ToListAsync());

        // TienditApp sends each month once on its own, even with two instances of the worker; another month goes.
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.CfdiMonthEmails.Add(CfdiMonthEmail.Request(seed.TenantId, 2026, 10, Accountant, null, DateTimeOffset.UtcNow));
            var twice = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(twice.InnerException).Number);
        }
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.CfdiMonthEmails.Add(CfdiMonthEmail.Request(seed.TenantId, 2026, 11, Accountant, null, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        (FormattableString Sql, int Number)[] never =
        [
            ($"UPDATE [CfdiMonthEmails] SET [Month] = 13 WHERE [Id] = {byHand.Id}", 547),
            ($"UPDATE [CfdiMonthEmails] SET [Year] = 2021 WHERE [Id] = {byHand.Id}", 547),
            // Sent with how many CFDI it carried, refused with why, never both.
            ($"UPDATE [CfdiMonthEmails] SET [SentAt] = SYSDATETIMEOFFSET(), [CfdiCount] = 1 WHERE [Id] = {byHand.Id}", 547),
            ($"UPDATE [CfdiMonthEmails] SET [CfdiCount] = NULL WHERE [Id] = {monthly.Id}", 547),
            ($"UPDATE [CfdiMonthEmails] SET [CfdiCount] = -1 WHERE [Id] = {monthly.Id}", 547),
            ($"UPDATE [CfdiMonthEmails] SET [Failure] = 5 WHERE [Id] = {byHand.Id}", 547),
            ($"UPDATE [CfdiMonthEmails] SET [Failure] = NULL WHERE [Id] = {byHand.Id}", 547),
            ($"UPDATE [CfdiMonthEmails] SET [FailedAttempts] = 1 WHERE [Id] = {byHand.Id}", 547),
            ($"UPDATE [CfdiMonthEmails] SET [To] = NULL WHERE [Id] = {byHand.Id}", 515),
            // Nothing goes on its own without the accountant's email.
            ($"UPDATE [InvoicingProfiles] SET [AccountantEmail] = NULL WHERE [TenantId] = {seed.TenantId}", 547),
            ($"UPDATE [InvoicingProfiles] SET [SendsMonthToAccountant] = NULL WHERE [TenantId] = {seed.TenantId}", 515)
        ];
        foreach (var (sql, number) in never)
        {
            var refused = await Record.ExceptionAsync(() => ExecuteAsync(database, sql));
            Assert.True(refused is SqlException e && e.Number == number, $"{sql.Format}: {refused?.Message ?? "it was not refused"}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWhileAnEmailExists_AndThenTakeTheColumnsAndTheTableAway()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        var email = CfdiMonthEmail.Request(seed.TenantId, 2026, 10, Accountant, seed.UserId, DateTimeOffset.UtcNow);
        await using (var context = database.CreateContext(seed.TenantId))
        {
            (await context.InvoicingProfiles.SingleAsync()).UseAccountant(Accountant, monthly: true);
            context.CfdiMonthEmails.Add(email);
            await context.SaveChangesAsync();
        }

        // An email stops it, even one that failed.
        await ExecuteAsync(database, $"UPDATE [CfdiMonthEmails] SET [FailedAt] = SYSDATETIMEOFFSET(), [Failure] = 3 WHERE [Id] = {email.Id}");
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50048 }, stopped?.Message ?? "it was not stopped");
        await using (var context = database.CreateContext())
            Assert.Equal(Migration, await LatestMigrationAsync(context));

        await ExecuteAsync(database, $"DELETE FROM [CfdiMonthEmails] WHERE [Id] = {email.Id}");
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAccountantCfdiEmails.CreateContext(database, seed.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal((0, 0), (
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND [name] IN (N'AccountantEmail', N'SendsMonthToAccountant')
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'CfdiMonthEmails'")));
            // The profile stays, without the accountant.
            Assert.Equal(seed.TenantId, (await context.InvoicingProfiles.SingleAsync()).TenantId);
        }

        // Rolling back again does nothing, and forward again the profile has no accountant and emails are saved.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        var profile = await forward.InvoicingProfiles.SingleAsync();
        Assert.Equal(((string?)null, false), (profile.AccountantEmail, profile.SendsMonthToAccountant));
        forward.CfdiMonthEmails.Add(CfdiMonthEmail.Request(seed.TenantId, 2026, 10, Accountant, null, DateTimeOffset.UtcNow));
        await forward.SaveChangesAsync();
        Assert.Equal(1, await forward.CfdiMonthEmails.CountAsync());
    }

    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid UserId);

    /// <summary>Two businesses, each with its invoicing profile, as the version before this one stores them.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeAccountantCfdiEmails.CreateContext(database);
        var tenants = Enumerable.Range(0, 2).Select(_ =>
        {
            var slug = $"tienda-{Guid.NewGuid():N}"[..20];
            return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
        }).ToList();
        foreach (var tenant in tenants)
            context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail), InvoicingProfile.Create(tenant.Id));
        await context.SaveChangesAsync();
        return new Seed(tenants[0].Id, tenants[1].Id, Guid.NewGuid());
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlAsync(sql);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql)
        => context.Database.SqlQuery<int>(sql).SingleAsync();

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
