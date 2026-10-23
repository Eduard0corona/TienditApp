namespace FoodParkDigital.Domain.OnlinePayments.Enums;

/// <summary>
/// A chargeback or a claim on the payment that placed an order, as Mercado Pago reports it (Fase 24K). While it is open
/// Mercado Pago holds the money; only a lost chargeback gives it back to the customer.
/// </summary>
public enum OnlinePaymentDispute
{
    None = 0,

    /// <summary>The customer's bank or the customer disputes the payment: Mercado Pago holds the money until it decides.</summary>
    Open = 1,

    /// <summary>It ended in the customer's favor: the money went back to the customer.</summary>
    Lost = 2,

    /// <summary>It ended in the business's favor: the money stays with the business.</summary>
    Won = 3
}
