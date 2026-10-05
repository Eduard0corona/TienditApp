using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>Production does not migrate on start: a version that went out before its apply.sql shows unhealthy.</summary>
public class DatabaseSchemaSqlServerTests
{
    [SqlServerFact]
    public async Task Schema_ShouldBeHealthy_OnlyWhenEveryMigrationOfThisVersionIsApplied()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var history = context.GetService<IHistoryRepository>();
        var migrations = context.Database.GetMigrations().ToList();
        await context.Database.ExecuteSqlRawAsync(history.GetCreateScript());
        foreach (var migration in migrations.SkipLast(1))
            await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(migration, "10.0.0")));

        // The newest migration of this version was not applied.
        var behind = await new DatabaseSchemaHealthCheck(context).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, behind.Status);
        Assert.Equal(1, behind.Data["pendingMigrations"]);
        Assert.Contains(migrations[^1], behind.Description);

        await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(migrations[^1], "10.0.0")));

        var current = await new DatabaseSchemaHealthCheck(context).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, current.Status);
    }
}
