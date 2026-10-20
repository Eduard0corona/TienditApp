namespace FoodParkDigital.Domain.Customers.Enums;

/// <summary>What moved the balance in a customer's favor (Fase 26D, ADR 0036).</summary>
public enum CustomerBalanceEntryKind
{
    /// <summary>The customer left money in their favor: in advance, or the rest of a payment to their account.</summary>
    Deposit = 0,
    /// <summary>A refund of an order went to their balance instead of out of a drawer.</summary>
    Refund = 1,
    /// <summary>They paid an order with their balance.</summary>
    Applied = 2,
    /// <summary>The business gave them back part or all of their balance.</summary>
    Returned = 3
}
