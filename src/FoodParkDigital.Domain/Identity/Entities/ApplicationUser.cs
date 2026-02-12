using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Identity.Enums;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Domain.Identity.Entities;

public class ApplicationUser : IdentityUser<Guid>, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string FullName => $"{FirstName} {LastName}";
}
