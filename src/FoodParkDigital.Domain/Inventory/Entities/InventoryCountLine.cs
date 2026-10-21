using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Inventory.Entities;

/// <summary>
/// One item of a physical count (Fase 27A, ADR 0037): what was counted and what the system had of it in the count's
/// location at that moment. It changes only through its <see cref="InventoryCount"/>, while the count is open.
/// </summary>
public class InventoryCountLine : AuditableEntity, ITenantScoped
{
    public const int MaxNotesLength = 300;
    public const decimal MaxQuantity = 9_999_999m;

    private InventoryCountLine() { }

    internal InventoryCountLine(Guid tenantId, Guid countId, Guid supplyItemId)
    {
        TenantId = tenantId;
        CountId = countId;
        SupplyItemId = supplyItemId;
    }

    public Guid TenantId { get; private set; }
    public Guid CountId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    /// <summary>What is on the shelf; null while it has not been counted.</summary>
    public decimal? CountedQuantity { get; private set; }
    /// <summary>What the system had in the location when it was counted; null while it has not been counted.</summary>
    public decimal? SystemQuantity { get; private set; }
    public DateTimeOffset? CountedAt { get; private set; }
    public string? CountedBy { get; private set; }
    public string? Notes { get; private set; }

    public bool IsCounted => CountedQuantity.HasValue;

    /// <summary>Counted minus what the system had: what posting adds (positive) or takes (negative).</summary>
    public decimal? Difference => CountedQuantity - SystemQuantity;

    /// <summary>
    /// What the stock ends at when the count is posted: the stock then plus the difference, so what moved after the item
    /// was counted is kept.
    /// </summary>
    public decimal? ResultingQuantity(decimal currentOnHand) => Difference is { } difference ? currentOnHand + difference : null;

    internal void Record(decimal? countedQuantity, decimal systemQuantity, DateTimeOffset countedAt, string countedBy, string? notes)
    {
        Notes = notes;
        if (countedQuantity == CountedQuantity)
            return;

        if (countedQuantity is null)
        {
            CountedQuantity = null;
            SystemQuantity = null;
            CountedAt = null;
            CountedBy = null;
            return;
        }

        CountedQuantity = countedQuantity;
        SystemQuantity = systemQuantity;
        CountedAt = countedAt;
        CountedBy = countedBy;
    }
}
