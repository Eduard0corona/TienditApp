using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Tenants.Entities;

public class TenantSettings : BaseEntity
{
    private TenantSettings() { }

    public TenantSettings(Guid tenantId)
    {
        TenantId = tenantId;
        Currency = "USD";
        TimeZone = "America/Mexico_City";
        AutoAcceptOrders = false;
        MaxConcurrentOrders = 50;
    }

    public Guid TenantId { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string TimeZone { get; private set; } = "America/Mexico_City";
    public bool AutoAcceptOrders { get; private set; }
    public int MaxConcurrentOrders { get; private set; }
    public string? SettingsJson { get; private set; }

    public Tenant Tenant { get; private set; } = null!;

    public void Update(string currency, string timeZone, bool autoAcceptOrders, int maxConcurrentOrders)
    {
        Currency = currency;
        TimeZone = timeZone;
        AutoAcceptOrders = autoAcceptOrders;
        MaxConcurrentOrders = maxConcurrentOrders;
    }
}
