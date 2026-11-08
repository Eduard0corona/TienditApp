using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders;

/// <summary>How soon a business has ready the orders its customers place on its online menu, as Explorar says it (Fase 29Y, ADR 0079).</summary>
public enum ReadyTime
{
    WithinFifteenMinutes = 0,
    WithinHalfAnHour = 1,
    WithinAnHour = 2,
    /// <summary>An hour or more: Explorar says nothing; its owner reads it in Configuración.</summary>
    Slower = 3
}

/// <summary>
/// How long the customers of a business waited for the orders they placed on its online menu to be ready (Fase 29Y, ADR
/// 0079): from when the order arrived until it was marked «Listo» (once: its state machine leaves it only to deliver it),
/// confirming it included, as its customer waits. Only
/// a kitchen marks an order ready: one that never was (a store, or a kitchen that hands it over without marking it) does not
/// count, neither as quick nor as slow; nor does one ordered with the QR of a table, handled by whoever serves it.
/// </summary>
public static class OrderReadyTimes
{
    /// <summary>The orders that count arrived in these last days, like those of <see cref="OrderConfirmTimes"/>.</summary>
    public const int WindowDays = 90;

    /// <summary>Fewer orders than these say nothing, like fewer than three requests (Fase 29J).</summary>
    public const int MinOrders = 3;

    /// <summary>
    /// The limit each <see cref="ReadyTime"/> but the last stays under, in its order: an order ready in exactly fifteen minutes
    /// is not within fifteen minutes. Whole minutes, which the database adds to when an order arrived.
    /// </summary>
    public static readonly IReadOnlyList<int> LimitMinutes = [15, 30, 60];

    /// <summary>Since when the orders count at <paramref name="now"/>: those that arrived in the last <see cref="WindowDays"/> days.</summary>
    public static DateTimeOffset Since(DateTimeOffset now) => now.AddDays(-WindowDays);

    /// <summary>
    /// Within what it has its orders ready: the soonest <see cref="ReadyTime"/> that holds at least half of them, the slowest
    /// counted last. Null with fewer than <see cref="MinOrders"/>.
    /// </summary>
    /// <param name="counts">How many orders within each <see cref="ReadyTime"/>, in its order; the last one counts the slowest.</param>
    public static ReadyTime? Typical(IReadOnlyList<int> counts)
        => TypicalWait.Bucket(counts, LimitMinutes.Count, MinOrders) is { } bucket ? (ReadyTime)bucket : null;

    /// <summary>What Explorar says of it: its <see cref="Typical"/> within an hour, never <see cref="ReadyTime.Slower"/>.</summary>
    public static ReadyTime? Shown(ReadyTime? typical)
        => typical is ReadyTime.Slower ? null : typical;
}
