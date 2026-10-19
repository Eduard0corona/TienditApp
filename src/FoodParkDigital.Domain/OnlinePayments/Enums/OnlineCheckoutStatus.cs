namespace FoodParkDigital.Domain.OnlinePayments.Enums;

/// <summary>Where an order paid online stands (Fase 24I, ADR 0031).</summary>
public enum OnlineCheckoutStatus
{
    /// <summary>The customer is paying in Mercado Pago: there is no order yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The payment was approved and the order was placed with it.</summary>
    Placed = 1,

    /// <summary>No payment was approved before it expired: there never was an order.</summary>
    Expired = 2,

    /// <summary>A payment was approved but the order could not be placed: the payment goes back to the customer.</summary>
    NotPlaced = 3
}
