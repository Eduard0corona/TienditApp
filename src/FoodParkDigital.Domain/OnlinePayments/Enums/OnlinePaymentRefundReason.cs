namespace FoodParkDigital.Domain.OnlinePayments.Enums;

/// <summary>Why money paid online goes back to the customer.</summary>
public enum OnlinePaymentRefundReason
{
    /// <summary>The payment was approved but its order could not be placed.</summary>
    NotPlaced = 1,

    /// <summary>A second payment was approved for an order already paid.</summary>
    DuplicatePayment = 2,

    /// <summary>The business refunded or voided the order with the «En línea» method.</summary>
    ByBusiness = 3,

    /// <summary>
    /// Given back in Mercado Pago without TienditApp asking (Fase 24K): the business from its account, or Mercado Pago
    /// itself. TienditApp learned of it and recorded it on the order.
    /// </summary>
    InMercadoPago = 4,

    /// <summary>Mercado Pago gave it back to the customer because of a claim the customer opened there (Fase 24K).</summary>
    Claim = 5,

    /// <summary>
    /// The customer's bank took it back with a chargeback, and the dispute ended in the customer's favor (Fase 24K): what
    /// was left paid online.
    /// </summary>
    Chargeback = 6
}
