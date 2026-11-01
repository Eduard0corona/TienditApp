namespace FoodParkDigital.Domain.Quotes;

/// <summary>Why an email of a quote will not go out (Fase 29D, ADR 0053 point 16). Stored as its number: never renumber.</summary>
public enum QuoteEmailFailure
{
    /// <summary>The mail server refused the address.</summary>
    Rejected = 0,
    /// <summary>TienditApp has no mail server set up.</summary>
    NotConfigured = 1,
    /// <summary>The mail server did not take it after every try.</summary>
    NotDelivered = 2
}
