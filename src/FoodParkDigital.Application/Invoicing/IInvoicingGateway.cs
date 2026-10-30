using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// The platform's PAC (Fase 28A, ADR 0045): it keeps each business's CSD, seals and stamps the CFDI TienditApp computed
/// (it only checks the amounts, never recomputes them), renders its PDF and asks the SAT to cancel it. Without its
/// credentials invoicing is not offered.
/// </summary>
public interface IInvoicingGateway
{
    /// <summary>Whether the platform has the PAC's credentials.</summary>
    bool IsConfigured { get; }

    /// <summary>Whether it stamps in the PAC's test environment (invoices without fiscal effect).</summary>
    bool Sandbox { get; }

    /// <summary>The PAC keeps the business's CSD (already checked by <see cref="SealingCertificateReader"/>).</summary>
    Task SaveCertificateAsync(byte[] certificate, byte[] privateKey, string password, CancellationToken cancellationToken);

    /// <summary>The PAC stops sealing with that CSD.</summary>
    Task RemoveCertificateAsync(string certificateNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Seals and stamps the CFDI. The same key on another attempt gives back the invoice already stamped instead of a
    /// second one.
    /// </summary>
    Task<StampedCfdi> StampAsync(CfdiDocument document, Guid idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// The CFDI the PAC stamped for the platform with that folio fiscal, or null when it has none (Fase 28K): how a stamp a
    /// person found in the SAT's portal is brought back, once the key no longer gives it back. One the SAT has cancelled is
    /// refused (<see cref="InvoicingGatewayRefusedException"/>).
    /// </summary>
    Task<StampedCfdi?> FindStampedAsync(string uuid, CancellationToken cancellationToken);

    /// <summary>
    /// The PDF of a stamped CFDI of the business, with its logo when it has one (Fase 28L): without it if the logo cannot be
    /// read, never failing because of it.
    /// </summary>
    Task<byte[]> RenderPdfAsync(string stampedXml, Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the SAT to cancel a CFDI of the business, with the stored CSD: with motive 01, with the UUID of the stamped CFDI
    /// that replaces it (Fase 28G); with the others, without one.
    /// </summary>
    Task<CfdiCancellationOutcome> CancelAsync(
        string issuerRfc, string uuid, CfdiCancellationMotive motive, string? replacementUuid, CancellationToken cancellationToken);

    /// <summary>What the SAT says of a CFDI now: in force, cancelled, or with its cancellation refused by the receiver.</summary>
    Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken);
}

/// <summary>A CFDI the SAT has: its folio fiscal, its stamped XML and when it was stamped.</summary>
public sealed record StampedCfdi(string Uuid, string Xml, DateTimeOffset StampedAt);

public enum CfdiCancellationOutcome
{
    /// <summary>The SAT took the request; the receiver may still have to accept it.</summary>
    Requested,

    /// <summary>The SAT cancelled it (or it already was).</summary>
    Cancelled
}

/// <summary>What the SAT's public status service is asked: the CFDI's parties, total, UUID and the end of its seal.</summary>
public sealed record CfdiStatusQuery(string IssuerRfc, string ReceiverRfc, decimal Total, string Uuid, string SealTail);

public enum CfdiSatState
{
    InForce,
    Cancelled,
    NotFound
}

/// <summary>The CFDI's state at the SAT, and whether the receiver refused its cancellation.</summary>
public sealed record CfdiSatStatus(CfdiSatState State, bool CancellationRefused);

/// <summary>The PAC did not answer (or the platform cannot use it now): try again later with the same request.</summary>
public sealed class InvoicingGatewayUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The PAC refused the request for good, with a reason the business can act on.</summary>
public sealed class InvoicingGatewayRefusedException(string message) : Exception(message);
