namespace FoodParkDigital.Domain.Fiscal.Enums;

/// <summary>Where the invoice of an order stands with the PAC and the SAT (Fase 28A, ADR 0045).</summary>
public enum OrderInvoiceStatus
{
    /// <summary>
    /// Its folio, date and idempotency key are saved and TienditApp asked the PAC to stamp it (or is about to). Without an
    /// answer it is asked again with the same key, so it is never stamped twice.
    /// </summary>
    Stamping = 0,

    /// <summary>The SAT has it: it has its UUID, its XML and its PDF.</summary>
    Stamped = 1,

    /// <summary>The PAC refused it, with its reason (for instance, a receiver that is not in the SAT's records). Its folio is lost.</summary>
    Refused = 2,

    /// <summary>Its cancellation was asked and waits for the receiver to accept it (up to 3 business days).</summary>
    Cancelling = 3,

    /// <summary>The SAT cancelled it. It is kept; the order can be invoiced again.</summary>
    Cancelled = 4
}
