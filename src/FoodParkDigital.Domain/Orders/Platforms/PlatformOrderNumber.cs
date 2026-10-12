using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// The folio a delivery platform's app shows for an order (Rappi's number, Uber Eats' 5 characters, DiDi's pickup
/// or order number), normalized so the same folio is always written the same way: outer spaces and one leading
/// <c>#</c> are dropped, inner spaces removed and letters upper-cased. Only A to Z, digits and hyphens are kept,
/// up to <see cref="MaxLength"/>, which fits DiDi's long id (about 19 digits).
/// </summary>
public sealed partial record PlatformOrderNumber
{
    public const int MaxLength = 30;
    /// <summary>What is read before normalizing: spaces may double a folio, but not much more.</summary>
    public const int MaxInputLength = 60;

    public const string RequiredMessage = "Escribe el folio del pedido que aparece en la app de la plataforma.";
    public const string InvalidMessage = "El folio de la app lleva de 1 a 30 letras, números o guiones.";

    [GeneratedRegex("^[A-Z0-9-]{1,30}$")]
    private static partial Regex Pattern();

    private PlatformOrderNumber(string value) => Value = value;

    public string Value { get; }

    /// <summary>The normalized folio, or a <see cref="DomainException"/> when it is empty or has other characters.</summary>
    public static PlatformOrderNumber From(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException(RequiredMessage);

        return TryNormalize(raw, out var value) ? new PlatformOrderNumber(value) : throw new DomainException(InvalidMessage);
    }

    /// <summary>Normalizes a folio as typed: false when nothing valid is left.</summary>
    public static bool TryNormalize(string? raw, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxInputLength)
            return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..];

        var normalized = WithoutSpaces(trimmed, dropHashes: false).ToUpperInvariant();
        if (!Pattern().IsMatch(normalized))
            return false;

        value = normalized;
        return true;
    }

    /// <summary>
    /// A search term written as a folio would be stored (no <c>#</c> or spaces, upper case), so a search for
    /// "#bc 953" finds BC953; empty when nothing valid is left, so a name or a phone does not search folios.
    /// </summary>
    public static string NormalizeForSearch(string? term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length > MaxInputLength)
            return string.Empty;

        var normalized = WithoutSpaces(term, dropHashes: true).ToUpperInvariant();
        return Pattern().IsMatch(normalized) ? normalized : string.Empty;
    }

    public override string ToString() => Value;

    private static string WithoutSpaces(string text, bool dropHashes)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || (dropHashes && character == '#'))
                continue;
            builder.Append(character);
        }

        return builder.ToString();
    }
}
