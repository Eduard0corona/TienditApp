using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.OnlineMenu;

/// <summary>Whether a business takes online orders now, or why not.</summary>
internal enum OnlineOrderGate
{
    /// <summary>Orders arrive now.</summary>
    Open,

    /// <summary>Its menu does not take orders: it is published to be seen, or not published.</summary>
    MenuOnly,

    /// <summary>Closed by its hours or by a special day.</summary>
    Closed,

    /// <summary>Open, but no drawer is open at its main branch, where online orders arrive and every payment enters.</summary>
    Paused
}

/// <summary>
/// The one rule of online orders, asked by the menu (GetPublicMenuQuery), its orders (PlacePublicOrderCommand) and
/// Explorar (GetPublicDirectoryQuery), so they never disagree: the menu takes orders, the business is open by its hours
/// and special days, and a drawer is open at its main branch. A customer seated at one of its tables still orders while
/// it is closed, except on a closed special day (<see cref="PublicOrderRules"/>).
/// </summary>
internal static class OnlineOrderAvailability
{
    /// <param name="mode">How the business published its menu.</param>
    /// <param name="hours">Its hours now; null when it never closes.</param>
    /// <param name="drawerOpen">Whether a drawer is open at its main branch.</param>
    /// <param name="seatedAtTable">Whether the customer is at one of its tables in use; Explorar has no table.</param>
    public static OnlineOrderGate Gate(OnlineMenuMode mode, BusinessHoursNow? hours, bool drawerOpen, bool seatedAtTable = false)
    {
        if (mode != OnlineMenuMode.Orders)
            return OnlineOrderGate.MenuOnly;
        if (hours is { Status.IsOpen: false } && !(seatedAtTable && PublicOrderRules.TableOverridesClosure(hours.Status)))
            return OnlineOrderGate.Closed;
        return drawerOpen ? OnlineOrderGate.Open : OnlineOrderGate.Paused;
    }

    /// <summary>
    /// Whether a table can change the answer: only while the business is closed, and not on a closed special day. Only
    /// then is it worth looking the table up.
    /// </summary>
    public static bool TableMayOrderWhileClosed(BusinessHoursNow? hours)
        => hours is { Status.IsOpen: false } && PublicOrderRules.TableOverridesClosure(hours.Status);
}
