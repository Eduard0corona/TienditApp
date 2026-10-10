namespace FoodParkDigital.Application.Identity.DTOs;

public class MyTenantDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
    /// <summary>The business's logo in small (128 px) for the business switcher; null while it has none.</summary>
    public string? LogoUrl { get; set; }
}
