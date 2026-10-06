using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Identity;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.BackgroundJobs;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Refresh tokens in SQL Server: a token renews its session once even when two requests present it together, and the
/// migrations that hash the plain values and then drop their columns keep the sessions of the tokens issued before the
/// hashing.
/// </summary>
public class RefreshTokenSqlServerTests
{
    private static readonly JwtSettings Settings = new()
    {
        Secret = "refresh-token-sql-tests-signing-key-not-a-real-secret-0123",
        Issuer = "FoodParkDigital.Tests",
        Audience = "FoodParkDigital.Tests",
        RefreshTokenExpirationDays = 7
    };

    [SqlServerFact]
    public async Task TwoRenewalsWithTheSameToken_ShouldKeepOnlyTheFirstReplacement()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var clock = new MutableClock { UtcNow = DateTimeOffset.UtcNow };
        var userId = await SeedUserAsync(database);
        var signIn = new JwtTokenService(Options.Create(Settings), clock).GenerateRefreshToken(userId, Guid.NewGuid());
        await using (var context = database.CreateContext())
        {
            context.RefreshTokens.Add(signIn.Token);
            await context.SaveChangesAsync();
        }

        Result<AuthResponse>? first = null;
        // The second request has read the token when the first one saves its renewal.
        var beforeSave = new OneShotBeforeSave(async () => first = await RenewAsync(database, clock, signIn.Value));
        var second = await RenewAsync(database, clock, signIn.Value, beforeSave);

        Assert.True(beforeSave.Fired);
        Assert.True(first!.IsSuccess, first.Error);
        Assert.Equal(RefreshTokenCommandHandler.ConcurrentRenewalError, second.Error);
        await using var check = database.CreateContext();
        var tokens = await check.RefreshTokens.AsNoTracking().ToListAsync();
        // The losing request saved nothing: neither its revocation nor its new token.
        Assert.Equal(2, tokens.Count);
        Assert.Equal(RefreshTokenRevocationReason.Rotated, tokens.Single(t => t.Id == signIn.Token.Id).RevocationReason);
        var replacement = tokens.Single(t => t.Id != signIn.Token.Id);
        Assert.Equal(RefreshToken.HashOf(first.Value!.RefreshToken!), replacement.TokenHash);
        Assert.Equal(signIn.Token.SessionId, replacement.SessionId);
    }

    [SqlServerFact]
    public async Task Migrations_ShouldHashThePlainValuesLeft_ThenDropTheirColumns_WhileTheSessionsKeepWorking()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, LastMigrationOfPhase8);
        var userId = await SeedUserAsync(database);
        var now = DateTimeOffset.UtcNow;
        await using (var context = database.CreateContext())
        {
            foreach (var (value, hash, revokedAt, replacement) in new[]
                     {
                         // Issued before the hashing and not used since.
                         ("valor-activo", (string?)null, (DateTimeOffset?)null, (string?)null),
                         // Replaced before the hashing: presenting it again must still count as a copy.
                         ("valor-reemplazado", null, now, "valor-activo"),
                         // Issued by Phase 8: only its hash.
                         ("", RefreshToken.HashOf("valor-con-hash"), null, null),
                         // Left without its hash by a rollback: it opens nothing, and stays so.
                         ("", null, null, null)
                     })
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO RefreshTokens (Id, UserId, Token, TokenHash, ExpiresAt, CreatedAt, RevokedAt, ReplacedByToken)
                    VALUES ({Guid.NewGuid()}, {userId}, {value}, {hash}, {now.AddDays(7)}, {now}, {revokedAt}, {replacement})
                    """);
            }
        }

        // Phase 9: the plain values get their hash and are emptied; the columns stay for Phase 8.
        await MigrateAsync(database, HashingMigration);
        await using (var context = database.CreateContext())
        {
            // The hash computed in SQL is the one this version computes for the value presented.
            var active = await UserSessions.FindAsync(context, "valor-activo", CancellationToken.None);
            var replaced = await UserSessions.FindAsync(context, "valor-reemplazado", CancellationToken.None);
            Assert.True(active!.IsActive);
            Assert.True(replaced!.WasRotated);
            Assert.NotNull(await UserSessions.FindAsync(context, "valor-con-hash", CancellationToken.None));
            Assert.Equal(1, await context.RefreshTokens.CountAsync(t => t.TokenHash == null));
            Assert.Equal(0, await PlainValuesAsync(context));

            // A token issued meanwhile stores no value: the column, still there for Phase 8, stays null.
            context.RefreshTokens.Add(new RefreshToken(userId, Guid.NewGuid(), "valor-nuevo", now, now.AddDays(7)));
            await context.SaveChangesAsync();
            Assert.Equal(1, await NullValuesAsync(context));
        }

        // Phase 10: the columns and their index go; the sessions stay.
        await MigrateAsync(database);
        await using (var context = database.CreateContext())
        {
            Assert.Equal((0, 0), await PlainValueSchemaAsync(context));
            Assert.True((await UserSessions.FindAsync(context, "valor-activo", CancellationToken.None))!.IsActive);
            Assert.True((await UserSessions.FindAsync(context, "valor-reemplazado", CancellationToken.None))!.WasRotated);
            Assert.NotNull(await UserSessions.FindAsync(context, "valor-nuevo", CancellationToken.None));
        }

        // Rolling both back gives the columns back, empty (Phase 8 reads a null Token as no plain value), and applying
        // them again ends the same way.
        await MigrateAsync(database, LastMigrationOfPhase8);
        await using (var context = database.CreateContext())
        {
            Assert.Equal((2, 1), await PlainValueSchemaAsync(context));
            Assert.Equal(0, await PlainValuesAsync(context));
        }

        await MigrateAsync(database);
        await using (var context = database.CreateContext())
        {
            Assert.Equal((0, 0), await PlainValueSchemaAsync(context));
            Assert.True((await UserSessions.FindAsync(context, "valor-activo", CancellationToken.None))!.IsActive);
        }
    }

    private const string LastMigrationOfPhase8 = "20261005095544_ScopeOrderLinesAndHistoryByTenant";
    private const string HashingMigration = "20261005175643_StopUsingPlainRefreshTokenValues";

    /// <summary>Applies the migrations up to <paramref name="target"/> (all of them without one), as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string? target = null)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>How many of the columns of the plain values RefreshTokens has, and whether it has their index.</summary>
    private static async Task<(int Columns, int Index)> PlainValueSchemaAsync(ApplicationDbContext context)
    {
        var columns = await context.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.RefreshTokens') AND [name] IN (N'Token', N'ReplacedByToken')
                """)
            .SingleAsync();
        var index = await context.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.RefreshTokens') AND [name] = N'IX_RefreshTokens_Token'
                """)
            .SingleAsync();
        return (columns, index);
    }

    /// <summary>Rows that keep the plain value of a token or of its replacement (columns the model no longer maps).</summary>
    private static Task<int> PlainValuesAsync(ApplicationDbContext context) =>
        context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM RefreshTokens WHERE Token <> '' OR ReplacedByToken IS NOT NULL")
            .SingleAsync();

    private static Task<int> NullValuesAsync(ApplicationDbContext context) =>
        context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM RefreshTokens WHERE Token IS NULL")
            .SingleAsync();

    private static async Task<Result<AuthResponse>> RenewAsync(
        SqlServerTestDatabase database,
        MutableClock clock,
        string refreshToken,
        params IInterceptor[] interceptors)
    {
        await using var context = database.CreateProductionLikeContext(
            new InMemoryApplicationDbContext.TestCurrentTenantService(),
            new InMemoryApplicationDbContext.NullMediator(),
            interceptors);
        return await new RefreshTokenCommandHandler(
                context,
                new JwtTokenService(Options.Create(Settings), clock),
                new TenantAccessService(context),
                clock,
                NullLogger<RefreshTokenCommandHandler>.Instance)
            .Handle(new RefreshTokenCommand(refreshToken), CancellationToken.None);
    }

    private static async Task<Guid> SeedUserAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "sesion@demo.com",
            NormalizedUserName = "SESION@DEMO.COM",
            Email = "sesion@demo.com",
            NormalizedEmail = "SESION@DEMO.COM",
            FirstName = "Sesión",
            LastName = "Demo",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Staff,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Runs a callback right before the next save, as a request that saves first.</summary>
    private sealed class OneShotBeforeSave(Func<Task> action) : SaveChangesInterceptor
    {
        private Func<Task>? _action = action;

        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _action, null) is { } action)
            {
                Fired = true;
                await action();
            }

            return result;
        }
    }
}
