using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
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

    public const int MinCreditTermDays = 1;
    public const int MaxCreditTermDays = 120;
    public const string CreditTermRangeMessage = "El plazo del fiado va de 1 a 120 días, o sin plazo.";

    /// <summary>
    /// How many days a customer has to pay what they owe (fiado, Fase 26B, ADR 0034), counted from the business day they
    /// started to owe it; null when the business gives no term. A customer may have a term of their own (Fase 26C). It only
    /// tells what is overdue: it neither charges interest nor stops selling on credit.
    /// </summary>
    public int? CreditTermDays { get; private set; }

    public void SetCreditTermDays(int? days)
    {
        if (days is < MinCreditTermDays or > MaxCreditTermDays)
            throw new DomainException(CreditTermRangeMessage);
        CreditTermDays = days;
    }

    public const int MinCreditReminderEveryDays = 1;
    public const int MaxCreditReminderEveryDays = 30;
    public const int DefaultCreditReminderEveryDays = 7;
    public const string CreditReminderEveryDaysMessage = "Los recordatorios del fiado se repiten cada 1 a 30 días.";

    /// <summary>
    /// WhatsApp reminders to the customers with something overdue (Fase 26C, ADR 0035); off until the owner turns them on.
    /// </summary>
    public bool CreditRemindersEnabled { get; private set; }
    /// <summary>The days between two reminders to the same customer while something stays overdue.</summary>
    public int CreditReminderEveryDays { get; private set; } = DefaultCreditReminderEveryDays;
    /// <summary>The credit reminder template; null uses the default text.</summary>
    public Guid? CreditReminderTemplateId { get; private set; }

    public void UpdateCreditReminders(bool enabled, int everyDays, Guid? templateId)
    {
        if (everyDays is < MinCreditReminderEveryDays or > MaxCreditReminderEveryDays)
            throw new DomainException(CreditReminderEveryDaysMessage);

        CreditRemindersEnabled = enabled;
        CreditReminderEveryDays = everyDays;
        CreditReminderTemplateId = templateId;
    }

    public const int MinCreditDueSoonDaysBefore = 1;
    public const int MaxCreditDueSoonDaysBefore = 7;
    public const int DefaultCreditDueSoonDaysBefore = 2;
    public const string CreditDueSoonDaysBeforeMessage = "El aviso antes de vencer se manda de 1 a 7 días antes.";

    /// <summary>
    /// A WhatsApp notice to the customers who are up to date, a few days before something of theirs comes due (Fase 26E,
    /// ADR 0043); off until the owner turns it on.
    /// </summary>
    public bool CreditDueSoonEnabled { get; private set; }
    /// <summary>How many days before the last day to pay the notice goes out.</summary>
    public int CreditDueSoonDaysBefore { get; private set; } = DefaultCreditDueSoonDaysBefore;
    /// <summary>The template of the notice; null uses the default text.</summary>
    public Guid? CreditDueSoonTemplateId { get; private set; }

    public void UpdateCreditDueSoon(bool enabled, int daysBefore, Guid? templateId)
    {
        if (daysBefore is < MinCreditDueSoonDaysBefore or > MaxCreditDueSoonDaysBefore)
            throw new DomainException(CreditDueSoonDaysBeforeMessage);

        CreditDueSoonEnabled = enabled;
        CreditDueSoonDaysBefore = daysBefore;
        CreditDueSoonTemplateId = templateId;
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

    /// <summary>
    /// Whether Explorar lists the business while its menu is published (ADR 0021): on until the owner hides it. A menu
    /// that is off is never listed, whatever this says.
    /// </summary>
    public bool ListedInDirectory { get; private set; } = true;

    public void SetDirectoryListing(bool listed)
    {
        ListedInDirectory = listed;
    }

    /// <summary>
    /// Whether customers book appointments from the online menu (Fase 24D, ADR 0025): off until the owner turns it on. It
    /// works while the menu is published, the agenda module is on, the business has weekly hours and its main branch has
    /// premises; this only says the owner wants it.
    /// </summary>
    public bool OnlineBookingEnabled { get; private set; }

    /// <summary>Appointments the business attends at once: a time is offered online while fewer than these overlap it.</summary>
    public int OnlineBookingCapacity { get; private set; } = OnlineBookingRules.DefaultCapacity;

    public void UpdateOnlineBooking(bool enabled, int capacity)
    {
        if (capacity is < 1 or > OnlineBookingRules.MaxCapacity)
            throw new DomainException(OnlineBookingRules.CapacityMessage);
        OnlineBookingEnabled = enabled;
        OnlineBookingCapacity = capacity;
    }

    /// <summary>
    /// Whether the business receives visits to quote at its customers' homes (Fase 29A, ADR 0053): a trade starts with it on,
    /// any other business off. It works while the agenda module is on, the menu is published, the business has an active
    /// delivery zone and weekly hours; it never asks for premises nor an open cash drawer. This only says the owner wants it.
    /// </summary>
    public bool HomeVisitRequestsEnabled { get; private set; }

    /// <summary>The catalog service that is the visit («Visita y diagnóstico»); null for none, and the visit costs its zone.</summary>
    public Guid? HomeVisitServiceId { get; private set; }

    /// <summary>How the visit is charged: its service plus the charge of its zone until the owner picks otherwise.</summary>
    public HomeVisitChargePolicy HomeVisitChargePolicy { get; private set; } = HomeVisitChargePolicy.Charged;

    /// <summary>
    /// When the business dismissed the notice that its store shows the address of its main branch; null while it shows
    /// (only to a business that receives visits or is a trade, and whose main branch has premises).
    /// </summary>
    public DateTimeOffset? HomeVisitAddressNoticeDismissedAt { get; private set; }

    /// <summary>
    /// The visits to quote, its visit service and how it is charged. The caller checks that the service is a service of
    /// the business; a policy that does not exist is refused.
    /// </summary>
    public void UpdateHomeVisitRequests(bool enabled, Guid? visitServiceId, HomeVisitChargePolicy chargePolicy)
    {
        if (!Enum.IsDefined(chargePolicy))
            throw new DomainException(HomeVisitRules.ChargePolicyMessage);
        if (visitServiceId == Guid.Empty)
            throw new DomainException(HomeVisitRules.VisitServiceMessage);
        HomeVisitRequestsEnabled = enabled;
        HomeVisitServiceId = visitServiceId;
        HomeVisitChargePolicy = chargePolicy;
    }

    /// <summary>A trade starts receiving visits to quote when it signs up (ADR 0053), with or without its zones and hours yet.</summary>
    public void StartReceivingHomeVisits() => HomeVisitRequestsEnabled = true;

    /// <summary>The visit service of a starter template, unless the business already has one.</summary>
    public void SuggestHomeVisitService(Guid visitServiceId)
    {
        if (visitServiceId == Guid.Empty)
            throw new DomainException(HomeVisitRules.VisitServiceMessage);
        HomeVisitServiceId ??= visitServiceId;
    }

    /// <summary>Dismisses the notice of the published address once; dismissing it again keeps the first time.</summary>
    public void DismissHomeVisitAddressNotice(DateTimeOffset now) => HomeVisitAddressNoticeDismissedAt ??= now;

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
