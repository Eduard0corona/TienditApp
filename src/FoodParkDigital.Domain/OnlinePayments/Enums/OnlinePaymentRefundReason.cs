namespace FoodParkDigital.Domain.OnlinePayments.Enums;

/// <summary>Why money paid online goes back to the customer.</summary>
public enum OnlinePaymentRefundReason
{
    /// <summary>The payment was approved but its order could not be placed.</summary>
    NotPlaced = 1,

    /// <summary>A second payment was approved for an order already paid.</summary>
    DuplicatePayment = 2,

    /// <summary>The business refunded or voided the order with the «En línea» method.</summary>
    ByBusiness = 3
}
