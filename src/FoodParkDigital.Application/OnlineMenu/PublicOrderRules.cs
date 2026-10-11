using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Application.OnlineMenu;

/// <summary>When the public menu takes orders although the business is closed. The menu and the order ask the same.</summary>
internal static class PublicOrderRules
{
    /// <summary>
    /// Whether a customer at one of its tables (the table's QR) still orders while the business is closed. Default
    /// decision (ADR 0012), pending the owner's confirmation: outside its hours, yes, as ADR 0011 decided (the customer
    /// is already there); on a closed special day, no, because nobody is attending.
    /// </summary>
    public static bool TableOverridesClosure(ScheduleStatus status) => !status.ClosedBySpecialDay;
}
