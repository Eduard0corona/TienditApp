namespace FoodParkDigital.Domain.Reviews.Enums;

/// <summary>
/// What is wrong with a review: why its business reports it, and why the platform hides it (Fase 24H, ADR 0030). A list
/// and not a text, so neither side writes about the customer.
/// </summary>
public enum ReviewConcern
{
    /// <summary>Insults, threats or hate.</summary>
    Offensive = 1,

    /// <summary>Someone's name, address or other private data.</summary>
    PersonalData = 2,

    /// <summary>Advertising or text with nothing to do with the business.</summary>
    Spam = 3,

    /// <summary>It is not about an order or an appointment with the business.</summary>
    NotAnExperience = 4,

    Other = 5
}
