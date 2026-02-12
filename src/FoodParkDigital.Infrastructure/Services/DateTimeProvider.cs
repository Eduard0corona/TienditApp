using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
