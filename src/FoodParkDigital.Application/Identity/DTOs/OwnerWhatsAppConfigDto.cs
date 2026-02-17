namespace FoodParkDigital.Application.Identity.DTOs;

public class OwnerWhatsAppConfigDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public bool IsEnabled { get; set; }
    public bool UseForOtp { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? WabaId { get; set; }
    public string? DisplayPhoneNumber { get; set; }
    public string OtpTemplateName { get; set; } = "otp_verification";
    public string OtpTemplateLanguageCode { get; set; } = "es_MX";
    public bool HasAccessToken { get; set; }
}
