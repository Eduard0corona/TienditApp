namespace FoodParkDigital.Domain.Fiscal.Enums;

/// <summary>
/// Why the CFDI of a month did not go to the accountant, for good (Fase 28O, ADR 0060). Stored as a number: keep the values.
/// </summary>
public enum CfdiMonthEmailFailure
{
    /// <summary>The mail server refused the address for good (a mailbox that does not exist, a domain that takes no mail).</summary>
    Rejected = 0,

    /// <summary>The platform has no mail server configured.</summary>
    NotConfigured = 1,

    /// <summary>The mail server did not take it through the outbox's last attempt (hours of retries): it can be sent again.</summary>
    NotDelivered = 2,

    /// <summary>The month's file is larger than an email carries: it is downloaded and sent by hand.</summary>
    TooLarge = 3,

    /// <summary>The month has more CFDI than one file holds: the SAT's bulk download has them.</summary>
    TooMany = 4
}
