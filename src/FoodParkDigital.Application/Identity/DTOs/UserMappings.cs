using FoodParkDigital.Domain.Identity.Entities;

namespace FoodParkDigital.Application.Identity.DTOs;

public static class UserMappings
{
    /// <summary>The account of the person; the role and permissions in the current business are filled by the caller.</summary>
    public static UserProfileDto ToProfileDto(this ApplicationUser user) => new()
    {
        Id = user.Id,
        Email = user.Email!,
        FirstName = user.FirstName,
        LastName = user.LastName,
        FullName = user.FullName,
        PhoneNumber = user.PhoneNumber,
        PhoneNumberConfirmed = user.PhoneNumberConfirmed,
        EmailConfirmed = user.EmailConfirmed,
        Role = user.Role.ToString(),
        TenantId = user.DefaultTenantId,
        IsActive = user.IsActive,
        AvatarUrl = string.IsNullOrEmpty(user.AvatarUrl) ? null : $"{user.AvatarUrl}_md.webp",
        AvatarImages = AvatarImageUrlsDto.FromBaseUrl(user.AvatarUrl),
        TwoFactorEnabled = user.TwoFactorEnabled,
        CreatedAt = user.CreatedAt
    };
}
