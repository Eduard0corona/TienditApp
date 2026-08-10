namespace FoodParkDigital.Application.Identity.DTOs;

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool EmailConfirmed { get; set; }
    public string Role { get; set; } = null!;
    public string? TenantRole { get; set; }
    public List<string> Permissions { get; set; } = [];
    public Guid? TenantId { get; set; }
    public bool IsActive { get; set; }
    public string? AvatarUrl { get; set; }
    public AvatarImageUrlsDto? AvatarImages { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
