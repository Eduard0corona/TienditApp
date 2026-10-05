using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Tenants.Entities;

/// <summary>
/// A folio counter of one business: its orders ("ORD"), and per day its inventory movements ("INV-20261005") and
/// purchase orders ("PO-20261005"). Every document takes the next number, also when two users save at the same time:
/// counting the stored documents gave both the same folio.
/// </summary>
public class FolioSequence : ITenantScoped
{
    public const int MaxScopeLength = 40;

    private FolioSequence() { }

    public FolioSequence(Guid tenantId, string scope, long lastNumber)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("El contador de folios es de un negocio.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > MaxScopeLength)
            throw new ArgumentException("El contador necesita un nombre de hasta 40 caracteres.", nameof(scope));
        if (lastNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(lastNumber), "El último folio no puede ser negativo.");

        TenantId = tenantId;
        Scope = scope;
        LastNumber = lastNumber;
    }

    public Guid TenantId { get; private set; }

    /// <summary>What the counter numbers: "ORD", or a prefix and its day ("INV-20261005").</summary>
    public string Scope { get; private set; } = string.Empty;

    /// <summary>The last number given; the next document takes the following one.</summary>
    public long LastNumber { get; private set; }

    /// <summary>Takes the next number.</summary>
    public long Next() => ++LastNumber;
}
