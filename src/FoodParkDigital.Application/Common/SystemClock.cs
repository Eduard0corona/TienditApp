using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Application.Common;

/// <summary>Fallback clock for handlers built without dependency injection (tests, optional dependencies).</summary>
internal sealed class SystemClock : IDateTimeProvider
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
