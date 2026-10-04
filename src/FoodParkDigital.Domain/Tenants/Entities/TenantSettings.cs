using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants.Entities;

public class TenantSettings : BaseEntity
{
    private TenantSettings() { }

    /// <param name="pricesIncludeTax">
    /// IVA policy: new businesses start with prices that include it (what a Mexican customer reads on a
    /// menu); omitted, the settings keep IVA added on top, as every business sold before tax policies.
    /// </param>
    public TenantSettings(
        Guid tenantId,
        TenantCapability capabilities = TenantCapability.Inventory,
        bool pricesIncludeTax = false)
    {
        TenantId = tenantId;
        Currency = "MXN";
        TimeZone = "America/Mexico_City";
        AutoAcceptOrders = false;
        MaxConcurrentOrders = 50;
        Capabilities = capabilities;
        PricesIncludeTax = pricesIncludeTax;
        DefaultTaxRate = VatRate.GeneralPercent;
        AutoCloseCounterSales = true;
    }

    public Guid TenantId { get; private set; }
    /// <summary>Mexican pesos: the app formats every amount as MXN.</summary>
    public string Currency { get; private set; } = "MXN";
    public string TimeZone { get; private set; } = "America/Mexico_City";
    public bool AutoAcceptOrders { get; private set; }
    public int MaxConcurrentOrders { get; private set; }
    public TenantCapability Capabilities { get; private set; }
    public string? SettingsJson { get; private set; }

    /// <summary>True when catalog prices already include IVA; false when IVA is added on top.</summary>
    public bool PricesIncludeTax { get; private set; }
    /// <summary>IVA rate in percent for products without their own: 16 in general, 8 in the border region.</summary>
    public decimal DefaultTaxRate { get; private set; } = VatRate.GeneralPercent;
    /// <summary>Counter sales close by themselves once fully paid (food with kitchen, once ready).</summary>
    public bool AutoCloseCounterSales { get; private set; } = true;

    /// <summary>
    /// When true a cash payment is refused unless the cashier has an open drawer, so no cash goes uncounted.
    /// Off by default: card and transfer payments never need a drawer.
    /// </summary>
    public bool RequireOpenCashSession { get; private set; }
    /// <summary>When the owner finished or skipped the guided setup.</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; private set; }

    /// <summary>Which orders send the customer WhatsApp messages: by default not counter sales (the customer is there).</summary>
    public OrderNotificationPolicy OrderNotificationPolicy { get; private set; } = OrderNotificationPolicy.RemoteAndServices;

    public const int MinReminderHoursBefore = 1;
    public const int MaxReminderHoursBefore = 72;
    public const int DefaultReminderHoursBefore = 24;

    /// <summary>WhatsApp reminders before each appointment; off until the business turns them on.</summary>
    public bool AppointmentRemindersEnabled { get; private set; }
    public int AppointmentReminderHoursBefore { get; private set; } = DefaultReminderHoursBefore;
    /// <summary>The reminder template; null uses the default text.</summary>
    public Guid? AppointmentReminderTemplateId { get; private set; }

    public Tenant Tenant { get; private set; } = null!;

    public void Update(string currency, string timeZone, bool autoAcceptOrders, int maxConcurrentOrders)
    {
        Currency = currency;
        TimeZone = timeZone;
        AutoAcceptOrders = autoAcceptOrders;
        MaxConcurrentOrders = maxConcurrentOrders;
    }

    public void UpdateCapabilities(TenantCapability capabilities)
    {
        Capabilities = capabilities;
    }

    /// <summary>
    /// Changes how future sales are priced and closed. Orders keep the policy they were sold with.
    /// </summary>
    public void UpdateSalesPolicy(bool pricesIncludeTax, decimal defaultTaxRate, bool autoCloseCounterSales)
    {
        DefaultTaxRate = VatRate.Of(defaultTaxRate).Percent;
        PricesIncludeTax = pricesIncludeTax;
        AutoCloseCounterSales = autoCloseCounterSales;
    }

    public void SetRequireOpenCashSession(bool required) => RequireOpenCashSession = required;

    public void SetOrderNotificationPolicy(OrderNotificationPolicy policy)
    {
        if (!Enum.IsDefined(policy))
            throw new DomainException("La política de avisos no es válida.");
        OrderNotificationPolicy = policy;
    }

    public void UpdateAppointmentReminders(bool enabled, int hoursBefore, Guid? templateId)
    {
        if (hoursBefore is < MinReminderHoursBefore or > MaxReminderHoursBefore)
            throw new DomainException(
                $"El recordatorio se envía de {MinReminderHoursBefore} a {MaxReminderHoursBefore} horas antes de la cita.");

        AppointmentRemindersEnabled = enabled;
        AppointmentReminderHoursBefore = hoursBefore;
        AppointmentReminderTemplateId = templateId;
    }

    /// <summary>Records the end of the guided setup; finishing it again keeps the first date.</summary>
    public void CompleteOnboarding(DateTimeOffset completedAt)
    {
        OnboardingCompletedAt ??= completedAt;
    }

    /// <summary>The public menu (/m/{slug}): off by default, menu only, or menu that takes orders.</summary>
    public OnlineMenuMode OnlineMenuMode { get; private set; } = OnlineMenuMode.Off;

    public void UpdateOnlineMenu(OnlineMenuMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new DomainException("El modo del menú en línea no es válido.");
        OnlineMenuMode = mode;
    }

    public bool HasCapability(TenantCapability capability)
        => (Capabilities & capability) == capability;
}
