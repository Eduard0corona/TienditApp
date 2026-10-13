using System.Globalization;
using System.Text;

namespace FoodParkDigital.Application.AiAgents;

/// <summary>
/// How the automatic replies recognize a keyword: whole words, ignoring case and accents. "menú" matches "menu",
/// while "hi" no longer matches inside "chilaquiles".
/// </summary>
public static class AutoReplyKeywords
{
    public static IReadOnlyList<string> Words(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The phrase ("buenas tardes") appears as consecutive whole words.</summary>
    public static bool Contains(IReadOnlyList<string> words, string phrase)
    {
        var target = Words(phrase);
        if (target.Count == 0 || target.Count > words.Count)
            return false;

        for (var start = 0; start <= words.Count - target.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < target.Count && matches; offset++)
                matches = words[start + offset] == target[offset];
            if (matches)
                return true;
        }

        return false;
    }

    public static bool ContainsAny(IReadOnlyList<string> words, IEnumerable<string> phrases)
        => phrases.Any(phrase => Contains(words, phrase));

    /// <summary>
    /// The words without each appearance of <paramref name="phrases"/>, so a phrase that holds a keyword of another kind
    /// does not count as it: «cuánto cuesta el envío» is not a question about the prices of the menu.
    /// </summary>
    public static IReadOnlyList<string> Without(IReadOnlyList<string> words, IEnumerable<string> phrases)
    {
        var kept = new bool[words.Count];
        Array.Fill(kept, true);
        foreach (var target in phrases.Select(Words).Where(target => target.Count > 0))
        {
            for (var start = 0; start <= words.Count - target.Count; start++)
            {
                if (Enumerable.Range(0, target.Count).All(offset => words[start + offset] == target[offset]))
                    Array.Fill(kept, false, start, target.Count);
            }
        }

        return words.Where((_, index) => kept[index]).ToList();
    }

    /// <summary>Every word of the message is one of <paramref name="allowed"/> ("ok, gracias").</summary>
    public static bool OnlyWordsFrom(IReadOnlyList<string> words, IReadOnlySet<string> allowed)
        => words.Count > 0 && words.All(allowed.Contains);
}
