using FoodParkDigital.Domain.Catalog.Enums;

namespace FoodParkDigital.Application.Inventory.Transfers;

/// <summary>A transfer in the list of transfers (Fase 27B, ADR 0038).</summary>
public class InventoryTransferListItemDto
{
    public Guid Id { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public Guid FromLocationId { get; set; }
    public string FromLocationName { get; set; } = string.Empty;
    public Guid ToLocationId { get; set; }
    public string ToLocationName { get; set; } = string.Empty;
    public DateTimeOffset TransferredAt { get; set; }
    public string TransferredBy { get; set; } = string.Empty;
    public int LineCount { get; set; }
    /// <summary>What the items were worth: the same left the origin and entered the destination.</summary>
    public decimal TotalCost { get; set; }
}

/// <summary>A transfer with each item and its two movements (Fase 27B, ADR 0038).</summary>
public class InventoryTransferDto : InventoryTransferListItemDto
{
    public string? Notes { get; set; }
    public Guid OutMovementId { get; set; }
    public string OutMovementNumber { get; set; } = string.Empty;
    public Guid InMovementId { get; set; }
    public string InMovementNumber { get; set; } = string.Empty;
    public List<InventoryTransferLineDto> Lines { get; set; } = [];
}

/// <summary>What moved of one item, and what each location had before and after.</summary>
public class InventoryTransferLineDto
{
    public Guid SupplyItemId { get; set; }
    public string SupplyItemName { get; set; } = string.Empty;
    public UnitType Unit { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>The same in both movements; null when the item has no cost to value it with.</summary>
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public decimal FromQuantityBefore { get; set; }
    public decimal FromQuantityAfter { get; set; }
    public decimal ToQuantityBefore { get; set; }
    public decimal ToQuantityAfter { get; set; }
}
