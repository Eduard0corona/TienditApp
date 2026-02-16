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
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public string Role { get; set; } = null!;
    public Guid? TenantId { get; set; }
    public bool IsActive { get; set; }
    public string? AvatarUrl { get; set; }
    public AvatarImageUrlsDto? AvatarImages { get; set; }
}

public class AvatarImageUrlsDto
{
    public string Original { get; set; } = null!;
    public string Large { get; set; } = null!;
    public string Medium { get; set; } = null!;
    public string Small { get; set; } = null!;
    public string Thumbnail { get; set; } = null!;

    public static AvatarImageUrlsDto? FromBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return null;

        return new AvatarImageUrlsDto
        {
            Original = $"{baseUrl}_original.webp",
            Large = $"{baseUrl}_lg.webp",
            Medium = $"{baseUrl}_md.webp",
            Small = $"{baseUrl}_sm.webp",
            Thumbnail = $"{baseUrl}_xs.webp"
        };
    }
}
