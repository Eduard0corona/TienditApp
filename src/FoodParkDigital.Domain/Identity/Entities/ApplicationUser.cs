using FoodParkDigital.Domain.Identity.Enums;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Domain.Identity.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public Guid? DefaultTenantId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public UserRole Role { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>When the last SMS code of this account was sent: one per minute, whatever the screen asking.</summary>
    public DateTimeOffset? SmsCodeSentAt { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}
