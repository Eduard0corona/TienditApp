namespace FoodParkDigital.Domain.Tenants.Enums;

/// <summary>What a sale made by the team does when the supplies of its recipes are not in stock.</summary>
public enum InsufficientStockPolicy
{
    /// <summary>The sale is refused and names the missing supply: the stock never goes below zero.</summary>
    Block = 0,
    /// <summary>The sale goes through, the stock goes below zero and the cashier is told which supply ran out.</summary>
    Warn = 1
}
