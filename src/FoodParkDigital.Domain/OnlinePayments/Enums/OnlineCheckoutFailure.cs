namespace FoodParkDigital.Domain.OnlinePayments.Enums;

/// <summary>Why an approved payment did not become an order, so it goes back to the customer.</summary>
public enum OnlineCheckoutFailure
{
    /// <summary>It was approved after the time to pay was over.</summary>
    PaidAfterExpiry = 1,

    /// <summary>The business stopped taking orders (it closed or paused them) while the customer paid.</summary>
    StoppedTakingOrders = 2,

    /// <summary>The order was refused when placed: a product ran out, a price or the delivery changed.</summary>
    OrderRefused = 3,

    /// <summary>The payment was not for the amount, the currency or the account of the order.</summary>
    PaymentMismatch = 4
}
