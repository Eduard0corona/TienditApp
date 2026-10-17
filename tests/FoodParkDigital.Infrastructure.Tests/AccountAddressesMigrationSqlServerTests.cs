using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddAccountAddresses (Fase 24B, ADR 0022): the addresses of a customer account, with the postal code of the zones also
/// for a row written by hand, go with the account; and rolling back stops with error 50017 once an account saved one,
/// and otherwise drops only what it added (the table and the index of Explorar).
/// </summary>
public class AccountAddressesMigrationSqlServerTests
{
    // The migration right before AddAccountAddresses, where its rollback.sql takes the database.
    private const string BeforeAddresses = "20261016162347_AddBusinessDirectory";
    private const string Migration = "20261017070332_AddAccountAddresses";

    [SqlServerFact]
    public async Task Checks_ShouldKeepEveryRowOneTheDomainWrites_AndTheAddressesGoWithTheAccount()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var userId = await SeedAccountWithAnAddressAsync(database);

        foreach (var postalCode in new[] { "0670", "06A00", " 0670" })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database,
                $"UPDATE [AccountAddresses] SET [PostalCode] = {postalCode} WHERE [UserId] = {userId}"));
            Assert.Equal(547, error.Number);
            Assert.Contains("CK_AccountAddresses_PostalCode", error.Message);
        }

        await ExecuteAsync(database, $"DELETE FROM [AspNetUsers] WHERE [Id] = {userId}");
        await using var check = database.CreateContext();
        Assert.Equal(0, await CountAsync(check, $"SELECT COUNT(*) AS [Value] FROM [AccountAddresses]"));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAnAccountSavedAnAddress_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        await SeedAccountWithAnAddressAsync(database);

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeAddresses));
        Assert.Equal(50017, error.Number);
        Assert.Contains("Customer accounts saved addresses", error.Message);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [AccountAddresses]"));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [AccountAddresses]");
        }

        await MigrateAsync(database, BeforeAddresses);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeAddresses, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'AccountAddresses'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = N'IX_DeliveryZonePostalCodes_PostalCode'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [AspNetUsers]")));
        }

        // Rolling back again does nothing, and forward again the table and the index are back.
        await MigrateAsync(database, BeforeAddresses);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext();
        Assert.Equal((1, 1), (
            await CountAsync(forward, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'AccountAddresses'"),
            await CountAsync(forward, $"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = N'IX_DeliveryZonePostalCodes_PostalCode'")));
    }

    /// <summary>A customer account with its home saved.</summary>
    private static async Task<Guid> SeedAccountWithAnAddressAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "cliente@demo.com",
            NormalizedUserName = "CLIENTE@DEMO.COM",
            Email = "cliente@demo.com",
            NormalizedEmail = "CLIENTE@DEMO.COM",
            FirstName = "María Fernanda",
            LastName = "López",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Customer,
            IsActive = true
        };
        context.Users.Add(user);
        context.AccountAddresses.Add(AccountAddress.Create(user.Id, "Casa", "06700", "Colima 145, Roma Norte", null, addressesKept: 0));
        await context.SaveChangesAsync();
        return user.Id;
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
        await context.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
