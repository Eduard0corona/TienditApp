namespace FoodParkDigital.Application.Common.Interfaces;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    string? Role { get; }
    Guid? TenantId { get; }
}
