using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>Scoped: set by the background workers before running work on behalf of a user.</summary>
public sealed class BackgroundExecutionContext : IBackgroundExecutionContext
{
    public string? UserId { get; private set; }
    public string? UserName { get; private set; }

    public void SetActor(string? userId, string? userName)
    {
        UserId = userId;
        UserName = userName;
    }
}
