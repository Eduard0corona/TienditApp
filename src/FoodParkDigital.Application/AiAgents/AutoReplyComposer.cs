namespace FoodParkDigital.Application.AiAgents;

/// <summary>
/// Puts an automatic reply together within its size. The parts the system writes (the hours, the menu link, the
/// questions for the customer's data) are never cut: the texts the business wrote give way instead, in their order, so
/// a long welcome or products text no longer cuts the link at the end.
/// </summary>
internal sealed class AutoReplyComposer
{
    /// <summary>The longest automatic reply.</summary>
    public const int MaxLength = 1300;

    // A text of the business cut with fewer characters than this left reads as noise: it is left out.
    private const int MinCutLength = 20;
    private const string Ellipsis = "...";

    private readonly List<(string Text, bool ByOwner)> _parts = [];

    /// <summary>A text the business wrote; blank ones are skipped.</summary>
    public AutoReplyComposer Owner(string? text) => Add(text, byOwner: true);

    /// <summary>A text the system writes; blank ones are skipped.</summary>
    public AutoReplyComposer System(string? text) => Add(text, byOwner: false);

    /// <summary>
    /// The parts in their order, with the room the system's leave going to the business's texts: the first one that
    /// does not fit is cut (or left out when too little room remains), and the ones after it are left out.
    /// </summary>
    public string Compose(int max = MaxLength, string separator = "\n\n")
    {
        var system = _parts.Where(part => !part.ByOwner).Select(part => part.Text).ToList();
        var kept = system.Count;
        var used = system.Sum(text => text.Length) + separator.Length * Math.Max(kept - 1, 0);
        var owner = new Dictionary<int, string>();
        for (var index = 0; index < _parts.Count; index++)
        {
            if (!_parts[index].ByOwner)
                continue;
            var text = _parts[index].Text;
            var joint = kept == 0 ? 0 : separator.Length;
            var room = max - used - joint;
            if (text.Length > room)
            {
                if (room >= MinCutLength)
                    owner[index] = $"{text[..(room - Ellipsis.Length)].TrimEnd()}{Ellipsis}";
                break;
            }
            owner[index] = text;
            used += joint + text.Length;
            kept++;
        }

        return string.Join(separator, _parts
            .Select((part, index) => part.ByOwner ? owner.GetValueOrDefault(index) : part.Text)
            .OfType<string>());
    }

    private AutoReplyComposer Add(string? text, bool byOwner)
    {
        if (!string.IsNullOrWhiteSpace(text))
            _parts.Add((text.Trim(), byOwner));
        return this;
    }
}
