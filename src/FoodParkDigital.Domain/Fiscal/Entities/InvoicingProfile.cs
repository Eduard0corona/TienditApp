using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// How a business invoices (Fase 28A, ADR 0045): the serie of its invoices, of its payment complements (Fase 28C) and of its
/// credit notes (Fase 28D), and the CSD (certificado de sello digital) its CFDI are sealed with. TienditApp checks the CSD and
/// hands it to the PAC, which keeps it: here only what identifies it is kept (number, RFC, validity, who uploaded it), never
/// its private key or password. And whether its customers invoice their tickets online (Fase 28F, ADR 0050).
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

    /// <summary>The serie of its credit notes (Fase 28D): «NC» unless the business chooses another one.</summary>
    public string CreditNoteSerie { get; private set; } = CreditNote.DefaultSerie;

    /// <summary>The CSD's number (NoCertificado, 20 digits), or null without one.</summary>
    public string? CertificateNumber { get; private set; }

    /// <summary>The RFC the CSD belongs to: it must be the business's.</summary>
    public string? CertificateRfc { get; private set; }

    public DateTimeOffset? CertificateValidFrom { get; private set; }
    public DateTimeOffset? CertificateValidTo { get; private set; }
    public DateTimeOffset? CertificateUploadedAt { get; private set; }
    public Guid? CertificateUploadedByUserId { get; private set; }

    /// <summary>Whether its customers invoice their tickets online, with the folio and code the ticket prints (Fase 28F).</summary>
    public bool SelfInvoicing { get; private set; }

    /// <summary>
    /// The key of its tickets' codes (<see cref="SelfInvoiceCode"/>): created the first time self-invoicing is turned on and
    /// kept when it is turned off, so the tickets already printed work again when it is turned back on. Never shown.
    /// </summary>
    public byte[]? SelfInvoiceKey { get; private set; }

    /// <summary>
    /// The message template its CFDI go by WhatsApp with (Fase 28M, ADR 0058): one approved in Meta with a document header,
    /// which carries the PDF. Null, or one no longer there, active or for invoices, sends TienditApp's text, which WhatsApp
    /// only delivers within 24 hours of the customer's last message.
    /// </summary>
    public Guid? WhatsAppTemplateId { get; private set; }

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

    /// <summary>The serie of the next credit notes; the ones already stamped keep theirs.</summary>
    public void ChangeCreditNoteSerie(string serie)
    {
        CreditNoteSerie = Cfdi.NormalizeSerie(serie, "las notas de crédito");
    }

    /// <summary>Its customers invoice their tickets online from now on; the first time, with a new key.</summary>
    public void TurnOnSelfInvoicing()
    {
        SelfInvoiceKey ??= SelfInvoiceCode.NewKey();
        SelfInvoicing = true;
    }

    /// <summary>Only the business invoices again; the key stays, and so do the invoices its customers already have.</summary>
    public void TurnOffSelfInvoicing() => SelfInvoicing = false;

    /// <summary>The template the next CFDI go by WhatsApp with, or TienditApp's text (null).</summary>
    public void UseWhatsAppTemplate(Guid? templateId)
    {
        if (templateId == Guid.Empty)
            throw new DomainException("Elige una plantilla o el texto de TienditApp.");
        WhatsAppTemplateId = templateId;
    }

    /// <summary>The code the ticket of an order prints while self-invoicing is on; null when it is off.</summary>
    public string? SelfInvoiceCodeFor(Guid orderId)
        => SelfInvoicing && SelfInvoiceKey is not null ? SelfInvoiceCode.For(SelfInvoiceKey, orderId) : null;

    /// <summary>Whether a customer can invoice the order with what they typed: self-invoicing is on and it is its code.</summary>
    public bool TakesSelfInvoiceCode(Guid orderId, string? typed)
        => SelfInvoicing && SelfInvoiceKey is not null && SelfInvoiceCode.Matches(SelfInvoiceKey, orderId, typed);

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
