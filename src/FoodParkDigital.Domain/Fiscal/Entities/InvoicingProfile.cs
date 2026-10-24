using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// How a business invoices (Fase 28A, ADR 0045): the serie of its invoices and of its payment complements (Fase 28C), and
/// the CSD (certificado de sello digital) its CFDI are sealed with. TienditApp checks the CSD and hands it to the PAC, which keeps it: here only what identifies it
/// is kept (number, RFC, validity, who uploaded it), never its private key or password.
/// </summary>
public partial class InvoicingProfile : AuditableEntity, IAggregateRoot, ITenantScoped
{
    /// <summary>The CSD is about to expire when this little is left: the business is told so it renews it in time.</summary>
    public static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(30);

    public const int CertificateNumberLength = 20;

    private InvoicingProfile() { }

    public Guid TenantId { get; private set; }

    /// <summary>The serie of its invoices: «F» unless the business chooses another one.</summary>
    public string Serie { get; private set; } = OrderInvoice.DefaultSerie;

    /// <summary>
    /// The serie of its payment complements (Fase 28C): «P» unless the business chooses another one. A serie has one counter,
    /// so the same serie as the invoices never repeats a folio.
    /// </summary>
    public string PaymentSerie { get; private set; } = PaymentComplement.DefaultSerie;

    /// <summary>The CSD's number (NoCertificado, 20 digits), or null without one.</summary>
    public string? CertificateNumber { get; private set; }

    /// <summary>The RFC the CSD belongs to: it must be the business's.</summary>
    public string? CertificateRfc { get; private set; }

    public DateTimeOffset? CertificateValidFrom { get; private set; }
    public DateTimeOffset? CertificateValidTo { get; private set; }
    public DateTimeOffset? CertificateUploadedAt { get; private set; }
    public Guid? CertificateUploadedByUserId { get; private set; }

    public byte[] Version { get; private set; } = null!;

    public bool HasCertificate => CertificateNumber is not null;

    public static InvoicingProfile Create(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("La facturación es de un negocio.");
        return new InvoicingProfile { TenantId = tenantId };
    }

    /// <summary>
    /// The CSD the PAC now keeps for the business, already checked (a sealing certificate, its key and password): it
    /// replaces the one before.
    /// </summary>
    public void UseCertificate(
        string number, string rfc, DateTimeOffset validFrom, DateTimeOffset validTo, Guid uploadedByUserId, DateTimeOffset now)
    {
        if (number is null || !CertificateNumberPattern().IsMatch(number))
            throw new DomainException("El número del certificado de sello digital tiene 20 dígitos.");
        var normalizedRfc = rfc?.Trim().ToUpperInvariant();
        if (!SatCatalog.IsValidRfc(normalizedRfc))
            throw new DomainException("El certificado de sello digital no trae un RFC válido.");
        if (validTo <= validFrom)
            throw new DomainException("La vigencia del certificado de sello digital no es válida.");
        if (validTo <= now)
            throw new DomainException("El certificado de sello digital ya venció: tramita uno nuevo en el SAT.");
        if (uploadedByUserId == Guid.Empty)
            throw new DomainException("El certificado necesita quién lo sube.");

        CertificateNumber = number;
        CertificateRfc = normalizedRfc;
        CertificateValidFrom = validFrom;
        CertificateValidTo = validTo;
        CertificateUploadedAt = now;
        CertificateUploadedByUserId = uploadedByUserId;
    }

    /// <summary>The business stops invoicing with its CSD: the invoices already stamped stay.</summary>
    public void RemoveCertificate()
    {
        CertificateNumber = null;
        CertificateRfc = null;
        CertificateValidFrom = null;
        CertificateValidTo = null;
        CertificateUploadedAt = null;
        CertificateUploadedByUserId = null;
    }

    /// <summary>The serie of the next invoices; the ones already stamped keep theirs.</summary>
    public void ChangeSerie(string serie)
    {
        Serie = OrderInvoice.NormalizeSerie(serie);
    }

    /// <summary>The serie of the next payment complements; the ones already stamped keep theirs.</summary>
    public void ChangePaymentSerie(string serie)
    {
        PaymentSerie = Cfdi.NormalizeSerie(serie, "los complementos de pago");
    }

    /// <summary>Whether its CSD seals now for the business's RFC.</summary>
    public bool CanSealFor(string? rfc, DateTimeOffset now)
        => HasCertificate && CertificateValidFrom <= now && now < CertificateValidTo
           && string.Equals(CertificateRfc, rfc?.Trim().ToUpperInvariant(), StringComparison.Ordinal);

    /// <summary>Whether its CSD expires within <see cref="ExpiryWarning"/>.</summary>
    public bool CertificateExpiresSoon(DateTimeOffset now)
        => CertificateValidTo is { } validTo && validTo > now && validTo - now <= ExpiryWarning;

    [GeneratedRegex("^[0-9]{20}$")]
    private static partial Regex CertificateNumberPattern();
}
