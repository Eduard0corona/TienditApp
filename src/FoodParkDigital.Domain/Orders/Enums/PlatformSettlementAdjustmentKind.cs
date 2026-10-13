namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>
/// Why a deposit of a delivery platform is not the sum of its orders' expected net: each adjustment of a settlement says
/// one of these, with a signed amount (negative when the platform deducts).
/// </summary>
public enum PlatformSettlementAdjustmentKind
{
    /// <summary>The part of a discount the business pays, or the one the platform covers (the 35 % Rappi covers).</summary>
    Promotion = 1,
    /// <summary>Missing products and customer claims the platform refunded and charges back.</summary>
    Claim = 2,
    /// <summary>An order the platform cancelled after it was delivered, or after it was settled.</summary>
    Cancellation = 3,
    /// <summary>Charges of the platform itself: advertising, a tablet, penalties.</summary>
    Fee = 4,
    /// <summary>A balance carried from another week (DiDi's negative balance for the cash the business collected).</summary>
    PreviousBalance = 5,
    /// <summary>Anything else, always with a description.</summary>
    Other = 99
}
