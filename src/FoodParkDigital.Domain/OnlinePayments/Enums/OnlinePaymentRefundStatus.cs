namespace FoodParkDigital.Domain.OnlinePayments.Enums;

/// <summary>Whether Mercado Pago already gave the money back.</summary>
public enum OnlinePaymentRefundStatus
{
    /// <summary>TienditApp asked for it (or is about to): the queue retries until Mercado Pago answers.</summary>
    Requested = 0,

    /// <summary>Mercado Pago gave it back.</summary>
    Completed = 1,

    /// <summary>Mercado Pago refused it (for instance, without balance in the account): the business gives it back there.</summary>
    Failed = 2
}
