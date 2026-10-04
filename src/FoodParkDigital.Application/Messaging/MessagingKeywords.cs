using System.Globalization;
using System.Text;

namespace FoodParkDigital.Application.Messaging;

public enum MessagingKeyword
{
    None = 0,
    OptOut = 1,
    OptIn = 2
}

/// <summary>
/// BAJA / ALTA written by the customer. Only a message that is just the keyword counts, so "quiero cancelar mi
/// pedido" or "dame de baja el pedido" never unsubscribe anyone.
/// </summary>
public static class MessagingKeywords
{
    private static readonly HashSet<string> OptOutWords = new(StringComparer.Ordinal)
    {
        "baja", "stop", "alto", "darme de baja", "dar de baja", "no mas mensajes", "no mas promociones"
    };

    private static readonly HashSet<string> OptInWords = new(StringComparer.Ordinal)
    {
        "alta", "start", "darme de alta", "dar de alta"
    };

    public static MessagingKeyword Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 40)
            return MessagingKeyword.None;

        var normalized = Normalize(body);
        if (OptOutWords.Contains(normalized))
            return MessagingKeyword.OptOut;
        if (OptInWords.Contains(normalized))
            return MessagingKeyword.OptIn;
        return MessagingKeyword.None;
    }

    private static string Normalize(string body)
    {
        var decomposed = body.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetter(character) || char.IsWhiteSpace(character))
                builder.Append(character);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
