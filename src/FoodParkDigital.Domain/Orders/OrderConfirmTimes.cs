using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders;

/// <summary>How soon a business confirms the orders its customers place on its online menu, as Explorar says it (Fase 29T, ADR 0074).</summary>
public enum OrderConfirmTime
{
    WithinFiveMinutes = 0,
    WithinFifteenMinutes = 1,
    WithinHalfAnHour = 2,
    /// <summary>Half an hour or more: Explorar says nothing; its owner reads it in Configuración.</summary>
    Slower = 3
}

/// <summary>
/// How long the customers of a business waited for it to accept or decline the orders they placed on its online menu (Fase
/// 29T, ADR 0074): from when the order arrived until it first left «Por confirmar» by the business. One a business accepts
/// automatically was answered as it arrived. The slowest are those answered after half an hour, and those that waited half
/// an hour without an answer, still waiting or cancelled by their customer then. One that has waited less, or that its
/// customer cancelled sooner, does not count; nor does one ordered with the QR of a table, handled by whoever serves it.
/// </summary>
public static class OrderConfirmTimes
{
    /// <summary>The orders that count arrived in these last days, like the requests of <c>ResponseTimes</c>.</summary>
    public const int WindowDays = 90;

    /// <summary>Fewer orders than these say nothing, like fewer than three requests (Fase 29J).</summary>
    public const int MinOrders = 3;

    /// <summary>
    /// The limit each <see cref="OrderConfirmTime"/> but the last stays under, in its order: a wait of exactly five minutes is
    /// not within five minutes. Whole minutes, which the database adds to when an order arrived.
    /// </summary>
    public static readonly IReadOnlyList<int> LimitMinutes = [5, 15, 30];

    /// <summary>
    /// How long an order of the online menu waits for its answer before it counts among the slowest (Fase 29X, ADR 0078): the
    /// last limit, half an hour.
    /// </summary>
    public static int SlowAfterMinutes => LimitMinutes[^1];

    /// <summary>
    /// How long it waits before the business is warned that it is still waiting (Fase 29X): the middle limit, 15 minutes,
    /// halfway to counting among the slowest.
    /// </summary>
    public static int WarnAfterMinutes => LimitMinutes[1];

    /// <summary>
    /// When the business of an order that arrived at <paramref name="arrivedAt"/> and still waits is reminded by email (Fase
    /// 31K, ADR 0091): when the bell starts marking it, <see cref="WarnAfterMinutes"/> after it arrived.
    /// </summary>
    public static DateTimeOffset RemindAt(DateTimeOffset arrivedAt) => arrivedAt.AddMinutes(WarnAfterMinutes);

    /// <summary>When an order that arrived at <paramref name="arrivedAt"/> is answered in time not to count among the slowest.</summary>
    public static DateTimeOffset ConfirmBy(DateTimeOffset arrivedAt) => arrivedAt.AddMinutes(SlowAfterMinutes);

    /// <summary>Since when the orders count at <paramref name="now"/>: those that arrived in the last <see cref="WindowDays"/> days.</summary>
    public static DateTimeOffset Since(DateTimeOffset now) => now.AddDays(-WindowDays);

    /// <summary>
    /// Within what it confirms its orders: the soonest <see cref="OrderConfirmTime"/> that holds at least half of them, the
    /// slowest counted last. Null with fewer than <see cref="MinOrders"/>.
    /// </summary>
    /// <param name="counts">How many orders within each <see cref="OrderConfirmTime"/>, in its order; the last one counts the slowest.</param>
    public static OrderConfirmTime? Typical(IReadOnlyList<int> counts)
        => TypicalWait.Bucket(counts, LimitMinutes.Count, MinOrders) is { } bucket ? (OrderConfirmTime)bucket : null;

    /// <summary>What Explorar says of it: its <see cref="Typical"/> within half an hour, never <see cref="OrderConfirmTime.Slower"/>.</summary>
    public static OrderConfirmTime? Shown(OrderConfirmTime? typical)
        => typical is OrderConfirmTime.Slower ? null : typical;

    /// <summary>
    /// Its place in the order of Explorar (Fase 29W, ADR 0077), on the scale of <c>ResponseTimes.Order</c>: its first two
    /// tramos with the first two of answering requests, within half an hour with the last of them (within a day), and half an
    /// hour or more with the slowest. In the middle go those that say nothing yet.
    /// </summary>
    public static int Order(OrderConfirmTime typical) => typical switch
    {
        OrderConfirmTime.WithinFiveMinutes => 0,
        OrderConfirmTime.WithinFifteenMinutes => 1,
        OrderConfirmTime.WithinHalfAnHour => 3,
        _ => 4
    };
}
