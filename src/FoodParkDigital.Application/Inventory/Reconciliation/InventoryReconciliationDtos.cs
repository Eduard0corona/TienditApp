using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Application.Inventory.Reconciliation;

/// <summary>How the stocks of the business compare with what their history adds up to (Fase 27C, ADR 0039).</summary>
public record InventoryReconciliationDto
{
    /// <summary>How many stocks were compared: every item with a stock or a movement in the location (or the business).</summary>
    public int CheckedCount { get; init; }
    /// <summary>Those whose stock is not what their history adds up to, by location and name.</summary>
    public PaginatedList<InventoryReconciliationItemDto> Mismatches { get; init; } = null!;
}

/// <summary>A stock that does not add up to its history.</summary>
public record InventoryReconciliationItemDto
{
    public Guid SupplyItemId { get; init; }
    public string SupplyItemName { get; init; } = string.Empty;
    public UnitType Unit { get; init; }
    public bool IsActive { get; init; }
    public Guid InventoryLocationId { get; init; }
    public string InventoryLocationName { get; init; } = string.Empty;
    /// <summary>What the stock says.</summary>
    public decimal OnHandQuantity { get; init; }
    /// <summary>What its movements add up to: what enters, minus what leaves.</summary>
    public decimal HistoryQuantity { get; init; }
    /// <summary>Stock minus history: positive when the stock has more than its history explains.</summary>
    public decimal Difference { get; init; }
    public int MovementCount { get; init; }
}

/// <summary>Where the history of one stock stops adding up to it, place by place.</summary>
public record InventoryReconciliationDetailDto
{
    public Guid SupplyItemId { get; init; }
    public string SupplyItemName { get; init; } = string.Empty;
    public UnitType Unit { get; init; }
    public bool IsActive { get; init; }
    public Guid InventoryLocationId { get; init; }
    public string InventoryLocationName { get; init; } = string.Empty;
    public decimal OnHandQuantity { get; init; }
    public decimal HistoryQuantity { get; init; }
    public decimal Difference { get; init; }
    public int MovementCount { get; init; }
    /// <summary>
    /// How many of its movement lines were walked: the most recent ones, up to
    /// <see cref="GetInventoryReconciliationDetailQueryHandler.MaxReviewedLines"/>.
    /// </summary>
    public int ReviewedCount { get; init; }
    /// <summary>How many places were found. The list brings the most recent ones.</summary>
    public int EventCount { get; init; }
    /// <summary>
    /// Each place where the stock changed without its history, and each reconciliation, oldest first. Their quantities
    /// add up to <see cref="Difference"/> when every place is listed.
    /// </summary>
    public List<InventoryReconciliationEventDto> Events { get; init; } = [];
}

/// <summary>
/// A place in the history of a stock: where it changed without a movement, a movement that wrote its balance wrong, or a
/// reconciliation that recorded what was missing.
/// </summary>
public record InventoryReconciliationEventDto
{
    public InventoryReconciliationEventKind Kind { get; init; }
    /// <summary>
    /// What it adds to the difference: positive when the stock gained what its history does not have. A reconciliation
    /// takes back what it recorded.
    /// </summary>
    public decimal Quantity { get; init; }
    /// <summary>What the stock went from and to there (both null when there is no history).</summary>
    public decimal? QuantityFrom { get; init; }
    public decimal? QuantityTo { get; init; }
    /// <summary>The movement where it shows: the later one between two, the first one before the history.</summary>
    public Guid? MovementId { get; init; }
    public string? MovementNumber { get; init; }
    public DateTimeOffset? MovementDate { get; init; }
    /// <summary>What that movement moved, with its sign (for a movement that wrote its balance wrong, or a reconciliation).</summary>
    public decimal? MovementQuantity { get; init; }
    /// <summary>The earlier movement, when it happened between two.</summary>
    public Guid? PreviousMovementId { get; init; }
    public string? PreviousMovementNumber { get; init; }
    public DateTimeOffset? PreviousMovementDate { get; init; }
}

public enum InventoryReconciliationEventKind
{
    /// <summary>The stock has a quantity and no movement at all.</summary>
    NoHistory = 1,
    /// <summary>The first movement found the stock with a quantity already.</summary>
    BeforeFirstMovement = 2,
    /// <summary>Between two movements the stock changed without one.</summary>
    BetweenMovements = 3,
    /// <summary>A movement wrote a balance that does not match what it moved.</summary>
    InMovement = 4,
    /// <summary>After the last movement the stock changed without one.</summary>
    AfterLastMovement = 5,
    /// <summary>A reconciliation recorded in the history what it was missing.</summary>
    Reconciled = 6,
    /// <summary>What comes from before the movements that were walked (long histories are walked from the most recent).</summary>
    BeforeReviewedHistory = 7
}

/// <summary>What a reconciliation recorded.</summary>
public record InventoryReconciliationResultDto
{
    /// <summary>How many items it recorded.</summary>
    public int ReconciledCount { get; init; }
    /// <summary>How many already added up (another reconciliation got there first, or a double tap).</summary>
    public int AlreadyAddedUpCount { get; init; }
    /// <summary>Its movements: up to two, what the history was missing and what it had too much of.</summary>
    public List<InventoryReconciliationMovementDto> Movements { get; init; } = [];
}

public record InventoryReconciliationMovementDto
{
    public Guid Id { get; init; }
    public string MovementNumber { get; init; } = string.Empty;
    public InventoryMovementType MovementType { get; init; }
}
