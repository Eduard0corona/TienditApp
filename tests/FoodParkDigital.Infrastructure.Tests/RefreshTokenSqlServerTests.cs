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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Refresh tokens in SQL Server: a token renews its session once even when two requests present it together, and
/// the rows the previous version keeps writing while the migration is applied still work.
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
    public async Task RowsWrittenByThePreviousVersion_ShouldGetASessionEach_AndStillBeFoundByTheirValue()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await SeedUserAsync(database);
        var now = DateTimeOffset.UtcNow;
        await using (var context = database.CreateContext())
        {
            // The insert of the previous version: the plain value, no hash and no session.
            foreach (var (value, revokedAt, replacement) in new[]
                     {
                         ("valor-activo", (DateTimeOffset?)null, (string?)null),
                         ("valor-reemplazado", now, "valor-activo")
                     })
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO RefreshTokens (Id, UserId, Token, ExpiresAt, CreatedAt, RevokedAt, ReplacedByToken)
                    VALUES ({Guid.NewGuid()}, {userId}, {value}, {now.AddDays(7)}, {now}, {revokedAt}, {replacement})
                    """);
            }
        }

        await using (var context = database.CreateContext())
        {
            var legacy = await context.RefreshTokens.AsNoTracking().ToListAsync();
            Assert.All(legacy, token => Assert.NotEqual(Guid.Empty, token.SessionId));
            Assert.NotEqual(legacy[0].SessionId, legacy[1].SessionId);
            Assert.NotNull(await UserSessions.FindAsync(context, "valor-activo", CancellationToken.None));
        }

        await using (var context = database.CreateContext())
            Assert.Equal(2, await UserSessions.HashLegacyTokensAsync(context, 100, CancellationToken.None));

        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await PlainValuesAsync(context));
            Assert.Equal(0, await NullValuesAsync(context));
            var active = await UserSessions.FindAsync(context, "valor-activo", CancellationToken.None);
            var replaced = await UserSessions.FindAsync(context, "valor-reemplazado", CancellationToken.None);
            Assert.Equal(RefreshToken.HashOf("valor-activo"), active!.TokenHash);
            Assert.True(replaced!.WasRotated);
        }
    }

    [SqlServerFact]
    public async Task TokensOfThisVersion_ShouldKeepAnEmptyValue_ThatThePreviousVersionStillReads()
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

        var renewed = await RenewAsync(database, clock, signIn.Value);
        Assert.True(renewed.IsSuccess, renewed.Error);
        await using (var context = database.CreateContext())
            await UserSessions.EndAllAsync(
                context, userId, RefreshTokenRevocationReason.PasswordChanged, clock.UtcNow, CancellationToken.None);

        // The previous version reads Token as required (going back to it with a swap): no row may leave it null.
        await using var check = database.CreateContext();
        Assert.Equal(2, await check.RefreshTokens.CountAsync());
        Assert.Equal(0, await NullValuesAsync(check));
        Assert.Equal(0, await PlainValuesAsync(check));
    }

    [SqlServerFact]
    public async Task RowsLeftWithoutAHashByARollback_ShouldOpenNothing_AndNotStopTheHashing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await SeedUserAsync(database);
        var now = DateTimeOffset.UtcNow;
        await using (var context = database.CreateContext())
        {
            // Two sessions hashed by this version whose hash the rollback dropped (applying the migration again adds
            // the column empty), and a token the previous version issued meanwhile.
            foreach (var value in new[] { "", "", "valor-activo" })
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO RefreshTokens (Id, UserId, Token, ExpiresAt, CreatedAt)
                    VALUES ({Guid.NewGuid()}, {userId}, {value}, {now.AddDays(7)}, {now})
                    """);
            }
        }

        // Only the plain value is hashed: the empty ones would all get the same hash, which the unique index refuses.
        await using (var context = database.CreateContext())
            Assert.Equal(1, await UserSessions.HashLegacyTokensAsync(context, 100, CancellationToken.None));

        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await UserSessions.HashLegacyTokensAsync(context, 100, CancellationToken.None));
            Assert.NotNull(await UserSessions.FindAsync(context, "valor-activo", CancellationToken.None));
            // SQL Server compares N' ' equal to N'': a blank value is refused before the lookup.
            Assert.Null(await UserSessions.FindAsync(context, "", CancellationToken.None));
            Assert.Null(await UserSessions.FindAsync(context, " ", CancellationToken.None));
            Assert.Equal(2, await context.RefreshTokens.CountAsync(t => t.TokenHash == null));
        }
    }

    /// <summary>Rows that keep the plain value of a token or of its replacement.</summary>
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
