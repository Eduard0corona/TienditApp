using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Tenants.Entities;

public class WhatsAppConfig : AuditableEntity
{
    private WhatsAppConfig() { }

    public WhatsAppConfig(Guid tenantId)
    {
        TenantId = tenantId;
        IsEnabled = false;
    }

    public Guid TenantId { get; private set; }
    public bool IsEnabled { get; private set; }
    public string? AccessToken { get; private set; }
    public string? PhoneNumberId { get; private set; }
    public string? WabaId { get; private set; }
    public string? DisplayPhoneNumber { get; private set; }
    public string? VerifyToken { get; private set; }

    public Tenant Tenant { get; private set; } = null!;

    public void Update(
        bool isEnabled,
        string? accessToken,
        string? phoneNumberId,
        string? wabaId,
        string? displayPhoneNumber)
    {
        IsEnabled = isEnabled;
        PhoneNumberId = phoneNumberId;
        WabaId = wabaId;
        DisplayPhoneNumber = displayPhoneNumber;

        // Only update token if a new value is provided
        if (!string.IsNullOrEmpty(accessToken))
            AccessToken = accessToken;
    }
}
