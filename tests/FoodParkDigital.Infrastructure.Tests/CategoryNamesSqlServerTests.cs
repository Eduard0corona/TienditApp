using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddUniqueCategoryNamePerTenant (Fase 31B): one name per category in a business. The unique index compares with the
/// collation of the database (without capitals or spaces at the end; accents count) and lets another business have the
/// name; the use cases answer a name taken at the same moment as taken, not with an error; the migration stops before
/// anything changes while two categories of a business share a name by that collation; and rolling back drops only the
/// index.
/// </summary>
public class CategoryNamesSqlServerTests
{
    // The migration right before AddUniqueCategoryNamePerTenant, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261108150147_AddOrderCancellationEmailsAndAnswerReminder";
    private const string Migration = "20261110022936_AddUniqueCategoryNamePerTenant";
    private const string IndexName = "IX_Categories_TenantId_Name";

    [SqlServerFact]
    public async Task Index_ShouldRefuseTheNameInTheBusiness_WithoutCapitalsOrSpacesAtTheEnd_ButNotInAnother()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await AddAsync(database, tenantId, "Bebidas");
        await AddAsync(database, otherId, "Bebidas");

        foreach (var name in new[] { "Bebidas", "BEBIDAS", "bebidas  " })
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, tenantId, name));
            Assert.Equal(2601, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        // Accents count, as in the use cases.
        await AddAsync(database, tenantId, "Bébidas");
        await using var context = database.CreateContext(tenantId);
        Assert.Equal(["Bebidas", "Bébidas"], await context.Categories.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync());
        Assert.Equal("TenantId,Name|1", await IndexAsync(context));
    }

    [SqlServerFact]
    public async Task UseCases_ShouldRefuseANameTheBusinessHas_AlsoOneTakenAtTheSameMoment()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        await AddAsync(database, tenantId, "Bebidas");
        var dessertsId = await AddAsync(database, tenantId, "Postres");

        await using (var context = database.CreateContext(tenantId))
        {
            var repeated = await CreateAsync(context, tenantId, "  bebidas\t");
            Assert.Equal(CategoryNames.Taken("Bebidas", isActive: true), repeated.Error);
        }

        // Someone else saves the name in other capitals right before this save: the index refuses it, and the person is
        // told which category has it.
        await using (var racing = Racing(database, tenantId, other => other.Categories.Add(new Category(tenantId, "comidas", null))))
        {
            var created = await CreateAsync(racing, tenantId, "Comidas");
            Assert.Equal(CategoryNames.Taken("comidas", isActive: true), created.Error);
        }

        await using (var racing = Racing(database, tenantId, other => other.Categories.Add(new Category(tenantId, "Dulces", null))))
        {
            var edited = await new UpdateCategoryCommandHandler(racing)
                .Handle(new UpdateCategoryCommand { Id = dessertsId, Name = "DULCES" }, CancellationToken.None);
            Assert.Equal(CategoryNames.Taken("Dulces", isActive: true), edited.Error);
        }

        await using var check = database.CreateContext(tenantId);
        Assert.Equal(
            ["Bebidas", "comidas", "Dulces", "Postres"],
            await check.Categories.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync());
    }

    [SqlServerFact]
    public async Task Migration_ShouldStopBeforeAnythingChanges_WhileABusinessRepeatsAName_AndApplyOnceRenamed()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var tenantId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        // Repeated by the collation (capitals and a space at the end): it stops the migration. Repeated only by a space
        // at the start, and the same name in another business: they do not.
        var repeatedId = await AddAsync(database, tenantId, "bebidas ");
        await AddAsync(database, tenantId, "Bebidas");
        await AddAsync(database, tenantId, "Postres");
        await AddAsync(database, tenantId, " postres");
        await AddAsync(database, otherId, "Bebidas");

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, Migration));

        Assert.Equal(50057, error.Number);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await IndexAsync(context));
            Assert.Equal(5, await context.Categories.IgnoreQueryFilters().CountAsync());
        }

        // Renamed in the app, it applies; the one repeated only by its spaces stays.
        await using (var context = database.CreateContext(tenantId))
        {
            var updated = await new UpdateCategoryCommandHandler(context)
                .Handle(new UpdateCategoryCommand { Id = repeatedId, Name = "Bebidas de antes" }, CancellationToken.None);
            Assert.True(updated.IsSuccess, updated.Error);
        }

        await MigrateAsync(database, Migration);

        await using var migrated = database.CreateContext(tenantId);
        Assert.Equal(Migration, await LatestMigrationAsync(migrated));
        Assert.Equal("TenantId,Name|1", await IndexAsync(migrated));
        Assert.Equal(
            [" postres", "Bebidas", "Bebidas de antes", "Postres"],
            (await migrated.Categories.Select(c => c.Name).ToListAsync()).Order(StringComparer.Ordinal));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheIndex_AndForwardAgainTheNamesStayUnique()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenantId = Guid.NewGuid();
        await AddAsync(database, tenantId, "Bebidas");
        await AddAsync(database, tenantId, "Postres");

        await MigrateAsync(database, BeforeMigration);
        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await IndexAsync(context));
            Assert.Equal(["Bebidas", "Postres"], await context.Categories.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync());
        }

        // Rolling back again does nothing; forward again, the index is back.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenantId);
        Assert.Equal("TenantId,Name|1", await IndexAsync(forward));
        await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, tenantId, "postres"));
    }

    private static async Task<Guid> AddAsync(SqlServerTestDatabase database, Guid tenantId, string name)
    {
        await using var context = database.CreateContext(tenantId);
        var category = new Category(tenantId, name, null);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>A context whose first save is preceded by <paramref name="change"/>, saved by someone else.</summary>
    private static ApplicationDbContext Racing(SqlServerTestDatabase database, Guid tenantId, Action<ApplicationDbContext> change)
        => database.CreateProductionLikeContext(Tenant(tenantId), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenantId);
                change(other);
                await other.SaveChangesAsync();
            }));

    private static Task<Result<CategoryDto>> CreateAsync(ApplicationDbContext context, Guid tenantId, string name)
        => new CreateCategoryCommandHandler(context, Tenant(tenantId))
            .Handle(new CreateCategoryCommand { Name = name }, CancellationToken.None);

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    /// <summary>The columns of the index in order and whether it is unique, or null without it.</summary>
    private static async Task<string?> IndexAsync(ApplicationDbContext context)
        => (await context.Database.SqlQuery<string>($"""
            SELECT CONCAT(STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.[key_ordinal]), N'|', MAX(CAST(i.[is_unique] AS int))) AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
            JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
            WHERE i.[object_id] = OBJECT_ID(N'dbo.Categories') AND i.[name] = {IndexName}
            GROUP BY i.[name]
            """).ToListAsync()).SingleOrDefault();

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
