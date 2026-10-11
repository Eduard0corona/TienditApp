using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.WebApi.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Readiness says whether the time zones a business can pick resolve on the host: a zone that falls back to UTC leaves
/// it Degraded (still serving), and the smoke test, which requires the entry Healthy, fails the deployment.
/// </summary>
public class BusinessTimeZonesHealthCheckTests
{
    [Fact]
    public async Task EveryZoneResolving_ShouldBeHealthy()
    {
        var result = await Check(new BusinessTimeZonesHealthCheck());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0, result.Data["zonesAtUtc"]);
    }

    [Fact]
    public async Task AZoneThatFallsBackToUtc_ShouldBeDegraded_AndNameIt()
    {
        // A host without Tijuana's data: TenantDateTimeResolver answers UTC for it.
        var withoutTijuana = new BusinessTimeZonesHealthCheck(zone =>
            zone == "America/Tijuana" ? TimeZoneInfo.Utc : TenantDateTimeResolver.Resolve(zone));

        var result = await Check(withoutTijuana);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(1, result.Data["zonesAtUtc"]);
        Assert.Equal(
            "1 time zone(s) of businesses resolve to UTC, without data on this host: America/Tijuana.", result.Description);
    }

    [Fact]
    public async Task AHostWithoutZoneData_ShouldBeDegraded_ForEveryZone()
    {
        var result = await Check(new BusinessTimeZonesHealthCheck(_ => TimeZoneInfo.Utc));

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(5, result.Data["zonesAtUtc"]);
        Assert.EndsWith(
            ": America/Mexico_City, America/Cancun, America/Mazatlan, America/Tijuana, America/Matamoros.", result.Description);
    }

    private static Task<HealthCheckResult> Check(BusinessTimeZonesHealthCheck check)
        => check.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("business-time-zones", check, failureStatus: null, tags: null)
        });
}
