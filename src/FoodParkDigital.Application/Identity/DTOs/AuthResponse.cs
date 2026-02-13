namespace FoodParkDigital.Application.Identity.DTOs;

public record AuthResponse
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool RequiresTwoFactor { get; init; }
    public string? UserId { get; init; }
    public UserDto? User { get; init; }
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Role { get; set; } = null!;
    public Guid TenantId { get; set; }
    public bool IsActive { get; set; }
}
