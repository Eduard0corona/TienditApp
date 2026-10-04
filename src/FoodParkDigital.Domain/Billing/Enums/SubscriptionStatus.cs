namespace FoodParkDigital.Domain.Billing.Enums;

/// <summary>Where a business stands with the platform's billing.</summary>
public enum SubscriptionStatus
{
    /// <summary>Free trial: full use of the plan until it ends.</summary>
    Trial = 0,
    /// <summary>Paid through a date.</summary>
    Active = 1,
    /// <summary>The trial or the paid period ended: the business keeps working during the grace days.</summary>
    PastDue = 2,
    /// <summary>Without access: unpaid after the grace days, or suspended by the platform.</summary>
    Suspended = 3,
    /// <summary>Closed by the platform: without access.</summary>
    Cancelled = 4
}
