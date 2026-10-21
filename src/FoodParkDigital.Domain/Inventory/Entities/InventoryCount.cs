using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Domain.Inventory.Entities;

/// <summary>
/// A physical count of one location (Fase 27A, ADR 0037): what is on the shelf, item by item, against what the system
/// said when each item was counted. Posting it moves the stock by each difference with adjustments of reason
/// <see cref="InventoryMovementReason.InventoryCount"/>, so what was sold after an item was counted is kept; after that,
/// or once cancelled, it no longer changes. A location has one open count at a time: two would adjust twice.
/// </summary>
public class InventoryCount : AuditableEntity, ITenantScoped
{
    /// <summary>A count bigger than this is split by category.</summary>
    public const int MaxLines = 2_000;
    public const int MaxNotesLength = 500;
    public const int MaxCancelReasonLength = 300;
    public const int MaxCategoryNameLength = 120;
    public const int MaxUserNameLength = 256;

    public const string NotOpenMessage = "El conteo ya se aplicó o se canceló: ya no cambia.";
    public const string TooManyLinesMessage = "Un conteo tiene como máximo 2,000 artículos: divídelo por categoría.";
    public const string NothingCountedMessage = "Cuenta al menos un artículo antes de aplicar el conteo.";

    private InventoryCount() { }

    public InventoryCount(
        Guid tenantId,
        string countNumber,
        Guid inventoryLocationId,
        DateTimeOffset startedAt,
        string startedBy,
        string? categoryName = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(countNumber))
            throw new DomainException("El conteo necesita su folio.");
        if (inventoryLocationId == Guid.Empty)
            throw new DomainException("Elige la ubicación que vas a contar.");

        TenantId = tenantId;
        CountNumber = countNumber;
        InventoryLocationId = inventoryLocationId;
        Status = InventoryCountStatus.InProgress;
        CategoryName = Clip(categoryName, MaxCategoryNameLength);
        Notes = Clip(notes, MaxNotesLength);
        StartedAt = startedAt;
        StartedBy = User(startedBy);
    }

    public Guid TenantId { get; private set; }
    /// <summary>CNT-20261021-0001.</summary>
    public string CountNumber { get; private set; } = null!;
    public Guid InventoryLocationId { get; private set; }
    public InventoryCountStatus Status { get; private set; }
    /// <summary>The category the count started with, when it took only one.</summary>
    public string? CategoryName { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public string StartedBy { get; private set; } = null!;
    public DateTimeOffset? PostedAt { get; private set; }
    public string? PostedBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelledBy { get; private set; }
    public string? CancelReason { get; private set; }
    /// <summary>The adjustment that added what was found over the system; none without surplus.</summary>
    public Guid? IncreaseMovementId { get; private set; }
    /// <summary>The adjustment that took what was missing; none without shortage.</summary>
    public Guid? DecreaseMovementId { get; private set; }
    public int LineCount { get; private set; }
    public int CountedLineCount { get; private set; }
    /// <summary>Goes up with every item counted or added: posting asks for the one that was reviewed.</summary>
    public int Revision { get; private set; }

    public bool IsOpen => Status == InventoryCountStatus.InProgress;

    /// <summary>An item to count. The caller makes sure the count does not have it yet.</summary>
    public InventoryCountLine AddLine(Guid supplyItemId)
    {
        EnsureOpen();
        if (LineCount >= MaxLines)
            throw new DomainException(TooManyLinesMessage);

        LineCount++;
        Revision++;
        return new InventoryCountLine(TenantId, Id, supplyItemId);
    }

    /// <summary>
    /// What was counted of an item, with what the system had of it at that moment; null leaves it uncounted. Changing the
    /// quantity of a counted item corrects it: the system quantity stays the one it was first counted against. Counting it
    /// again from scratch is clearing it and counting it, which takes the system of then.
    /// </summary>
    public void Record(
        InventoryCountLine line,
        decimal? countedQuantity,
        decimal systemQuantity,
        UnitType unit,
        DateTimeOffset countedAt,
        string countedBy,
        string? notes = null)
    {
        EnsureOpen();
        if (line.CountId != Id)
            throw new DomainException("El artículo no es de este conteo.");
        if (countedQuantity is { } quantity && !IsValidCountedQuantity(quantity, unit))
            throw new DomainException(CountedQuantityMessage(unit));

        var wasCounted = line.IsCounted;
        line.Record(countedQuantity, systemQuantity, countedAt, User(countedBy), Clip(notes, InventoryCountLine.MaxNotesLength));
        CountedLineCount += (line.IsCounted ? 1 : 0) - (wasCounted ? 1 : 0);
        Revision++;
    }

    /// <summary>The differences moved the stock with these adjustments (none of a kind without differences of it).</summary>
    public void Post(DateTimeOffset postedAt, string postedBy, Guid? increaseMovementId, Guid? decreaseMovementId)
    {
        EnsureOpen();
        if (CountedLineCount == 0)
            throw new DomainException(NothingCountedMessage);

        Status = InventoryCountStatus.Posted;
        PostedAt = postedAt;
        PostedBy = User(postedBy);
        IncreaseMovementId = increaseMovementId;
        DecreaseMovementId = decreaseMovementId;
    }

    public void Cancel(DateTimeOffset cancelledAt, string cancelledBy, string? reason = null)
    {
        EnsureOpen();

        Status = InventoryCountStatus.Cancelled;
        CancelledAt = cancelledAt;
        CancelledBy = User(cancelledBy);
        CancelReason = Clip(reason, MaxCancelReasonLength);
    }

    /// <summary>Zero or more, with at most three decimals, whole for units that are not measured (pieces, packs).</summary>
    public static bool IsValidCountedQuantity(decimal quantity, UnitType unit)
        => quantity == 0 || (quantity <= InventoryCountLine.MaxQuantity && UnitTypeRules.IsValidQuantity(quantity, unit));

    public static string CountedQuantityMessage(UnitType unit) => UnitTypeRules.AllowsFractions(unit)
        ? "La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales."
        : "La cantidad contada va de 0 a 9,999,999, en piezas enteras.";

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new DomainException(NotOpenMessage);
    }

    private static string User(string user)
    {
        if (string.IsNullOrWhiteSpace(user))
            throw new DomainException("Falta quién hizo el cambio.");
        var trimmed = user.Trim();
        return trimmed.Length > MaxUserNameLength ? trimmed[..MaxUserNameLength] : trimmed;
    }

    internal static string? Clip(string? text, int max)
    {
        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return trimmed is { Length: var length } && length > max ? trimmed[..max] : trimmed;
    }
}
