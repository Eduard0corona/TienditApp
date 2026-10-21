using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Application.Inventory.Counts;

/// <summary>A physical count in the list of counts (Fase 27A, ADR 0037).</summary>
public class InventoryCountListItemDto
{
    public Guid Id { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public Guid InventoryLocationId { get; set; }
    public string InventoryLocationName { get; set; } = string.Empty;
    public InventoryCountStatus Status { get; set; }
    public string? CategoryName { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string StartedBy { get; set; } = string.Empty;
    public DateTimeOffset? PostedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public int LineCount { get; set; }
    public int CountedLineCount { get; set; }
}

/// <summary>A physical count with what its differences are worth (Fase 27A, ADR 0037).</summary>
public class InventoryCountDto
{
    public Guid Id { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public Guid InventoryLocationId { get; set; }
    public string InventoryLocationName { get; set; } = string.Empty;
    public InventoryCountStatus Status { get; set; }
    public string? CategoryName { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string StartedBy { get; set; } = string.Empty;
    public DateTimeOffset? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
    public int LineCount { get; set; }
    public int CountedLineCount { get; set; }
    /// <summary>Counted items whose quantity is not what the system had when they were counted.</summary>
    public int DifferenceLineCount { get; set; }
    /// <summary>What was found over the system: at the items' cost while open, what its adjustment added once posted; null if cancelled.</summary>
    public decimal? SurplusValue { get; set; }
    /// <summary>What was missing: at the items' cost while open, what its adjustment took once posted; null if cancelled.</summary>
    public decimal? ShortageValue { get; set; }
    /// <summary>Sent back to post it: posting a count that changed after it was reviewed is refused.</summary>
    public int Revision { get; set; }
    public Guid? IncreaseMovementId { get; set; }
    public string? IncreaseMovementNumber { get; set; }
    public Guid? DecreaseMovementId { get; set; }
    public string? DecreaseMovementNumber { get; set; }
}

/// <summary>An item of a physical count (Fase 27A, ADR 0037).</summary>
public class InventoryCountLineDto
{
    public Guid Id { get; set; }
    public Guid SupplyItemId { get; set; }
    public string SupplyItemName { get; set; } = string.Empty;
    public string? SupplyCode { get; set; }
    public string? SupplySku { get; set; }
    public string? SupplyBarcode { get; set; }
    public string? CategoryName { get; set; }
    public UnitType Unit { get; set; }
    public bool IsSupplyActive { get; set; }
    /// <summary>What is on the shelf; null while it has not been counted.</summary>
    public decimal? CountedQuantity { get; set; }
    /// <summary>What the system had in the location when it was counted.</summary>
    public decimal? SystemQuantity { get; set; }
    /// <summary>Counted minus what the system had.</summary>
    public decimal? Difference { get; set; }
    /// <summary>
    /// While open, what the location has now; once posted, what it had when the count moved it (only items with a
    /// difference). Null for a cancelled count.
    /// </summary>
    public decimal? CurrentQuantity { get; set; }
    /// <summary>What the stock ends at (open) or ended at (posted): the stock then plus the difference.</summary>
    public decimal? ResultingQuantity { get; set; }
    /// <summary>The difference at the item's cost (open) or what its adjustment was worth (posted); negative when missing.</summary>
    public decimal? DifferenceValue { get; set; }
    public DateTimeOffset? CountedAt { get; set; }
    public string? CountedBy { get; set; }
    public string? Notes { get; set; }
}

/// <summary>What recording quantities changed: the count and the items that were saved.</summary>
public class RecordedInventoryCountDto
{
    public InventoryCountDto Count { get; set; } = new();
    public List<InventoryCountLineDto> Lines { get; set; } = [];
}
