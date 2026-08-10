namespace FoodParkDigital.Application.Common.Interfaces;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    string? Role { get; }
    string? TenantRole { get; }
    IReadOnlyCollection<string> Permissions { get; }
    Guid? TenantId { get; }
}
