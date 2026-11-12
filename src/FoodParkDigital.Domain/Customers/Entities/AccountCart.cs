using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Customers.Entities;

/// <summary>
/// The cart a customer account leaves in a store (Fase 31J, ADR 0090), so it follows the customer to another device. It
/// belongs to the account, not to the business, like a favorite: the business does not see it, and
/// <see cref="BusinessId"/> is the store it is for. It keeps what was chosen, never a price: the menu prices it when the
/// store opens it again, and what the menu no longer has is left out there. A cart ordered or emptied stays, empty, for
/// <see cref="FreshDays"/>: another device that kept it, older, then forgets it instead of bringing it back.
/// </summary>
public class AccountCart : AuditableEntity, IAggregateRoot
{
    /// <summary>The carts one account keeps, the most recent ones (as a device), the emptied ones included.</summary>
    public const int MaxPerAccount = 10;
    /// <summary>A cart left longer than this is not offered again.</summary>
    public const int FreshDays = 7;
    // The same limits as the store and an order (PlacePublicOrderCommandValidator, CreateOrderItemDtoValidator).
    public const int MaxLines = 50;
    public const int MaxLineQuantity = 99;
    public const int MaxLineNotesLength = 300;
    /// <summary>Every option of every group of a product.</summary>
    public const int MaxOptionsPerLine = Product.MaxOptionGroups * ProductOptionGroup.MaxOptions;
    /// <summary>The options of all its lines: a cart is a few kilobytes, not what a line could take fifty times.</summary>
    public const int MaxOptionsPerCart = 500;

    public const string AccountRequiredMessage = "El carrito necesita una cuenta.";
    public const string BusinessRequiredMessage = "El carrito necesita un negocio.";
    public const string TooManyLinesMessage = "Un carrito lleva hasta 50 productos distintos.";
    public const string BadLineMessage = "Un producto del carrito no es válido.";
    public const string TooManyOptionsMessage = "Un carrito lleva hasta 500 opciones entre todos sus productos.";

    private List<AccountCartLine> _lines = [];

    private AccountCart() { }

    private AccountCart(Guid userId, Guid businessId)
    {
        UserId = userId;
        BusinessId = businessId;
    }

    public Guid UserId { get; private set; }

    /// <summary>The store the cart is for.</summary>
    public Guid BusinessId { get; private set; }

    public IReadOnlyList<AccountCartLine> Lines => _lines;

    /// <summary>Filled by «Pedir otra vez»: how many products the order had (the store words what did not come back).</summary>
    public int? ReorderOf { get; private set; }

    /// <summary>Filled by «Pedir otra vez»: the products sold by weight or in fractions, which the cart does not take.</summary>
    public int? Skipped { get; private set; }

    /// <summary>When the customer last changed it, on whatever device (or ordered or emptied it).</summary>
    public DateTimeOffset SavedAt { get; private set; }

    /// <summary>A new cart of the account in a store. One per store is the caller's to keep (the index says so too).</summary>
    public static AccountCart Create(
        Guid userId, Guid businessId, IEnumerable<AccountCartLine> lines, int? reorderOf, int? skipped, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            throw new DomainException(AccountRequiredMessage);
        if (businessId == Guid.Empty)
            throw new DomainException(BusinessRequiredMessage);

        var cart = new AccountCart(userId, businessId);
        cart.Replace(lines, reorderOf, skipped, now);
        return cart;
    }

    /// <summary>
    /// What the customer has in the cart now, all of it. The same product with the same options and the same note is one
    /// line (its quantities add up to the most a line takes), as in the store; a note is trimmed, and empty is none.
    /// </summary>
    public void Replace(IEnumerable<AccountCartLine> lines, int? reorderOf, int? skipped, DateTimeOffset now)
    {
        var kept = new List<AccountCartLine>();
        foreach (var line in lines)
        {
            var notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim();
            if (line.ProductId == Guid.Empty
                || line.Quantity is < 1 or > MaxLineQuantity
                || line.OptionIds.Count > MaxOptionsPerLine
                || line.OptionIds.Any(id => id == Guid.Empty)
                || notes is { Length: > MaxLineNotesLength })
                throw new DomainException(BadLineMessage);

            var options = line.OptionIds.Distinct().Order().ToList();
            var same = kept.FindIndex(existing => existing.ProductId == line.ProductId
                && existing.Notes == notes && existing.OptionIds.SequenceEqual(options));
            if (same >= 0)
                kept[same] = kept[same] with { Quantity = Math.Min(kept[same].Quantity + line.Quantity, MaxLineQuantity) };
            else
                kept.Add(new AccountCartLine(line.ProductId, options, line.Quantity, notes));
        }
        if (kept.Count > MaxLines)
            throw new DomainException(TooManyLinesMessage);
        if (kept.Sum(line => line.OptionIds.Count) > MaxOptionsPerCart)
            throw new DomainException(TooManyOptionsMessage);

        _lines = kept;
        ReorderOf = reorderOf is > 0 ? reorderOf : null;
        Skipped = skipped is > 0 ? skipped : null;
        SavedAt = now;
    }

    /// <summary>
    /// The cart was ordered or emptied, on whatever device: it stays, empty and with when that happened, so a device that
    /// kept it from before forgets it instead of sending it back.
    /// </summary>
    public void Empty(DateTimeOffset now) => Replace([], null, null, now);

    /// <summary>Changed (or ordered or emptied) within <see cref="FreshDays"/>: a device still compares its own with it.</summary>
    public bool IsRecent(DateTimeOffset now) => now - SavedAt < TimeSpan.FromDays(FreshDays);

    /// <summary>Whether it is still offered: changed within <see cref="FreshDays"/> and with something in it.</summary>
    public bool IsFresh(DateTimeOffset now) => _lines.Count > 0 && IsRecent(now);
}

/// <summary>A line of a cart: the product, its options (in order, each once), how many and the customer's note.</summary>
public sealed record AccountCartLine(Guid ProductId, IReadOnlyList<Guid> OptionIds, int Quantity, string? Notes)
{
    public bool Equals(AccountCartLine? other) => other is not null
        && ProductId == other.ProductId && Quantity == other.Quantity && Notes == other.Notes
        && OptionIds.SequenceEqual(other.OptionIds);

    public override int GetHashCode() => HashCode.Combine(ProductId, Quantity, Notes, OptionIds.Count);
}
