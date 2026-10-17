using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Domain.Delivery;
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
/// The tables of the delivery zones (Fase 22B): a zone keeps its postal codes and cents in its business, a name and a
/// postal code are in one zone of a business at most (another business can have them too), a saved zone keeps the rows of
/// the postal codes it still has, deleting a zone takes its postal codes, the checks keep every row one the domain could
/// write, and rolling back stops while zones are saved and otherwise drops only the two tables. With the use cases: a save
/// that meets someone else's at the same moment is answered as stale or with the zone that took the name or postal code,
/// whatever order the database ran the rows in, and is undone whole.
/// </summary>
public class DeliveryZonesSqlServerTests
{
    private const string BeforeDeliveryZones = "20261012204959_AddBranchServiceMode";
    private const string Migration = "20261013002425_AddDeliveryZones";

    [SqlServerFact]
    public async Task Tables_ShouldHaveTheirIndexesAndKeys()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var indexes = await context.Database.SqlQuery<IndexRow>($"""
            SELECT [name] AS [Name], CAST([is_unique] AS bit) AS [IsUnique]
            FROM sys.indexes
            WHERE [object_id] IN (OBJECT_ID(N'dbo.DeliveryZones'), OBJECT_ID(N'dbo.DeliveryZonePostalCodes')) AND [type] > 0
            """).ToListAsync();
        Assert.True(Assert.Single(indexes, index => index.Name == "IX_DeliveryZonePostalCodes_TenantId_PostalCode").IsUnique);
        Assert.False(Assert.Single(indexes, index => index.Name == "IX_DeliveryZonePostalCodes_DeliveryZoneId").IsUnique);
        Assert.True(Assert.Single(indexes, index => index.Name == "IX_DeliveryZones_TenantId_Name").IsUnique);
        // Explorar asks which businesses reach a postal code, in all of them at once (Fase 24B).
        Assert.False(Assert.Single(indexes, index => index.Name == "IX_DeliveryZonePostalCodes_PostalCode").IsUnique);
        Assert.Equal(6, indexes.Count);

        var onDelete = await context.Database.SqlQuery<string>($"""
            SELECT [delete_referential_action_desc] AS [Value] FROM sys.foreign_keys
            WHERE [name] = N'FK_DeliveryZonePostalCodes_DeliveryZones_DeliveryZoneId'
            """).SingleAsync();
        Assert.Equal("CASCADE", onDelete);
    }

    [SqlServerFact]
    public async Task Zone_ShouldBeStoredWithItsPostalCodesAndCents_InItsBusinessOnly()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields(
                "Centro", 35.50m, 149.99m, 399.99m, ["72005", "72000"])));
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var zone = await check.DeliveryZones.Include(z => z.PostalCodes).SingleAsync();
            Assert.Equal((35.50m, 149.99m, 399.99m), (zone.Fee, zone.MinimumOrder, zone.FreeDeliveryFrom));
            Assert.Equal(["72000", "72005"], zone.PostalCodes.Select(code => code.PostalCode));
        }
        await using (var neighbor = database.CreateContext(other.Id))
        {
            Assert.Empty(await neighbor.DeliveryZones.ToListAsync());
            Assert.Empty(await neighbor.DeliveryZonePostalCodes.ToListAsync());
        }
    }

    [SqlServerFact]
    public async Task PostalCode_ShouldBeInOneZoneOfTheBusiness_WhileAnotherBusinessCanHaveItToo()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("Centro", "72000")));
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("Angelópolis", "72830", "72000")));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        await using (var neighbor = database.CreateContext(other.Id))
        {
            neighbor.DeliveryZones.Add(new DeliveryZone(other.Id, Fields("Centro", "72000")));
            await neighbor.SaveChangesAsync();
        }
    }

    [SqlServerFact]
    public async Task Name_ShouldBeInOneZoneOfTheBusiness_WithoutTellingCapitalsApart()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        await SeedZoneAsync(database, tenant.Id, "Centro", "72000");

        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("centro", "72005")));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        await SeedZoneAsync(database, other.Id, "Centro", "72000");
    }

    [SqlServerFact]
    public async Task Update_AddingThePostalCodeSomeoneAddedMeanwhile_ShouldBeStale()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var zoneId = await SeedZoneAsync(database, tenant.Id, "Centro", "72000");
        // Someone adds 72010 to the zone after this save read it, and before it writes. Its new postal code is written
        // before the zone, so the database answers with the unique index, not with the revision.
        await using var racing = Racing(database, tenant.Id, async other =>
            (await LoadAsync(other, zoneId)).Update(Fields("Centro", "72000", "72010")));

        var result = await UpdateAsync(racing, tenant.Id, zoneId, 1, "Centro", "72000", "72010");

        Assert.Equal(DeliveryZoneErrors.Stale, result.Error);
        await using var context = database.CreateContext(tenant.Id);
        var stored = await LoadAsync(context, zoneId);
        Assert.Equal(2, stored.Revision);
        Assert.Equal(["72000", "72010"], stored.PostalCodes.Select(code => code.PostalCode));
        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task Update_OfAZoneDeletedMeanwhile_ShouldBeStale()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var zoneId = await SeedZoneAsync(database, tenant.Id, "Centro", "72000");
        await using var racing = Racing(database, tenant.Id, async other => other.DeliveryZones.Remove(await LoadAsync(other, zoneId)));

        var result = await UpdateAsync(racing, tenant.Id, zoneId, 1, "Centro", "72000", "72010");

        Assert.Equal(DeliveryZoneErrors.Stale, result.Error);
        await using var context = database.CreateContext(tenant.Id);
        Assert.Empty(await context.DeliveryZones.ToListAsync());
        Assert.Empty(await context.DeliveryZonePostalCodes.ToListAsync());
    }

    [SqlServerFact]
    public async Task Update_TakingAPostalCodeAnotherZoneTookMeanwhile_ShouldNameThatZone()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var centroId = await SeedZoneAsync(database, tenant.Id, "Centro", "72000");
        var norteId = await SeedZoneAsync(database, tenant.Id, "Norte", "72100");
        await using var racing = Racing(database, tenant.Id, async other =>
            (await LoadAsync(other, norteId)).Update(Fields("Norte", "72100", "72010")));

        var result = await UpdateAsync(racing, tenant.Id, centroId, 1, "Centro", "72000", "72010");

        Assert.Equal(DeliveryZoneErrors.PostalCodeInAnotherZone("72010", "Norte"), result.Error);
        await using var context = database.CreateContext(tenant.Id);
        var centro = await LoadAsync(context, centroId);
        Assert.Equal((1, "72000"), (centro.Revision, Assert.Single(centro.PostalCodes).PostalCode));
    }

    [SqlServerFact]
    public async Task Create_WithANameOrPostalCodeAnotherZoneTookMeanwhile_ShouldNameIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        await using (var racing = Racing(database, tenant.Id, other =>
        {
            other.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("Norte", "72010")));
            return Task.CompletedTask;
        }))
        {
            var result = await CreateAsync(racing, tenant.Id, "Centro", "72000", "72010");
            Assert.Equal(DeliveryZoneErrors.PostalCodeInAnotherZone("72010", "Norte"), result.Error);
        }

        await using (var racing = Racing(database, tenant.Id, other =>
        {
            other.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("Centro", "72020")));
            return Task.CompletedTask;
        }))
        {
            var result = await CreateAsync(racing, tenant.Id, "Centro", "72000");
            Assert.Equal(DeliveryZoneErrors.NameTaken("Centro"), result.Error);
        }

        await using var context = database.CreateContext(tenant.Id);
        Assert.Equal(["Centro", "Norte"], await context.DeliveryZones.OrderBy(z => z.Name).Select(z => z.Name).ToListAsync());
        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task Delete_OfAZoneSavedMeanwhile_ShouldBeStale()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var zoneId = await SeedZoneAsync(database, tenant.Id, "Centro", "72000");
        await using var racing = Racing(database, tenant.Id, async other =>
            (await LoadAsync(other, zoneId)).Update(Fields("Centro", "72000", "72010")));

        var result = await new DeleteDeliveryZoneCommandHandler(racing, Tenant(tenant.Id), new Owner())
            .Handle(new DeleteDeliveryZoneCommand(zoneId, 1), CancellationToken.None);

        Assert.Equal(DeliveryZoneErrors.Stale, result.Error);
        await using var context = database.CreateContext(tenant.Id);
        Assert.Equal(2, (await LoadAsync(context, zoneId)).PostalCodes.Count);
        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task Update_ThatFailsForAnotherReason_ShouldNotPassForSomeoneElsesSave()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var zoneId = await SeedZoneAsync(database, tenant.Id, "Centro", "72000");
        // A check that no longer agrees with the domain (made stricter by hand here): an error to log, not a conflict.
        await using (var schema = database.CreateContext())
        {
            await schema.Database.ExecuteSqlRawAsync(
                "ALTER TABLE [DeliveryZones] DROP CONSTRAINT [CK_DeliveryZones_Fee]; "
                + "ALTER TABLE [DeliveryZones] ADD CONSTRAINT [CK_DeliveryZones_Fee] CHECK ([Fee] <= 35);");
        }

        await using var context = database.CreateContext(tenant.Id);
        await Assert.ThrowsAsync<DbUpdateException>(() => UpdateAsync(context, tenant.Id, zoneId, 1, "Centro", "72000", "72010"));
        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(1, (await LoadAsync(check, zoneId)).Revision);
    }

    [SqlServerFact]
    public async Task SavedZone_ShouldKeepTheRowsOfThePostalCodesItStillHas_AndDeletingItTakesThemAll()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        Guid zoneId;
        Guid keptId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var zone = new DeliveryZone(tenant.Id, Fields("Centro", "72000", "72005"));
            context.DeliveryZones.Add(zone);
            await context.SaveChangesAsync();
            zoneId = zone.Id;
            keptId = zone.PostalCodes.Single(code => code.PostalCode == "72000").Id;
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var zone = await context.DeliveryZones.Include(z => z.PostalCodes).SingleAsync(z => z.Id == zoneId);
            zone.Update(Fields("Centro", "72000", "72010"));
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var codes = await check.DeliveryZonePostalCodes.OrderBy(code => code.PostalCode).ToListAsync();
            Assert.Equal(["72000", "72010"], codes.Select(code => code.PostalCode));
            Assert.Equal(keptId, codes[0].Id);

            check.DeliveryZones.Remove(await check.DeliveryZones.Include(z => z.PostalCodes).SingleAsync());
            await check.SaveChangesAsync();
        }

        await using var after = database.CreateContext(tenant.Id);
        Assert.Empty(await after.DeliveryZonePostalCodes.ToListAsync());
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_ARowTheDomainNeverWrites()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        (decimal Fee, decimal Minimum, decimal? FreeFrom, int Revision, string PostalCode)[] rows =
        [
            (-1m, 0m, null, 1, "72000"),
            (10_000.01m, 0m, null, 1, "72000"),
            (35m, -0.01m, null, 1, "72000"),
            (35m, 100_000.01m, null, 1, "72000"),
            (35m, 0m, 0m, 1, "72000"),
            (35m, 0m, 100_000.01m, 1, "72000"),
            (0m, 0m, 400m, 1, "72000"),
            (35m, 0m, null, 0, "72000"),
            (35m, 0m, null, 1, "7200"),
            (35m, 0m, null, 1, "7200a"),
            (35m, 0m, null, 1, "7200 "),
        ];
        await using var context = database.CreateContext();

        foreach (var row in rows)
        {
            // A name of its own: a row whose postal code is refused leaves its zone, and names are one per zone.
            var zoneId = Guid.NewGuid();
            var name = $"Zona {zoneId:N}";
            var error = await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [DeliveryZones] ([Id], [TenantId], [Name], [Fee], [MinimumOrder], [FreeDeliveryFrom], [IsActive], [Revision], [CreatedAt])
                    VALUES ({zoneId}, {tenant.Id}, {name}, {row.Fee}, {row.Minimum}, {row.FreeFrom}, 1, {row.Revision}, SYSDATETIMEOFFSET())
                    """);
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [DeliveryZonePostalCodes] ([Id], [TenantId], [DeliveryZoneId], [PostalCode])
                    VALUES ({Guid.NewGuid()}, {tenant.Id}, {zoneId}, {row.PostalCode})
                    """);
            });
            Assert.True(error.Number == 547, $"{row}: error {error.Number}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileZonesAreSaved_AndThenDropOnlyTheTwoTables()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenant, _) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("Centro", "72000")));
            await context.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeDeliveryZones));
        Assert.Equal(50012, error.Number);
        Assert.Contains("delivery zones saved", error.Message);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            var zone = await context.DeliveryZones.Include(z => z.PostalCodes).SingleAsync();
            Assert.Single(zone.PostalCodes);

            // Deleted on purpose, the rollback goes on.
            context.DeliveryZones.Remove(zone);
            await context.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeDeliveryZones);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeDeliveryZones, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN (N'DeliveryZones', N'DeliveryZonePostalCodes')
                """).SingleAsync());
            Assert.Equal(2, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Tenants]").SingleAsync());
        }
        // Rolling back again does nothing, and forward again the tables are back, empty.
        await MigrateAsync(database, BeforeDeliveryZones);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Empty(await forward.DeliveryZones.ToListAsync());
    }

    private static DeliveryZoneFields Fields(string name, params string[] postalCodes)
        => new(name, 35m, 0m, null, postalCodes);

    private static async Task<Guid> SeedZoneAsync(SqlServerTestDatabase database, Guid tenantId, string name, params string[] postalCodes)
    {
        await using var context = database.CreateContext(tenantId);
        var zone = new DeliveryZone(tenantId, Fields(name, postalCodes));
        context.DeliveryZones.Add(zone);
        await context.SaveChangesAsync();
        return zone.Id;
    }

    private static Task<DeliveryZone> LoadAsync(ApplicationDbContext context, Guid zoneId)
        => context.DeliveryZones.Include(z => z.PostalCodes).SingleAsync(z => z.Id == zoneId);

    /// <summary>A context whose first save is preceded by <paramref name="change"/>, saved by someone else.</summary>
    private static ApplicationDbContext Racing(SqlServerTestDatabase database, Guid tenantId, Func<ApplicationDbContext, Task> change)
        => database.CreateProductionLikeContext(Tenant(tenantId), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenantId);
                await change(other);
                await other.SaveChangesAsync();
            }));

    private static Task<FoodParkDigital.Application.Common.Models.Result<DeliveryZoneDto>> CreateAsync(
        ApplicationDbContext context, Guid tenantId, string name, params string[] postalCodes)
        => new CreateDeliveryZoneCommandHandler(context, Tenant(tenantId), new Owner())
            .Handle(new CreateDeliveryZoneCommand(name, 35m, 0m, null, postalCodes), CancellationToken.None);

    private static Task<FoodParkDigital.Application.Common.Models.Result<DeliveryZoneDto>> UpdateAsync(
        ApplicationDbContext context, Guid tenantId, Guid zoneId, int revision, string name, params string[] postalCodes)
        => new UpdateDeliveryZoneCommandHandler(context, Tenant(tenantId), new Owner())
            .Handle(new UpdateDeliveryZoneCommand(zoneId, revision, name, 40m, 0m, null, postalCodes, true), CancellationToken.None);

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    private sealed class Owner : ICurrentUserService
    {
        public string? UserId => "4f6c2a0e-8d8b-4a8f-9a55-0c4b1f3f8a10";
        public string? UserName => "duena@demo.com";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private static async Task<(Tenant Tenant, Tenant Other)> SeedAsync(SqlServerTestDatabase database)
    {
        var tenant = NewTenant("Cocina de Lupita");
        var other = NewTenant("Fonda vecina");
        // Without what Fase 22E added: some tests seed a database migrated to an earlier migration.
        await using var seed = BeforeHomeVisits.CreateContext(database);
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, TenantCapability.Food), new TenantSettings(other.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    private static Tenant NewTenant(string name)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant(name, slug, BusinessType.Food, $"{slug}@demo.com");
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

    private sealed record IndexRow(string Name, bool IsUnique);
}
