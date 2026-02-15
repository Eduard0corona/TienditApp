namespace FoodParkDigital.Application.Tenants.DTOs;

public class WhatsAppConfigDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public bool IsEnabled { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? WabaId { get; set; }
    public string? DisplayPhoneNumber { get; set; }
    public bool HasAccessToken { get; set; }
}
