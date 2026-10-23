using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// The CFDI 4.0 invoice (ingreso) of an order to a customer with an RFC, stamped through the platform's PAC with the
/// business's CSD (Fase 28A, ADR 0045). Its folio, date and idempotency key are fixed and saved before the PAC is asked,
/// so asking again after a lost answer never stamps it twice. Its amounts are the ones the backend computed for the
/// order's CFDI draft. It is never deleted: a cancelled one stays, and the order can be invoiced again.
/// </summary>
public class OrderInvoice : AuditableEntity, IAggregateRoot, ITenantScoped
{
    public const string DefaultSerie = "F";
    public const int MaxSerieLength = 10;
    public const int UuidLength = 36;
    public const int MaxProviderInvoiceIdLength = 64;
    public const int MaxFailureMessageLength = 500;
    public const int MaxFileKeyLength = 300;
    public const string PaidInOneExhibition = "PUE";

    private OrderInvoice() { }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }

    public string Serie { get; private set; } = null!;
    public long Folio { get; private set; }

    /// <summary>The date the CFDI carries, fixed when it was asked for so that asking again sends the same invoice.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>The key the PAC knows the request by: the same one on every attempt.</summary>
    public Guid IdempotencyKey { get; private set; }

    public OrderInvoiceStatus Status { get; private set; }
    public CfdiReceiver Receiver { get; private set; } = null!;

    /// <summary>c_FormaPago of the sale (the method that paid the most).</summary>
    public string PaymentForm { get; private set; } = null!;

    /// <summary>c_MetodoPago: PUE, the sale is paid in full.</summary>
    public string PaymentMethod { get; private set; } = null!;

    /// <summary>
    /// The CFDI as it was asked for (concepts, taxes and totals of the order's draft), written by the application: every
    /// attempt sends exactly this, even if the order changes afterwards.
    /// </summary>
    public string Document { get; private set; } = null!;

    public decimal SubTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }

    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The folio fiscal the SAT gave it.</summary>
    public string? Uuid { get; private set; }

    /// <summary>The PAC's id of the invoice: its PDF, its XML and its cancellation are asked with it.</summary>
    public string? ProviderInvoiceId { get; private set; }

    public DateTimeOffset? StampedAt { get; private set; }

    /// <summary>The stamped XML, in the business's private files.</summary>
    public string? XmlFileKey { get; private set; }

    /// <summary>Its PDF, in the business's private files (kept apart: the PAC may give it later than the XML).</summary>
    public string? PdfFileKey { get; private set; }

    /// <summary>Why the PAC refused it, or why the receiver did not accept its cancellation.</summary>
    public string? FailureMessage { get; private set; }

    public DateTimeOffset? RefusedAt { get; private set; }

    public CfdiCancellationMotive? CancellationMotive { get; private set; }
    public Guid? CancellationRequestedByUserId { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>The PAC's answer and the business change it: a change on a stale copy is a conflict (409).</summary>
    public byte[] Version { get; private set; } = null!;

    /// <summary>Whether it stands for the order with the SAT, or may: an order has at most one.</summary>
    public bool IsLive => Status is OrderInvoiceStatus.Stamping or OrderInvoiceStatus.Stamped or OrderInvoiceStatus.Cancelling;

    /// <summary>The folio as the business reads it: «F-123».</summary>
    public string Number => $"{Serie}-{Folio}";

    /// <summary>
    /// The invoice of a paid order, with its folio and the amounts of its CFDI draft, before the PAC is asked: what is saved
    /// here is what every attempt sends.
    /// </summary>
    public static OrderInvoice Request(
        Guid tenantId,
        Guid orderId,
        string serie,
        long folio,
        CfdiReceiver receiver,
        string paymentForm,
        string document,
        decimal subTotal,
        decimal taxTotal,
        decimal total,
        Guid requestedByUserId,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || orderId == Guid.Empty || requestedByUserId == Guid.Empty)
            throw new DomainException("La factura necesita su negocio, su pedido y quién la pide.");
        var normalizedSerie = NormalizeSerie(serie);
        if (folio <= 0)
            throw new DomainException("El folio de la factura es un número positivo.");
        ArgumentNullException.ThrowIfNull(receiver);
        if (string.IsNullOrWhiteSpace(paymentForm) || paymentForm.Length != 2 || paymentForm == SatCatalog.PaymentFormToBeDefined)
            throw new DomainException("Una venta pagada se factura con la forma en que se pagó.");
        if (string.IsNullOrWhiteSpace(document))
            throw new DomainException("La factura necesita sus conceptos.");
        if (subTotal < 0 || taxTotal < 0 || total <= 0 || subTotal + taxTotal != total
            || decimal.Round(total, 2) != total || decimal.Round(subTotal, 2) != subTotal)
            throw new DomainException("Los importes de la factura no cuadran.");

        return new OrderInvoice
        {
            TenantId = tenantId,
            OrderId = orderId,
            Serie = normalizedSerie,
            Folio = folio,
            IssuedAt = now,
            IdempotencyKey = Guid.NewGuid(),
            Status = OrderInvoiceStatus.Stamping,
            Receiver = receiver,
            PaymentForm = paymentForm,
            PaymentMethod = PaidInOneExhibition,
            Document = document,
            SubTotal = subTotal,
            TaxTotal = taxTotal,
            Total = total,
            RequestedByUserId = requestedByUserId,
            RequestedAt = now
        };
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

    /// <summary>The PAC refused it for good: the order can be invoiced again (with another folio) once the data is fixed.</summary>
    public void Refuse(string? message, DateTimeOffset now)
    {
        if (Status != OrderInvoiceStatus.Stamping)
            throw new DomainException("Esta factura ya no espera su timbre.");

        FailureMessage = Trim(message, "El PAC no timbró la factura.");
        RefusedAt = now;
        Status = OrderInvoiceStatus.Refused;
    }

    /// <summary>
    /// The owner or an administrator asks the SAT to cancel it: because it has errors and nothing replaces it, or because
    /// the sale did not happen.
    /// </summary>
    public void RequestCancellation(CfdiCancellationMotive motive, Guid userId, DateTimeOffset now)
    {
        if (motive is not (CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut))
            throw new DomainException("Cancela por errores sin relación (02) o porque la venta no se llevó a cabo (03).");
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
    }

    /// <summary>The receiver did not accept the cancellation, or the SAT refused it: it stays stamped, with the reason.</summary>
    public void KeepAfterRefusedCancellation(string? message)
    {
        if (Status != OrderInvoiceStatus.Cancelling)
            throw new DomainException("Esta factura no espera su cancelación.");

        FailureMessage = Trim(message, "El SAT no canceló la factura.");
        Status = OrderInvoiceStatus.Stamped;
    }

    /// <summary>A serie as the SAT takes it: letters and numbers, in capitals.</summary>
    internal static string NormalizeSerie(string? serie)
    {
        var normalized = serie?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxSerieLength || !normalized.All(char.IsAsciiLetterOrDigit))
            throw new DomainException($"La serie de las facturas tiene de 1 a {MaxSerieLength} letras o números.");
        return normalized;
    }

    private static string FileKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxFileKeyLength)
            throw new DomainException("El archivo de la factura no es válido.");
        return key;
    }

    private static string Trim(string? message, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();
        return text.Length > MaxFailureMessageLength ? text[..MaxFailureMessageLength] : text;
    }
}
