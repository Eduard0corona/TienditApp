using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// The long id a delivery platform gives an order, besides the short folio its app shows (Fase 22I, ADR 0041): Uber
/// Eats' UUID, DiDi's id of about 19 digits, Rappi's order number. Its statements use it, and it does not repeat as the
/// short folio does. Normalized as the folio (<see cref="PlatformOrderNumber"/>): outer spaces and one leading <c>#</c>
/// are dropped, inner spaces removed and letters upper-cased. Only A to Z, digits and hyphens are kept, from
/// <see cref="MinLength"/> to <see cref="MaxLength"/>: at least 6, so Uber Eats' 5 characters typed in the wrong box are
/// refused.
/// </summary>
public sealed partial record PlatformOrderId
{
    public const int MinLength = 6;
    public const int MaxLength = 64;
    /// <summary>What is read before normalizing: spaces may separate the groups of a long id.</summary>
    public const int MaxInputLength = 100;

    public const string InvalidMessage = "El ID largo del pedido lleva de 6 a 64 letras, números o guiones.";

    [GeneratedRegex("^[A-Z0-9-]{6,64}$")]
    private static partial Regex Pattern();

    private PlatformOrderId(string value) => Value = value;

    public string Value { get; }

    /// <summary>The normalized id; null when the box is empty; a <see cref="DomainException"/> when it has other characters.</summary>
    public static PlatformOrderId? FromOptional(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return TryNormalize(raw, out var value) ? new PlatformOrderId(value) : throw new DomainException(InvalidMessage);
    }

    /// <summary>Normalizes an id as typed: false when nothing valid is left.</summary>
    public static bool TryNormalize(string? raw, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxInputLength)
            return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..];

        var builder = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed)
        {
            if (!char.IsWhiteSpace(character))
                builder.Append(char.ToUpperInvariant(character));
        }

        var normalized = builder.ToString();
        if (!Pattern().IsMatch(normalized))
            return false;

        value = normalized;
        return true;
    }

    /// <summary>
    /// A search term written as the stored long ids are, so a part of one finds it (the last digits of DiDi's id): empty
    /// when the term cannot be part of a long id.
    /// </summary>
    public static string NormalizeForSearch(string? term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length > MaxInputLength)
            return string.Empty;

        var builder = new StringBuilder(term.Length);
        foreach (var character in term)
        {
            if (!char.IsWhiteSpace(character) && character != '#')
                builder.Append(char.ToUpperInvariant(character));
        }

        var normalized = builder.ToString();
        return SearchPattern().IsMatch(normalized) ? normalized : string.Empty;
    }

    [GeneratedRegex("^[A-Z0-9-]{1,64}$")]
    private static partial Regex SearchPattern();

    public override string ToString() => Value;
}
