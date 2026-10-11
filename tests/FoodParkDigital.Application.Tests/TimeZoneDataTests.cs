using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The host that runs the tests has the data of every time zone a business can pick, with its summer time: without it
/// <see cref="TenantDateTimeResolver"/> falls back to UTC silently, and the hours, the special days and the clock
/// changes of Tijuana and Matamoros would be tested in UTC. It guards the CI host only; where the app runs, the
/// business-time-zones entry of /health/ready guards it, and the deployment's smoke test requires it Healthy.
/// </summary>
public class TimeZoneDataTests
{
    public static TheoryData<string> SupportedZones()
    {
        var zones = new TheoryData<string>();
        foreach (var zone in BusinessTimeZones.Supported)
            zones.Add(zone);
        return zones;
    }

    [Theory]
    [MemberData(nameof(SupportedZones))]
    public void EverySupportedZone_ShouldResolve_NotFallBackToUtc(string zone)
    {
        var resolved = TenantDateTimeResolver.Resolve(zone);

        Assert.NotSame(TimeZoneInfo.Utc, resolved);
        Assert.NotEqual(TimeSpan.Zero, resolved.BaseUtcOffset);
    }

    [Theory]
    // Centro, Sureste and Pacífico have no summer time since 2022.
    [InlineData("America/Mexico_City", "2026-07-15T18:00:00Z", -6)]
    [InlineData("America/Mexico_City", "2026-12-15T18:00:00Z", -6)]
    [InlineData("America/Cancun", "2026-07-15T18:00:00Z", -5)]
    [InlineData("America/Cancun", "2026-12-15T18:00:00Z", -5)]
    [InlineData("America/Mazatlan", "2026-07-15T18:00:00Z", -7)]
    [InlineData("America/Mazatlan", "2026-12-15T18:00:00Z", -7)]
    // Baja California and the northeast border keep the summer time of the United States.
    [InlineData("America/Tijuana", "2026-07-15T18:00:00Z", -7)]
    [InlineData("America/Tijuana", "2026-12-15T18:00:00Z", -8)]
    [InlineData("America/Matamoros", "2026-07-15T18:00:00Z", -5)]
    [InlineData("America/Matamoros", "2026-12-15T18:00:00Z", -6)]
    public void EachZone_ShouldHaveItsOffset_InSummerAndInWinter(string zone, string instant, int hours)
    {
        var offset = TenantDateTimeResolver.Resolve(zone).GetUtcOffset(DateTimeOffset.Parse(instant));

        Assert.Equal(TimeSpan.FromHours(hours), offset);
    }
}
