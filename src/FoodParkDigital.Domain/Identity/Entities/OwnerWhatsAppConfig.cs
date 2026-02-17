using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Identity.Entities;

public class OwnerWhatsAppConfig : AuditableEntity
{
    private OwnerWhatsAppConfig() { }

    public OwnerWhatsAppConfig(Guid userId)
    {
        UserId = userId;
        IsEnabled = false;
        UseForOtp = true;
        OtpTemplateName = "otp_verification";
        OtpTemplateLanguageCode = "es_MX";
    }

    public Guid UserId { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool UseForOtp { get; private set; }
    public string? AccessToken { get; private set; }
    public string? PhoneNumberId { get; private set; }
    public string? WabaId { get; private set; }
    public string? DisplayPhoneNumber { get; private set; }
    public string OtpTemplateName { get; private set; } = null!;
    public string OtpTemplateLanguageCode { get; private set; } = null!;

    public ApplicationUser User { get; private set; } = null!;

    public void Update(
        bool isEnabled,
        bool useForOtp,
        string? accessToken,
        string? phoneNumberId,
        string? wabaId,
        string? displayPhoneNumber,
        string? otpTemplateName,
        string? otpTemplateLanguageCode)
    {
        IsEnabled = isEnabled;
        UseForOtp = useForOtp;
        PhoneNumberId = phoneNumberId;
        WabaId = wabaId;
        DisplayPhoneNumber = displayPhoneNumber;

        if (!string.IsNullOrWhiteSpace(accessToken))
            AccessToken = accessToken;

        if (!string.IsNullOrWhiteSpace(otpTemplateName))
            OtpTemplateName = otpTemplateName.Trim();

        if (!string.IsNullOrWhiteSpace(otpTemplateLanguageCode))
            OtpTemplateLanguageCode = otpTemplateLanguageCode.Trim();
    }
}
