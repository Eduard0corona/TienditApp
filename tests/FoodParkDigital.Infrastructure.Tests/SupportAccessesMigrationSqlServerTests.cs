using FoodParkDigital.Domain.Audit.Entities;
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
/// AddSupportAccesses (Fase 32G, ADR 0099): the platform admin's accesses to a business, with their reason and time. The
/// table is born empty and what exists keeps working; the database refuses what the aggregate never writes (a reason under
/// 10 characters, an end that is not after the start, a close outside its time, a business or an account that does not
/// exist) and keeps the history when the account is deleted (15 minutes to 4 hours is the domain's: a date function in a
/// check would not be portable to the relational tests' SQLite); rolling back stops with error 50066 once there is an
/// access, and otherwise drops only what it added.
/// </summary>
public class SupportAccessesMigrationSqlServerTests
{
    // The migration right before AddSupportAccesses, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261116030110_AddAssistantConnections";
    private const string Migration = "20261124213159_AddSupportAccesses";
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 18, 0, 0, TimeSpan.Zero);
    private const string Reason = "El dueño reporta que su corte de caja no cuadra.";

    [SqlServerFact]
    public async Task WhatExists_ShouldKeepWorking_AndTheDatabaseShouldRefuseWhatTheAggregateNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var seed = await SeedAsync(BeforeSupportAccesses.CreateContext(database));

        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.Empty(await context.SupportAccesses.AsNoTracking().ToListAsync());
            Assert.True(await context.Tenants.AnyAsync(t => t.Id == seed.TenantId));
        }

        // What the domain writes is kept: an access opened for 30 minutes and closed after 10.
        var access = SupportAccess.Start(seed.TenantId, seed.AdminId, Reason, 30, Now);
        access.End(Now.AddMinutes(10));
        await using (var write = database.CreateContext(seed.TenantId))
        {
            write.SupportAccesses.Add(access);
            await write.SaveChangesAsync();
        }
        await using (var check = database.CreateContext(seed.TenantId))
        {
            var stored = await check.SupportAccesses.AsNoTracking().SingleAsync();
            Assert.Equal((seed.TenantId, seed.AdminId, Reason), (stored.TenantId, stored.AdminUserId, stored.Reason));
            Assert.Equal((Now, Now.AddMinutes(30), Now.AddMinutes(10)), (stored.StartedAt, stored.ExpiresAt, stored.EndedAt!.Value));
        }

        var id = access.Id;
        foreach (var (sql, constraint) in new (string, string)[]
        {
            ($"UPDATE [SupportAccesses] SET [Reason] = N'revisar' WHERE [Id] = '{id}'", "CK_SupportAccesses_Reason"),
            ($"UPDATE [SupportAccesses] SET [ExpiresAt] = [StartedAt], [EndedAt] = NULL WHERE [Id] = '{id}'", "CK_SupportAccesses_Duration"),
            ($"UPDATE [SupportAccesses] SET [ExpiresAt] = DATEADD(MINUTE, -1, [StartedAt]), [EndedAt] = NULL WHERE [Id] = '{id}'", "CK_SupportAccesses_Duration"),
            ($"UPDATE [SupportAccesses] SET [EndedAt] = [ExpiresAt] WHERE [Id] = '{id}'", "CK_SupportAccesses_EndedAt"),
            ($"UPDATE [SupportAccesses] SET [EndedAt] = DATEADD(MINUTE, -1, [StartedAt]) WHERE [Id] = '{id}'", "CK_SupportAccesses_EndedAt"),
            ($"UPDATE [SupportAccesses] SET [TenantId] = '{Guid.NewGuid()}' WHERE [Id] = '{id}'", "FK_SupportAccesses_Tenants_TenantId"),
            ($"UPDATE [SupportAccesses] SET [AdminUserId] = '{Guid.NewGuid()}' WHERE [Id] = '{id}'", "FK_SupportAccesses_AspNetUsers_AdminUserId"),
            // The history stays: the account that opened it cannot go while it is there.
            ($"DELETE FROM [AspNetUsers] WHERE [Id] = '{seed.AdminId}'", "FK_SupportAccesses_AspNetUsers_AdminUserId")
        })
        {
            await using var context = database.CreateContext(seed.TenantId);
            var error = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(sql));
            Assert.True(error is SqlException, $"Not refused ({error?.GetType().Name}): {sql}");
            Assert.Contains(constraint, error!.Message);
        }

        // The longest and the shortest reason, and a close the minute before its time, are kept.
        await using var limits = database.CreateContext(seed.TenantId);
        Assert.Equal(1, await limits.Database.ExecuteSqlRawAsync(
            $"UPDATE [SupportAccesses] SET [Reason] = REPLICATE(N'a', 500), [ExpiresAt] = DATEADD(MINUTE, 240, [StartedAt]), [EndedAt] = DATEADD(MINUTE, 239, [StartedAt]) WHERE [Id] = '{id}'"));
        Assert.Equal(1, await limits.Database.ExecuteSqlRawAsync(
            $"UPDATE [SupportAccesses] SET [Reason] = REPLICATE(N'a', 10), [ExpiresAt] = DATEADD(MINUTE, 15, [StartedAt]), [EndedAt] = NULL WHERE [Id] = '{id}'"));
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepWorking_UnawareOfTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version signs up a business and its owner.
        var seed = await SeedAsync(BeforeSupportAccesses.CreateContext(database));

        await using var context = database.CreateContext(seed.TenantId);
        Assert.True(await context.Users.AnyAsync(u => u.Id == seed.AdminId));
        Assert.Empty(await context.SupportAccesses.ToListAsync());
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceThereIsAnAccess_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database.CreateContext());
        await using (var context = database.CreateContext(seed.TenantId))
        {
            context.SupportAccesses.Add(SupportAccess.Start(seed.TenantId, seed.AdminId, Reason, 15, Now));
            await context.SaveChangesAsync();
        }

        // Even ended long ago, it is history.
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50066 }, stopped?.Message ?? "it was not stopped");
        await using (var after = database.CreateContext(seed.TenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(after));
            Assert.Equal(1, await after.SupportAccesses.CountAsync());
        }

        // Without accesses, the table goes: the business and the account stay.
        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [SupportAccesses]");
        }
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeSupportAccesses.CreateContext(database))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQueryRaw<int?>("SELECT OBJECT_ID(N'dbo.SupportAccesses') AS [Value]").SingleAsync());
            Assert.True(await context.Users.AnyAsync(u => u.Id == seed.AdminId));
            Assert.True(await context.Tenants.AnyAsync(t => t.Id == seed.TenantId));
        }

        // Rolling back again does nothing; forward again, the table is back, empty.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.Empty(await forward.SupportAccesses.ToListAsync());
    }

    private sealed record Seed(Guid TenantId, Guid AdminId);

    /// <summary>A business and a platform admin account, saved by <paramref name="seed"/>.</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext seed)
    {
        await using (seed)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Barbería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
            var email = $"{slug}@plataforma.com";
            var admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                FirstName = "Ana",
                LastName = "Soporte",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Role = UserRole.SuperAdmin,
                IsActive = true
            };
            seed.Tenants.Add(tenant);
            seed.Users.Add(admin);
            await seed.SaveChangesAsync();
            return new Seed(tenant.Id, admin.Id);
        }
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
