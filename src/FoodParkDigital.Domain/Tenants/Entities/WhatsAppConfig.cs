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
        string? displayPhoneNumber,
        string? verifyToken = null)
    {
        IsEnabled = isEnabled;
        PhoneNumberId = NormalizePhoneNumberId(phoneNumberId);
        WabaId = wabaId;
        DisplayPhoneNumber = displayPhoneNumber;

        // Only update token if a new value is provided
        if (!string.IsNullOrEmpty(accessToken))
            AccessToken = accessToken;

        if (!string.IsNullOrEmpty(verifyToken))
            VerifyToken = verifyToken;
    }

    /// <summary>
    /// The phone number ID routes inbound webhooks to exactly one tenant, so a blank
    /// value means "not configured" and is stored as null instead of an empty string.
    /// </summary>
    public static string? NormalizePhoneNumberId(string? phoneNumberId)
        => string.IsNullOrWhiteSpace(phoneNumberId) ? null : phoneNumberId.Trim();
}
