namespace FoodParkDigital.Domain.Fiscal.Enums;

/// <summary>Why an email of a CFDI did not go out, for good (Fase 28E, ADR 0049). Stored as a number: keep the values.</summary>
public enum CfdiEmailFailure
{
    /// <summary>The mail server refused the address for good (a mailbox that does not exist, a domain that takes no mail).</summary>
    Rejected = 0,

    /// <summary>The platform has no mail server configured.</summary>
    NotConfigured = 1,

    /// <summary>The CFDI stopped standing (its cancellation was asked) before the email went out: it was not sent.</summary>
    NoLongerStamped = 2,

    /// <summary>The mail server did not take it through the outbox's last attempt (hours of retries): it can be sent again.</summary>
    NotDelivered = 3
}
