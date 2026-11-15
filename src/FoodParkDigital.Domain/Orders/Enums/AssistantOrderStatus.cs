namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>Where an order an AI assistant prepared stands (Fase 32B, ADR 0096). Expired is not kept: it is its time.</summary>
public enum AssistantOrderStatus
{
    /// <summary>Prepared: the customer has not sent it from its link yet. Past its time, it expired.</summary>
    Pending = 0,

    /// <summary>The customer reviewed it and sent it from the store: it is an online order of the business.</summary>
    Confirmed = 1
}
