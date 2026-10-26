using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// What every CFDI a business stamps through the platform's PAC has (Fases 28A to 28C, ADR 0045 to 0047): the invoices of
/// ingreso (of an order and global) and the payment complements. Its folio, date and idempotency key are fixed and saved
/// before the PAC is asked, so asking again after a lost answer never stamps it twice; what it says is the CFDI the backend
/// computed. It is never deleted: a cancelled one stays.
/// </summary>
public abstract class Cfdi : AuditableEntity, ITenantScoped
{
    public const int MaxSerieLength = 10;
    public const int UuidLength = 36;
    public const int MaxProviderInvoiceIdLength = 64;
    public const int MaxFailureMessageLength = 500;
    public const int MaxFileKeyLength = 300;

    public Guid TenantId { get; protected set; }

    public string Serie { get; protected set; } = null!;
    public long Folio { get; protected set; }

    /// <summary>The date the CFDI carries, fixed when it was asked for so that asking again sends the same one.</summary>
    public DateTimeOffset IssuedAt { get; protected set; }

    /// <summary>The key the PAC knows the request by: the same one on every attempt.</summary>
    public Guid IdempotencyKey { get; protected set; }

    public OrderInvoiceStatus Status { get; protected set; }

    /// <summary>
    /// The CFDI as it was asked for, written by the application: every attempt sends exactly this, even if what it covers
    /// changes afterwards.
    /// </summary>
    public string Document { get; protected set; } = null!;

    public DateTimeOffset RequestedAt { get; protected set; }

    /// <summary>The folio fiscal the SAT gave it.</summary>
    public string? Uuid { get; protected set; }

    /// <summary>The PAC's id of the CFDI: its PDF, its XML and its cancellation are asked with it.</summary>
    public string? ProviderInvoiceId { get; protected set; }

    public DateTimeOffset? StampedAt { get; protected set; }

    /// <summary>The stamped XML, in the business's private files.</summary>
    public string? XmlFileKey { get; protected set; }

    /// <summary>Its PDF, in the business's private files (kept apart: the PAC may give it later than the XML).</summary>
    public string? PdfFileKey { get; protected set; }

    /// <summary>Why the PAC refused it, or why its cancellation did not go through.</summary>
    public string? FailureMessage { get; protected set; }

    public DateTimeOffset? RefusedAt { get; protected set; }

    public CfdiCancellationMotive? CancellationMotive { get; protected set; }
    public Guid? CancellationRequestedByUserId { get; protected set; }
    public DateTimeOffset? CancellationRequestedAt { get; protected set; }
    public DateTimeOffset? CancelledAt { get; protected set; }

    /// <summary>The PAC's answer and the business change it: a change on a stale copy is a conflict (409).</summary>
    public byte[] Version { get; protected set; } = null!;

    /// <summary>Whether it stands with the SAT, or may: stamping, stamped or waiting for its cancellation.</summary>
    public bool IsLive => Status is OrderInvoiceStatus.Stamping or OrderInvoiceStatus.Stamped or OrderInvoiceStatus.Cancelling;

    /// <summary>The folio as the business reads it: «F-123».</summary>
    public string Number => $"{Serie}-{Folio}";

    /// <summary>
    /// What every request fixes before the PAC is asked: serie and folio, the date, a new key and the CFDI. The caller checks
    /// its own parties and amounts first.
    /// </summary>
    protected void Begin(Guid tenantId, string serie, long folio, string document, DateTimeOffset now)
    {
        var normalizedSerie = NormalizeSerie(serie);
        if (folio <= 0)
            throw new DomainException("El folio de la factura es un número positivo.");
        if (string.IsNullOrWhiteSpace(document))
            throw new DomainException("La factura necesita sus conceptos.");

        TenantId = tenantId;
        Serie = normalizedSerie;
        Folio = folio;
        IssuedAt = now;
        IdempotencyKey = Guid.NewGuid();
        Status = OrderInvoiceStatus.Stamping;
        Document = document;
        RequestedAt = now;
    }

    /// <summary>The PAC stamped it. Hearing it again (a repeated answer) changes nothing.</summary>
    public void Stamp(string uuid, string providerInvoiceId, DateTimeOffset stampedAt, string xmlFileKey)
    {
        var normalizedUuid = uuid?.Trim().ToUpperInvariant();
        if (normalizedUuid is null || normalizedUuid.Length != UuidLength || !Guid.TryParse(normalizedUuid, out _))
            throw new DomainException("El folio fiscal (UUID) de la factura no es válido.");
        if (Status != OrderInvoiceStatus.Stamping)
        {
            if (Uuid == normalizedUuid)
                return;
            throw new DomainException("Esta factura ya no espera su timbre.");
        }
        if (string.IsNullOrWhiteSpace(providerInvoiceId) || providerInvoiceId.Trim().Length > MaxProviderInvoiceIdLength)
            throw new DomainException("La factura timbrada necesita el identificador del PAC.");

        Uuid = normalizedUuid;
        ProviderInvoiceId = providerInvoiceId.Trim();
        StampedAt = stampedAt;
        XmlFileKey = FileKey(xmlFileKey);
        Status = OrderInvoiceStatus.Stamped;
    }

    /// <summary>Its PDF, kept when the PAC gives it.</summary>
    public void AttachPdf(string pdfFileKey)
    {
        if (Uuid is null)
            throw new DomainException("Solo una factura timbrada tiene PDF.");
        PdfFileKey = FileKey(pdfFileKey);
    }

    /// <summary>The PAC refused it for good: what it covered can be stamped again (with another folio).</summary>
    public void Refuse(string? message, DateTimeOffset now)
    {
        if (Status != OrderInvoiceStatus.Stamping)
            throw new DomainException("Esta factura ya no espera su timbre.");

        FailureMessage = Trim(message, "El PAC no timbró la factura.");
        RefusedAt = now;
        Status = OrderInvoiceStatus.Refused;
        Released();
    }

    /// <summary>The owner or an administrator asks the SAT to cancel it, with one of the motives this kind of CFDI takes.</summary>
    public void RequestCancellation(CfdiCancellationMotive motive, Guid userId, DateTimeOffset now)
    {
        if (!TakesCancellationMotive(motive))
            throw new DomainException(CancellationMotivesMessage);
        if (userId == Guid.Empty)
            throw new DomainException("La cancelación necesita quién la pide.");
        if (Status == OrderInvoiceStatus.Cancelling && CancellationMotive == motive)
            return;
        if (Status != OrderInvoiceStatus.Stamped)
            throw new DomainException(Status == OrderInvoiceStatus.Cancelled
                ? "Esta factura ya está cancelada."
                : "Solo se cancela una factura timbrada.");

        CancellationMotive = motive;
        CancellationRequestedByUserId = userId;
        CancellationRequestedAt = now;
        FailureMessage = null;
        Status = OrderInvoiceStatus.Cancelling;
    }

    /// <summary>The SAT cancelled it. Hearing it again changes nothing.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status == OrderInvoiceStatus.Cancelled)
            return;
        if (Status != OrderInvoiceStatus.Cancelling)
            throw new DomainException("Solo se cancela una factura cuya cancelación se pidió.");

        CancelledAt = now;
        Status = OrderInvoiceStatus.Cancelled;
        Released();
    }

    /// <summary>The receiver did not accept the cancellation, or the SAT refused it: it stays stamped, with the reason.</summary>
    public void KeepAfterRefusedCancellation(string? message)
    {
        if (Status != OrderInvoiceStatus.Cancelling)
            throw new DomainException("Esta factura no espera su cancelación.");

        FailureMessage = Trim(message, "El SAT no canceló la factura.");
        Status = OrderInvoiceStatus.Stamped;
    }

    /// <summary>Whether this kind of CFDI is cancelled with that motive.</summary>
    public abstract bool TakesCancellationMotive(CfdiCancellationMotive motive);

    /// <summary>What to say when the motive is not one this kind of CFDI takes.</summary>
    protected abstract string CancellationMotivesMessage { get; }

    /// <summary>It no longer stands with the SAT (refused or cancelled): what it covered is free to be stamped again.</summary>
    protected virtual void Released() { }

    /// <summary>A serie as the SAT takes it: letters and numbers, in capitals.</summary>
    /// <param name="serie">The serie as typed.</param>
    /// <param name="of">What it numbers, as its refusal names it: «las facturas» or «los complementos de pago».</param>
    internal static string NormalizeSerie(string? serie, string of = "las facturas")
    {
        var normalized = serie?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxSerieLength || !normalized.All(char.IsAsciiLetterOrDigit))
            throw new DomainException($"La serie de {of} tiene de 1 a {MaxSerieLength} letras o números.");
        return normalized;
    }

    /// <summary>An amount the CFDI carries as it is: never negative, in cents.</summary>
    protected static bool IsCents(decimal amount) => amount >= 0 && decimal.Round(amount, 2) == amount;

    private static string FileKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxFileKeyLength)
            throw new DomainException("El archivo de la factura no es válido.");
        return key;
    }

    /// <summary>A reason as it is kept: trimmed, the fallback when there is none, and never longer than the column.</summary>
    protected static string Trim(string? message, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();
        return text.Length > MaxFailureMessageLength ? text[..MaxFailureMessageLength] : text;
    }
}
