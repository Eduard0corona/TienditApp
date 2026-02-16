namespace FoodParkDigital.Application.Identity.DTOs;

public class MyTenantDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
}
