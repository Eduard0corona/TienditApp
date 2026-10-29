namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>
/// Why the business forgave part or all of what a customer owed for an order (Fase 26B, ADR 0034). «Otro» always comes with
/// a note.
/// </summary>
public enum OrderWriteOffReason
{
    /// <summary>The customer is not going to pay it (they moved, closed, or it is not worth chasing).</summary>
    WontBeCollected = 1,
    /// <summary>It was recorded wrong: charged and not registered, or sold for more than it was.</summary>
    RecordingError = 2,
    /// <summary>The business decided to give it.</summary>
    Courtesy = 3,
    /// <summary>
    /// The business lowered the price after the sale (a defect, a delay, a deal): a discount or rebate, not a debt it gave up
    /// on (Fase 28J). Its credit note says so.
    /// </summary>
    Discount = 4,
    /// <summary>Anything else, always with a note.</summary>
    Other = 99
}
