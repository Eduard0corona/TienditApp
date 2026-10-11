using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// SpecialDays (Fase 21) is a table of its own, one row per date: its checks keep every row a valid rule, its unique
/// index keeps one rule per date even when two people save at once, the version deployed before works on the new
/// schema without knowing it, and rolling it back drops only the table.
/// </summary>
public class SpecialDaysSqlServerTests
{
    // The migration right before AddSpecialDays, where its rollback.sql takes the database.
    private const string BeforeSpecialDays = "20261010074254_AddOpeningHours";

    // The business's today when the rows below were marked.
    private static readonly DateOnly Today = new(2026, 10, 11);

    private static readonly DateOnly Christmas = new(2026, 12, 25);

    [SqlServerFact]
    public async Task Table_ShouldHaveItsShape_AndRefuseRowsThatAreNotARule()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var (tenant, other) = await SeedAsync(database);
        await using var context = database.CreateContext(tenant.Id);

        Assert.Equal(1, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
            WHERE c.[object_id] = OBJECT_ID(N'dbo.SpecialDays') AND c.[name] = N'Date' AND t.[name] = N'date' AND c.[is_nullable] = 0
            """));
        Assert.Equal(4, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
            WHERE c.[object_id] = OBJECT_ID(N'dbo.SpecialDays') AND t.[name] = N'time' AND c.[scale] = 0 AND c.[is_nullable] = 1
                AND c.[name] IN (N'FirstOpens', N'FirstCloses', N'SecondOpens', N'SecondCloses')
            """));
        Assert.Equal(1, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
            WHERE c.[object_id] = OBJECT_ID(N'dbo.SpecialDays') AND c.[name] = N'Reason' AND t.[name] = N'nvarchar' AND c.[max_length] = 80
            """));
        Assert.Equal(2, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
            WHERE c.[object_id] = OBJECT_ID(N'dbo.SpecialDays') AND t.[name] = N'int' AND c.[is_nullable] = 0 AND c.[name] IN (N'Kind', N'Revision')
            """));
        Assert.Equal(
            new[] { "CK_SpecialDays_Kind", "CK_SpecialDays_Openings", "CK_SpecialDays_Reason", "CK_SpecialDays_Revision", "CK_SpecialDays_SecondOpening" },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] AS [Value] FROM sys.check_constraints WHERE [parent_object_id] = OBJECT_ID(N'dbo.SpecialDays') ORDER BY [name]
                """).ToListAsync());
        Assert.Equal(
            new[] { "IX_SpecialDays_TenantId_Date 1", "IX_SpecialDays_TenantId_PeriodId 0" },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] + N' ' + CAST([is_unique] AS nvarchar(1)) AS [Value] FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.SpecialDays') AND [name] LIKE N'IX[_]%' ORDER BY [name]
                """).ToListAsync());

        var nine = new TimeOnly(9, 0);
        var two = new TimeOnly(14, 0);
        var four = new TimeOnly(16, 0);
        var six = new TimeOnly(18, 0);
        await RefusedAsync(context, "CK_SpecialDays_Kind", tenant.Id, 2, null, null, null, null, null, 1);
        await RefusedAsync(context, "CK_SpecialDays_Openings", tenant.Id, (int)SpecialDayKind.Closed, nine, two, null, null, null, 1);
        await RefusedAsync(context, "CK_SpecialDays_Openings", tenant.Id, (int)SpecialDayKind.SpecialHours, null, null, null, null, null, 1);
        await RefusedAsync(context, "CK_SpecialDays_Openings", tenant.Id, (int)SpecialDayKind.SpecialHours, nine, two, four, null, null, 1);
        await RefusedAsync(context, "CK_SpecialDays_SecondOpening", tenant.Id, (int)SpecialDayKind.SpecialHours, nine, two, new TimeOnly(13, 0), six, null, 1);
        await RefusedAsync(context, "CK_SpecialDays_SecondOpening", tenant.Id, (int)SpecialDayKind.SpecialHours, nine, new TimeOnly(2, 0), four, six, null, 1);
        await RefusedAsync(context, "CK_SpecialDays_SecondOpening", tenant.Id, (int)SpecialDayKind.SpecialHours, nine, two, four, four, null, 1);
        await RefusedAsync(context, "CK_SpecialDays_Reason", tenant.Id, (int)SpecialDayKind.Closed, null, null, null, null, "", 1);
        await RefusedAsync(context, "CK_SpecialDays_Reason", tenant.Id, (int)SpecialDayKind.Closed, null, null, null, null, " ", 1);
        await RefusedAsync(context, "CK_SpecialDays_Reason", tenant.Id, (int)SpecialDayKind.Closed, null, null, null, null, "   ", 1);
        await RefusedAsync(context, "CK_SpecialDays_Revision", tenant.Id, (int)SpecialDayKind.Closed, null, null, null, null, null, 0);

        // What the domain writes goes in: two openings, the second one past midnight.
        await InsertAsync(context, tenant.Id, (int)SpecialDayKind.SpecialHours, nine, two, four, new TimeOnly(1, 0), "Fin de año", 1);
        // One rule per date and business; another business marks the same date.
        var duplicate = await Assert.ThrowsAsync<SqlException>(() =>
            InsertAsync(context, tenant.Id, (int)SpecialDayKind.Closed, null, null, null, null, "Navidad", 1));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("IX_SpecialDays_TenantId_Date", duplicate.Message);
        await InsertAsync(context, other.Id, (int)SpecialDayKind.Closed, null, null, null, null, "Navidad", 1);
        Assert.Equal(2, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM SpecialDays WHERE [Date] = {Christmas}"));
        // A reason made only of emoji weighs nothing in the default collation (it equals '' there), and it is a reason.
        await InsertAsync(context, tenant.Id, (int)SpecialDayKind.Closed, null, null, null, null, "🎄", 1, Christmas.AddDays(-1));
        await InsertAsync(context, tenant.Id, (int)SpecialDayKind.Closed, null, null, null, null, "⭐", 1, Christmas.AddDays(1));
        Assert.Equal(
            new[] { "🎄", "⭐" },
            await context.Database.SqlQuery<string>($"""
                SELECT [Reason] AS [Value] FROM SpecialDays WHERE [Date] <> {Christmas} ORDER BY [Date]
                """).ToListAsync());
    }

    [SqlServerFact]
    public async Task Reason_ThatTheRequestAndTheRuleTake_ShouldBeStoredAsTheRuleKeepsIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        // Whatever the collation weighs: emoji alone, a flag, symbols, 40 characters that are 20 emoji, spaces around
        // (the rule trims them) or alone (no reason at all).
        var twentyEmoji = string.Concat(Enumerable.Repeat("🎉", 20));
        string?[] reasons = ["🎄", "⭐", " 🇲🇽 ", "☕✨", twentyEmoji, "   ", "Navidad 🎄"];

        await using (var context = database.CreateContext(tenant.Id))
        {
            var calendar = new SpecialDayCalendar(tenant.Id, Today, []);
            foreach (var (reason, index) in reasons.Select((reason, index) => (reason, index)))
            {
                var date = Christmas.AddDays(index);
                var request = new AddSpecialDayCommandValidator().Validate(
                    new AddSpecialDayCommand(date, null, SpecialDayKind.Closed, null, reason));
                Assert.True(request.IsValid, reason);
                context.SpecialDays.AddRange(calendar.Add(date, date, SpecialDayRule.Closed(reason)).Added);
            }
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
            Assert.Equal(
                new[] { "🎄", "⭐", "🇲🇽", "☕✨", twentyEmoji, null, "Navidad 🎄" },
                (await context.SpecialDays.AsNoTracking().OrderBy(d => d.Date).ToListAsync()).Select(d => d.Rule.Reason));
    }

    [SqlServerFact]
    public async Task Rows_ShouldStayInTheirBusiness_AndRefuseAStaleEditOrADateTakenMeanwhile()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        var nochebuena = SpecialDayRule.Hours([new DailyOpening(new TimeOnly(9, 0), new TimeOnly(18, 0))], "Nochebuena");
        Guid periodId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var change = new SpecialDayCalendar(tenant.Id, Today, []).Add(Christmas.AddDays(-1), Christmas, SpecialDayRule.Closed("Navidad"));
            context.SpecialDays.AddRange(change.Added);
            await context.SaveChangesAsync();
            periodId = change.PeriodId;
        }
        await using (var context = database.CreateContext(other.Id))
        {
            context.SpecialDays.AddRange(new SpecialDayCalendar(other.Id, Today, []).Add(Christmas, Christmas, SpecialDayRule.Closed(null)).Added);
            await context.SaveChangesAsync();
        }

        // Each business reads its own dates only, rule and all.
        await using (var context = database.CreateContext(tenant.Id))
        {
            var rows = await context.SpecialDays.AsNoTracking().OrderBy(d => d.Date).ToListAsync();
            Assert.Equal(new[] { Christmas.AddDays(-1), Christmas }, rows.Select(d => d.Date));
            Assert.All(rows, row =>
            {
                Assert.Equal(tenant.Id, row.TenantId);
                Assert.Equal(SpecialDayRule.Closed("Navidad"), row.Rule);
            });
        }
        await using (var context = database.CreateContext(other.Id))
            Assert.Equal(SpecialDayRule.Closed(null), Assert.Single(await context.SpecialDays.AsNoTracking().ToListAsync()).Rule);

        // Two people edit the same period: the second one saves over rows another revision already changed.
        await using (var first = database.CreateContext(tenant.Id))
        await using (var second = database.CreateContext(tenant.Id))
        {
            var firstCalendar = new SpecialDayCalendar(tenant.Id, Today, await first.SpecialDays.ToListAsync());
            var secondCalendar = new SpecialDayCalendar(tenant.Id, Today, await second.SpecialDays.ToListAsync());

            Assert.Equal(2, firstCalendar.Change(periodId, Christmas.AddDays(-1), Christmas, nochebuena).Modified.Count);
            await first.SaveChangesAsync();

            var stale = secondCalendar.Change(periodId, Christmas.AddDays(-1), Christmas.AddDays(1), SpecialDayRule.Closed("Navidad"));
            second.SpecialDays.AddRange(stale.Added);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }
        await using (var context = database.CreateContext(tenant.Id))
        {
            var rows = await context.SpecialDays.AsNoTracking().ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Equal(nochebuena, row.Rule);
                Assert.Equal(2, row.Revision);
            });
        }

        // Two people mark the same free date: the database keeps the first one.
        await using (var first = database.CreateContext(tenant.Id))
        await using (var second = database.CreateContext(tenant.Id))
        {
            var firstCalendar = new SpecialDayCalendar(tenant.Id, Today, await first.SpecialDays.ToListAsync());
            var secondCalendar = new SpecialDayCalendar(tenant.Id, Today, await second.SpecialDays.ToListAsync());

            first.SpecialDays.AddRange(firstCalendar.Add(Christmas.AddDays(6), Christmas.AddDays(7), SpecialDayRule.Closed("Año Nuevo")).Added);
            await first.SaveChangesAsync();

            second.SpecialDays.AddRange(secondCalendar.Add(Christmas.AddDays(7), Christmas.AddDays(7), SpecialDayRule.Closed(null)).Added);
            var taken = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(taken.InnerException).Number);
        }
        await using (var context = database.CreateContext(tenant.Id))
            Assert.Equal(4, await context.SpecialDays.CountAsync());
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepWorking_AndRollingBackShouldDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var (tenant, _) = await SeedAsync(database);
        var hours = OpeningHours.Create([new OpeningInterval(DayOfWeek.Friday, new TimeOnly(18, 0), new TimeOnly(2, 0))]);
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await context.TenantSettings.SingleAsync()).SetOpeningHours(hours);
            context.SpecialDays.AddRange(new SpecialDayCalendar(tenant.Id, Today, []).Add(Christmas, Christmas, SpecialDayRule.Closed("Navidad")).Added);
            await context.SaveChangesAsync();
        }

        // The version deployed before does not know the table: it reads and saves the settings on the new schema.
        await using (var context = CreateVersionBeforeSpecialDays(database, tenant.Id))
        {
            var settings = await context.TenantSettings.SingleAsync();
            Assert.Equal(hours, settings.OpeningHours);
            settings.SetInsufficientStockPolicy(InsufficientStockPolicy.Warn);
            await context.SaveChangesAsync();
        }

        // Back to the version before special days: the table goes, the settings and their hours stay.
        await MigrateAsync(database, BeforeSpecialDays);
        await using (var context = CreateVersionBeforeSpecialDays(database, tenant.Id))
        {
            Assert.Equal(0, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.objects WHERE [name] = N'SpecialDays'"));
            var settings = await context.TenantSettings.SingleAsync();
            Assert.Equal(hours, settings.OpeningHours);
            Assert.Equal(InsufficientStockPolicy.Warn, settings.InsufficientStockPolicy);
            settings.SetInsufficientStockPolicy(InsufficientStockPolicy.Block);
            await context.SaveChangesAsync();
        }

        // Forward again: no special days until the owner marks them again.
        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Empty(await context.SpecialDays.ToListAsync());
            var settings = await context.TenantSettings.AsNoTracking().SingleAsync();
            Assert.Equal(hours, settings.OpeningHours);
            Assert.Equal(InsufficientStockPolicy.Block, settings.InsufficientStockPolicy);
        }
    }

    private static async Task<(Tenant Tenant, Tenant Other)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Ferretería", slug, BusinessType.Retail, $"{slug}@demo.com");
        var other = new Tenant("Taquería", slug + "b", BusinessType.Food, $"{slug}b@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, TenantCapability.Retail), new TenantSettings(other.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    // A row written straight to the table, past the app's own checks: on Christmas unless another date is given.
    private static Task<int> InsertAsync(
        ApplicationDbContext context, Guid tenantId, int kind,
        TimeOnly? firstOpens, TimeOnly? firstCloses, TimeOnly? secondOpens, TimeOnly? secondCloses, string? reason, int revision,
        DateOnly? date = null)
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO SpecialDays (Id, TenantId, PeriodId, [Date], Kind, FirstOpens, FirstCloses, SecondOpens, SecondCloses, Reason, Revision, CreatedAt)
            VALUES ({Guid.NewGuid()}, {tenantId}, {Guid.NewGuid()}, {date ?? Christmas}, {kind}, {firstOpens}, {firstCloses}, {secondOpens}, {secondCloses},
                {reason}, {revision}, {DateTimeOffset.UtcNow})
            """);

    private static async Task RefusedAsync(
        ApplicationDbContext context, string constraint, Guid tenantId, int kind,
        TimeOnly? firstOpens, TimeOnly? firstCloses, TimeOnly? secondOpens, TimeOnly? secondCloses, string? reason, int revision)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            InsertAsync(context, tenantId, kind, firstOpens, firstCloses, secondOpens, secondCloses, reason, revision));
        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message);
    }

    private static VersionBeforeSpecialDaysContext CreateVersionBeforeSpecialDays(SqlServerTestDatabase database, Guid tenantId)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new VersionBeforeSpecialDaysContext(options, tenant, mediator));

    /// <summary>The app as deployed before Fase 21: it maps the weekly hours, not the special days.</summary>
    private sealed class VersionBeforeSpecialDaysContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<SpecialDay>();
        }
    }

    /// <summary>Applies the migrations up to <paramref name="target"/> (all of them without one), as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string? target = null)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
