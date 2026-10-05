using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodParkDigital.Infrastructure.Persistence;

/// <summary>
/// Unhealthy while the database lacks a migration this version expects. Production does not migrate on start, so a
/// version published before its <c>apply.sql</c> ran shows here, before its screens fail on a missing column.
/// </summary>
public sealed class DatabaseSchemaHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _context;

    public DatabaseSchemaHealthCheck(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var data = new Dictionary<string, object> { ["pendingMigrations"] = pending.Count };

        // The names go to the log only: the endpoint answers with the status.
        return pending.Count == 0
            ? HealthCheckResult.Healthy(data: data)
            : HealthCheckResult.Unhealthy(
                $"{pending.Count} migration(s) of this version are not in the database, starting with {pending[0]}.",
                data: data);
    }
}
