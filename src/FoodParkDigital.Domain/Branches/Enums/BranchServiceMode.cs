namespace FoodParkDigital.Domain.Branches.Enums;

/// <summary>How a branch serves its customers, which decides whether its address is public.</summary>
public enum BranchServiceMode
{
    /// <summary>A place customers come to (to eat, to buy or to pick up): its address is on the online menu.</summary>
    Premises = 0,
    /// <summary>
    /// Delivery only, without premises open to the public (a dark kitchen, often a home): its address stays with the
    /// team and never reaches the customers.
    /// </summary>
    DeliveryOnly = 1
}
