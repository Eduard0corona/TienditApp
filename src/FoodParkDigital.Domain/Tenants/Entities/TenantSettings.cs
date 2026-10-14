using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants.Entities;

public class TenantSettings : BaseEntity, ITenantScoped
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

    /// <summary>When the owner finished or skipped the guided setup.</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; private set; }

    /// <summary>Which orders send the customer WhatsApp messages: by default not counter sales (the customer is there).</summary>
    public OrderNotificationPolicy OrderNotificationPolicy { get; private set; } = OrderNotificationPolicy.RemoteAndServices;

    /// <summary>
    /// Whether a sale whose recipes need more supplies than there are is refused (default) or goes through with a
    /// warning. Orders customers place themselves and work already done (tabs, completed services) never block.
    /// </summary>
    public InsufficientStockPolicy InsufficientStockPolicy { get; private set; } = InsufficientStockPolicy.Block;

    public const int MinReminderHoursBefore = 1;
    public const int MaxReminderHoursBefore = 72;
    public const int DefaultReminderHoursBefore = 24;

    /// <summary>WhatsApp reminders before each appointment; off until the business turns them on.</summary>
    public bool AppointmentRemindersEnabled { get; private set; }
    public int AppointmentReminderHoursBefore { get; private set; } = DefaultReminderHoursBefore;
    /// <summary>The reminder template; null uses the default text.</summary>
    public Guid? AppointmentReminderTemplateId { get; private set; }

    /// <summary>
    /// The minutes a team member needs between a visit at the customer's address and their other appointments (Fase 22E);
    /// 0 for none.
    /// </summary>
    public int HomeVisitTravelMinutes { get; private set; } = HomeVisitRules.DefaultTravelMinutes;

    public Tenant Tenant { get; private set; } = null!;

    public void Update(string currency, string timeZone, bool autoAcceptOrders, int maxConcurrentOrders)
    {
        Currency = currency;
        TimeZone = timeZone;
        AutoAcceptOrders = autoAcceptOrders;
        MaxConcurrentOrders = maxConcurrentOrders;
    }

    /// <summary>Where the business lives its days: cuts, reports and reminders start at its midnight.</summary>
    public void SetTimeZone(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 50)
            throw new DomainException("La zona horaria no es válida.");
        TimeZone = timeZone;
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

    public void SetOrderNotificationPolicy(OrderNotificationPolicy policy)
    {
        if (!Enum.IsDefined(policy))
            throw new DomainException("La política de avisos no es válida.");
        OrderNotificationPolicy = policy;
    }

    public void SetInsufficientStockPolicy(InsufficientStockPolicy policy)
    {
        if (!Enum.IsDefined(policy))
            throw new DomainException("La política de existencias no es válida.");
        InsufficientStockPolicy = policy;
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

    /// <summary>The travel time between a visit at the customer's address and another appointment of the same person.</summary>
    public void SetHomeVisitTravelMinutes(int minutes)
    {
        if (minutes is < 0 or > HomeVisitRules.MaxTravelMinutes)
            throw new DomainException(HomeVisitRules.TravelMinutesMessage);
        HomeVisitTravelMinutes = minutes;
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

    /// <summary>The colors of the business in its team's app and its public menu: TienditApp's until the owner picks one.</summary>
    public BusinessTheme Theme { get; private set; } = BusinessTheme.Nopal;

    public void SetTheme(BusinessTheme theme)
    {
        if (!Enum.IsDefined(theme))
            throw new DomainException("El tema no existe.");
        Theme = theme;
    }

    /// <summary>
    /// When the business attends, in its time zone; null until the owner sets it. With hours, the public menu says
    /// whether it is open and takes orders for pickup or delivery only while it is.
    /// </summary>
    public OpeningHours? OpeningHours { get; private set; }

    /// <summary>Replaces the hours; null removes them (the business never shows as closed).</summary>
    public void SetOpeningHours(OpeningHours? hours)
    {
        OpeningHours = hours;
    }

    public bool HasCapability(TenantCapability capability)
        => (Capabilities & capability) == capability;
}
