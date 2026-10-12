using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
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
/// AddBranchServiceMode (Fase 22A) only adds a column with a default, so the version deployed before keeps adding and
/// editing branches over it, all with premises. Rolling it back stops with error 50011 while a branch only delivers
/// (without the column the previous version publishes the address of the main one), and otherwise drops only what it
/// added.
/// </summary>
public class BranchServiceModeMigrationSqlServerTests
{
    // The migration right before AddBranchServiceMode, where its rollback.sql takes the database.
    private const string BeforeServiceMode = "20261012145908_AddPlatformSettlements";
    private const string Migration = "20261012204959_AddBranchServiceMode";

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepAddingAndEditingBranches_WhichHavePremises()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedAsync(database);
        Guid kitchenId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var kitchen = new Branch(tenant.Id, "Cocina", "COCINA", new BranchContact(AddressLine1: "Privada Los Pinos 8"),
                serviceMode: BranchServiceMode.DeliveryOnly);
            context.Branches.Add(kitchen);
            await context.SaveChangesAsync();
            kitchenId = kitchen.Id;
        }

        // The version deployed before does not know the column: its branches take the default, and editing one that only
        // delivers leaves it as it was.
        await using (var previous = CreateVersionBeforeServiceMode(database, tenant.Id))
        {
            previous.Branches.Add(new Branch(tenant.Id, "Centro", "CENTRO", new BranchContact(AddressLine1: "5 de Mayo 10")));
            var kitchen = await previous.Branches.SingleAsync(b => b.Id == kitchenId);
            kitchen.Update("Cocina norte", kitchen.Code, new BranchContact(AddressLine1: "Privada Los Pinos 8"), BranchServiceMode.Premises);
            await previous.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenant.Id);
        var branches = await check.Branches.OrderBy(b => b.Code).ToListAsync();
        Assert.Equal(
            [("CENTRO", BranchServiceMode.Premises), ("COCINA", BranchServiceMode.DeliveryOnly)],
            branches.Select(b => (b.Code, b.ServiceMode)));
        Assert.Equal("Cocina norte", branches[1].Name);
    }

    [SqlServerFact]
    public async Task ServiceMode_ShouldBeOneTheDomainKnows()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedAsync(database);

        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            INSERT INTO Branches (Id, TenantId, Name, Code, IsDefault, IsActive, ServiceMode, CreatedAt)
            VALUES ({Guid.NewGuid()}, {tenant.Id}, N'Otra', N'OTRA', 0, 1, 2, SYSDATETIMEOFFSET())
            """));

        Assert.Equal(547, error.Number);
        Assert.Contains("CK_Branches_ServiceMode", error.Message);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileABranchOnlyDelivers_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.Branches.Add(Branch.CreateDefault(tenant.Id, BranchServiceMode.DeliveryOnly));
            await context.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeServiceMode));
        Assert.Equal(50011, error.Number);
        Assert.Contains("branches that only deliver", error.Message);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(BranchServiceMode.DeliveryOnly, (await context.Branches.SingleAsync()).ServiceMode);

            // Changed to premises on purpose, the rollback goes on.
            var main = await context.Branches.SingleAsync();
            main.Update(main.Name, main.Code, BranchContact.Empty, BranchServiceMode.Premises);
            await context.SaveChangesAsync();
        }

        // Without such branches, the rollback drops the column and its check, and nothing else.
        await MigrateAsync(database, BeforeServiceMode);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeServiceMode, await LatestMigrationAsync(context));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Branches') AND [name] = N'ServiceMode'
                """));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_Branches_ServiceMode'
                """));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM Branches"));
        }
        // Rolling back again does nothing.
        await MigrateAsync(database, BeforeServiceMode);

        // Forward again (to this migration; later ones apply on top): every branch has premises.
        await MigrateAsync(database, Migration);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(BranchServiceMode.Premises, (await context.Branches.SingleAsync()).ServiceMode);
            Assert.Equal(Migration, await LatestMigrationAsync(context));
        }
    }

    private static async Task<Tenant> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Cocina sin local", slug, BusinessType.Food, $"{slug}@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return tenant;
    }

    private static VersionBeforeServiceModeContext CreateVersionBeforeServiceMode(SqlServerTestDatabase database, Guid tenantId)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new VersionBeforeServiceModeContext(options, tenant, mediator));

    /// <summary>The app as deployed before Fase 22A: branches without a service mode.</summary>
    private sealed class VersionBeforeServiceModeContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Branch>().Ignore(b => b.ServiceMode);
        }
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
