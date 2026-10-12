namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>Who charged the customer for an order of a delivery platform (Rappi, Uber Eats, DiDi Food).</summary>
public enum PlatformCollectedBy
{
    /// <summary>The platform charged in its app and settles it later, minus its commission and withholdings.</summary>
    Platform = 0,
    /// <summary>The business charged (for example, the cash a DiDi courier hands over): it goes through a drawer.</summary>
    Business = 1
}
