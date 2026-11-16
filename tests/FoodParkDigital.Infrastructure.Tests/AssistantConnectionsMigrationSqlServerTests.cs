using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Assistants;
using FoodParkDigital.Domain.Assistants.Entities;
using FoodParkDigital.Domain.Assistants.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.AssistantAuthorization;
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
/// AddAssistantConnections (Fase 32E, ADR 0097): the OAuth clients assistants register, the codes a customer's permission
/// gives, the connections and their tokens. Their tables are born empty and the accounts that exist keep working; the
/// database refuses what the aggregates never write (a secret that is not a hash, a second working connection with the same
/// assistant, a code redeemed without its connection, a rotated access token, a client deleted while connected) and the
/// account takes all of it when it is deleted; rolling back stops with error 50065 while an assistant is connected, and
/// otherwise drops only what it added. The daily cleanup deletes what expired a week ago, what was disconnected 30 days ago
/// and the clients never connected.
/// </summary>
public class AssistantConnectionsMigrationSqlServerTests
{
    // The migration right before AddAssistantConnections, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261115151611_AddAssistantOrders";
    private const string Migration = "20261116030110_AddAssistantConnections";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private const string Resource = "https://app.test/mcp/cuenta";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [SqlServerFact]
    public async Task WhatExists_ShouldKeepWorking_AndTheDatabaseShouldRefuseWhatTheAggregatesNeverWrite()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var account = await SeedAccountAsync(BeforeAssistantConnections.CreateContext(database));

        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext())
        {
            Assert.Empty(await context.AssistantClients.AsNoTracking().ToListAsync());
            Assert.Equal(1, await context.Users.CountAsync());
        }

        // What the domain writes is kept: a client with its addresses, a code redeemed for a connection and its tokens.
        var (client, code, connection, access, refresh) = Connected(account, 'a', Now);
        await using (var write = database.CreateContext())
        {
            write.AddRange(client, code, connection, access, refresh);
            await write.SaveChangesAsync();
        }
        await using var check = database.CreateContext();
        var storedClient = await check.AssistantClients.AsNoTracking().SingleAsync();
        Assert.Equal("Claude", storedClient.Name);
        Assert.Equal([Callback, "http://127.0.0.1:33418/callback"], storedClient.RedirectUris);
        var storedCode = await check.AssistantAuthorizationCodes.AsNoTracking().SingleAsync();
        Assert.Equal((connection.Id, Challenge, Resource, "cuenta"), (storedCode.ConnectionId!.Value, storedCode.CodeChallenge, storedCode.Resource, storedCode.Scope));
        Assert.Equal(AssistantSecrets.HashOf(new string('a', 43)), storedCode.CodeHash);
        Assert.Equal(2, await check.AssistantTokens.CountAsync(t => t.ConnectionId == connection.Id));
        var storedConnection = await check.AssistantConnections.AsNoTracking().SingleAsync();
        Assert.Equal((account, client.Id, true), (storedConnection.UserId, storedConnection.ClientId, storedConnection.IsActive));

        foreach (var (sql, constraint) in new (string, string)[]
        {
            ($"UPDATE [AssistantClients] SET [ClientId] = REPLICATE('c', 42) WHERE [Id] = '{client.Id}'", "CK_AssistantClients_ClientId"),
            ($"UPDATE [AssistantAuthorizationCodes] SET [CodeHash] = 'abc' WHERE [Id] = '{code.Id}'", "CK_AssistantAuthorizationCodes_CodeHash"),
            ($"UPDATE [AssistantAuthorizationCodes] SET [CodeChallenge] = 'plain' WHERE [Id] = '{code.Id}'", "CK_AssistantAuthorizationCodes_CodeChallenge"),
            ($"UPDATE [AssistantAuthorizationCodes] SET [ConnectionId] = NULL WHERE [Id] = '{code.Id}'", "CK_AssistantAuthorizationCodes_Redeemed"),
            ($"UPDATE [AssistantAuthorizationCodes] SET [ConnectionId] = '{Guid.NewGuid()}' WHERE [Id] = '{code.Id}'", "FK_AssistantAuthorizationCodes_AssistantConnections_ConnectionId"),
            ($"UPDATE [AssistantTokens] SET [TokenHash] = 'abc' WHERE [Id] = '{access.Id}'", "CK_AssistantTokens_TokenHash"),
            ($"UPDATE [AssistantTokens] SET [Kind] = 3 WHERE [Id] = '{access.Id}'", "CK_AssistantTokens_Kind"),
            ($"UPDATE [AssistantTokens] SET [RotatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = '{access.Id}'", "CK_AssistantTokens_RotatedAt"),
            ($"UPDATE [AssistantTokens] SET [TokenHash] = (SELECT [TokenHash] FROM [AssistantTokens] WHERE [Id] = '{refresh.Id}') WHERE [Id] = '{access.Id}'", "IX_AssistantTokens_TokenHash"),
            // A second working connection with the same assistant; the client of a connection deleted.
            ($"""
              INSERT INTO [AssistantConnections] ([Id], [UserId], [ClientId], [Scope], [GrantedAt], [CreatedAt])
              VALUES (NEWID(), '{account}', '{client.Id}', 'cuenta', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
              """, "IX_AssistantConnections_UserId_ClientId"),
            ($"DELETE FROM [AssistantClients] WHERE [Id] = '{client.Id}'", "FK_AssistantConnections_AssistantClients_ClientId"),
        })
        {
            var error = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(sql));
            Assert.True(error is SqlException, $"Not refused ({error?.GetType().Name}): {sql}");
            Assert.Contains(constraint, error!.Message);
        }

        // Once disconnected, connecting the same assistant again is a new connection.
        await check.Database.ExecuteSqlRawAsync($"UPDATE [AssistantConnections] SET [RevokedAt] = SYSDATETIMEOFFSET() WHERE [Id] = '{connection.Id}'");
        check.AssistantConnections.Add(AssistantConnection.Grant(account, client.Id, "cuenta", Now));
        await check.SaveChangesAsync();

        // The account takes all of it when it is deleted; the client stays, for the cleanup.
        await check.Database.ExecuteSqlRawAsync($"DELETE FROM [AspNetUsers] WHERE [Id] = '{account}'");
        Assert.Equal((0, 0, 0, 1), (
            await check.AssistantConnections.CountAsync(),
            await check.AssistantTokens.CountAsync(),
            await check.AssistantAuthorizationCodes.CountAsync(),
            await check.AssistantClients.CountAsync()));
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSigningUpAccounts()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version signs up a customer, unaware of the tables.
        var account = await SeedAccountAsync(BeforeAssistantConnections.CreateContext(database));

        await using var context = database.CreateContext();
        Assert.True(await context.Users.AnyAsync(u => u.Id == account));
        Assert.Empty(await context.AssistantConnections.Where(c => c.UserId == account).ToListAsync());
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileAnAssistantIsConnected_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var account = await SeedAccountAsync(database.CreateContext());
        var (client, code, connection, access, refresh) = Connected(account, 'a', Now);
        await using (var context = database.CreateContext())
        {
            context.AddRange(client, code, connection, access, refresh);
            await context.SaveChangesAsync();
        }

        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50065 }, stopped?.Message ?? "it was not stopped");
        await using (var after = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(after));
            Assert.Equal(1, await after.AssistantConnections.CountAsync(c => c.RevokedAt == null));
        }

        // Disconnected, it goes with the tables: the account stays.
        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE [AssistantConnections] SET [RevokedAt] = SYSDATETIMEOFFSET()");
        }
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAssistantConnections.CreateContext(database))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            foreach (var table in new[] { "AssistantClients", "AssistantAuthorizationCodes", "AssistantConnections", "AssistantTokens" })
                Assert.Null(await context.Database.SqlQueryRaw<int?>($"SELECT OBJECT_ID(N'dbo.{table}') AS [Value]").SingleAsync());
            Assert.True(await context.Users.AnyAsync(u => u.Id == account));
        }

        // Rolling back again does nothing; forward again, the tables are back, empty.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext();
        Assert.Empty(await forward.AssistantConnections.ToListAsync());
    }

    [SqlServerFact]
    public async Task Cleanup_ShouldDelete_WhatExpiredAWeekAgo_TheDisconnected30DaysAgo_AndTheClientsNeverConnected()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var account = await SeedAccountAsync(database.CreateContext());

        // Connected 40 days ago and disconnected 31 days ago: its code and tokens expired long ago.
        var old = Connected(account, 'a', Now.AddDays(-40));
        old.Connection.Revoke(Now.AddDays(-31));
        // Connected a day ago and working; its access token expired 23 hours ago, kept for the week.
        var working = Connected(account, 'b', Now.AddDays(-1));
        // Registered eight days ago and never connected; registered a day ago, still in its week.
        var abandoned = AssistantClient.Register(new string('c', 43), "Bot", [Callback], Now.AddDays(-8));
        var fresh = AssistantClient.Register(new string('d', 43), "Nuevo", [Callback], Now.AddDays(-1));
        await using (var context = database.CreateContext())
        {
            context.AddRange(old.Client, old.Code, old.Connection, old.Access, old.Refresh);
            context.AddRange(working.Client, working.Code, working.Connection, working.Access, working.Refresh);
            context.AddRange(abandoned, fresh);
            await context.SaveChangesAsync();
        }

        await using var services = new ServiceCollection()
            .AddScoped<IApplicationDbContext>(_ => database.CreateContext())
            .BuildServiceProvider();
        var cleanup = new AssistantAuthorizationCleanupService(
            services.GetRequiredService<IServiceScopeFactory>(), new Clock(Now), NullLogger<AssistantAuthorizationCleanupService>.Instance);

        // The old connection's two tokens and its code, the connection, and then its client and the abandoned one.
        Assert.Equal(6, await cleanup.DeleteBatchAsync(CancellationToken.None));
        Assert.Equal(0, await cleanup.DeleteBatchAsync(CancellationToken.None));
        await using var check = database.CreateContext();
        Assert.Equal(new[] { working.Client.Id, fresh.Id }.Order(), (await check.AssistantClients.Select(c => c.Id).ToListAsync()).Order());
        Assert.Equal(working.Connection.Id, (await check.AssistantConnections.SingleAsync()).Id);
        Assert.Equal(2, await check.AssistantTokens.CountAsync());
        Assert.Equal(1, await check.AssistantAuthorizationCodes.CountAsync());
    }

    private sealed record Granted(
        AssistantClient Client, AssistantAuthorizationCode Code, AssistantConnection Connection, AssistantToken Access, AssistantToken Refresh);

    /// <summary>An assistant connected to <paramref name="account"/> at <paramref name="at"/>, as the exchange of its code leaves it.</summary>
    private static Granted Connected(Guid account, char secret, DateTimeOffset at)
    {
        var client = AssistantClient.Register(new string(secret, 43), "Claude", [Callback, "http://127.0.0.1:33418/callback"], at);
        var code = AssistantAuthorizationCode.Issue(client, account, new string(secret, 43), Callback, Challenge, Resource, "cuenta", at);
        var connection = AssistantConnection.Grant(account, client.Id, "cuenta", at);
        code.Redeem(connection.Id, at.AddMinutes(1));
        var access = AssistantToken.Issue(connection.Id, AssistantTokenKind.Access, new string(secret, 42) + "A", at);
        var refresh = AssistantToken.Issue(connection.Id, AssistantTokenKind.Refresh, new string(secret, 42) + "R", at);
        return new Granted(client, code, connection, access, refresh);
    }

    /// <summary>A customer account, saved by <paramref name="seed"/>.</summary>
    private static async Task<Guid> SeedAccountAsync(ApplicationDbContext seed)
    {
        await using (seed)
        {
            var email = $"{Guid.NewGuid():N}@demo.com";
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                FirstName = "María Fernanda",
                LastName = "López",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Role = UserRole.Customer,
                IsActive = true
            };
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
            return user.Id;
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

    private sealed class Clock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
