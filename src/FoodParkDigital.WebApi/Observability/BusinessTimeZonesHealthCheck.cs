using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodParkDigital.WebApi.Observability;

/// <summary>
/// Degraded (still serving traffic) when a time zone a business can pick resolves to UTC on this host. Without the
/// host's time-zone data <see cref="TenantDateTimeResolver"/> falls back to UTC silently, and the hours, the special
/// days and the dates of those businesses would run five to eight hours off. CI tests the zones on its own host only;
/// this check tests them where the app runs, and the deployment's smoke test requires it Healthy.
/// </summary>
public sealed class BusinessTimeZonesHealthCheck : IHealthCheck
{
    private readonly Func<string?, TimeZoneInfo> _resolve;

    public BusinessTimeZonesHealthCheck()
        : this(TenantDateTimeResolver.Resolve)
    {
    }

    /// <summary>With another resolver, for the tests.</summary>
    internal BusinessTimeZonesHealthCheck(Func<string?, TimeZoneInfo> resolve)
    {
        _resolve = resolve;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // No zone of Mexico is at UTC: one that is, is the fallback.
        var atUtc = BusinessTimeZones.Supported.Where(zone => _resolve(zone).BaseUtcOffset == TimeSpan.Zero).ToList();
        var data = new Dictionary<string, object> { ["zonesAtUtc"] = atUtc.Count };

        // The names go to the log only: the endpoint answers with the status.
        return Task.FromResult(atUtc.Count == 0
            ? HealthCheckResult.Healthy(data: data)
            : HealthCheckResult.Degraded(
                $"{atUtc.Count} time zone(s) of businesses resolve to UTC, without data on this host: "
                + $"{string.Join(", ", atUtc)}.",
                data: data));
    }
}
