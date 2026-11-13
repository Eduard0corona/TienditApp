using System.Globalization;
using System.Text;

namespace FoodParkDigital.Infrastructure.Documents;

/// <summary>The two faces of TienditApp's PDF: Helvetica and its bold, two of the 14 fonts every PDF reader has.</summary>
internal enum PdfFont
{
    Regular,
    Bold
}

/// <summary>
/// Text in the PDF's two fonts (Fase 31M, ADR 0093): written in WinAnsiEncoding, so Spanish (accents, ñ, ¿, ¡) needs no
/// embedded font, and measured with the fonts' own widths to align and wrap it. What the encoding does not have becomes the
/// nearest letter it has (ő is o), a space or a question mark; an emoji is left out.
/// </summary>
internal static class PdfText
{
    // The widths of the characters 32 to 255 of WinAnsiEncoding, in thousandths of the font size, from Adobe's Core 14 AFM
    // files of Helvetica and Helvetica-Bold (Copyright (c) 1985, 1987, 1989, 1990, 1997 Adobe Systems Incorporated), which
    // Adobe lets anyone use, copy and distribute for any purpose and without charge.
    private static readonly short[] RegularWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 350,
        556, 350, 222, 556, 333, 1000, 556, 556, 333, 1000, 667, 333, 1000, 350, 611, 350,
        350, 222, 222, 333, 333, 350, 556, 1000, 333, 1000, 500, 333, 944, 350, 500, 667,
        278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333,
        400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,
        667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
        556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500
    ];

    private static readonly short[] BoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584, 350,
        556, 350, 278, 556, 500, 1000, 556, 556, 333, 1000, 667, 333, 1000, 350, 611, 350,
        350, 278, 278, 500, 500, 350, 556, 1000, 333, 1000, 556, 333, 944, 350, 500, 667,
        278, 333, 556, 556, 556, 556, 280, 556, 333, 737, 370, 556, 584, 333, 737, 333,
        400, 584, 333, 333, 333, 611, 556, 278, 333, 333, 365, 556, 834, 834, 834, 611,
        722, 722, 722, 722, 722, 722, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
        556, 556, 556, 556, 556, 556, 889, 556, 556, 556, 556, 556, 278, 278, 278, 278,
        611, 611, 611, 611, 611, 611, 611, 584, 611, 611, 611, 611, 611, 556, 611, 556
    ];

    // The characters WinAnsiEncoding keeps between 128 and 159, where Latin-1 has control codes.
    private static readonly Dictionary<char, byte> Windows1252 = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88,
        ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93,
        ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B,
        ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
        // Not in the encoding, but drawn the same by its nearest character.
        ['−'] = 0x96, ['‐'] = (byte)'-', ['‑'] = (byte)'-', ['′'] = (byte)'\'', ['″'] = (byte)'"',
        // Letters with a stroke or without a dot, which do not decompose into a letter and a mark.
        ['ł'] = (byte)'l', ['Ł'] = (byte)'L', ['ı'] = (byte)'i', ['đ'] = (byte)'d', ['Đ'] = (byte)'D', ['ħ'] = (byte)'h',
        ['Ħ'] = (byte)'H', ['ŧ'] = (byte)'t', ['Ŧ'] = (byte)'T'
    };

    /// <summary>The mark of a text cut short.</summary>
    public const string Ellipsis = "…";

    private const byte Unknown = (byte)'?';

    /// <summary>
    /// <paramref name="text"/> in WinAnsiEncoding, on one line: line breaks, tabs and other spaces are a space, invisible
    /// characters, emoji and other symbols are left out (with the space they leave doubled, once), a letter with a mark the
    /// encoding lacks is the letter without it, and anything else (another alphabet) is a question mark.
    /// </summary>
    public static byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length);
        var dropped = false;
        var normalized = text.Normalize(NormalizationForm.FormC);
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (char.IsHighSurrogate(c) && i + 1 < normalized.Length && char.IsLowSurrogate(normalized[i + 1]))
            {
                // Outside the basic plane: an emoji or a script these fonts never had.
                i++;
                dropped = true;
                continue;
            }
            if (TryEncode(c, out var code))
            {
                bytes.Add(code);
                continue;
            }
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                bytes.Add((byte)' ');
                continue;
            }
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark or UnicodeCategory.Surrogate)
                continue;
            var nearest = Nearest(c);
            if (nearest.Count > 0)
                bytes.AddRange(nearest);
            else if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.OtherSymbol or UnicodeCategory.MathSymbol)
                // ✅ ❤ ★ ☎: a symbol these fonts lack says nothing missing, unlike a letter.
                dropped = true;
            else
                bytes.Add(Unknown);
        }
        if (!dropped)
            return bytes.ToArray();
        // What was left out leaves the spaces around it together: one, and none at either end.
        var joined = new List<byte>(bytes.Count);
        foreach (var code in bytes)
            if (code != (byte)' ' || (joined.Count > 0 && joined[^1] != (byte)' '))
                joined.Add(code);
        if (joined.Count > 0 && joined[^1] == (byte)' ')
            joined.RemoveAt(joined.Count - 1);
        return joined.ToArray();
    }

    /// <summary>
    /// At most <paramref name="maxLines"/> of <paramref name="lines"/>; when there were more, the last one kept ends with an
    /// ellipsis, cut so it still fits in <paramref name="maxWidth"/>.
    /// </summary>
    public static IReadOnlyList<string> Clip(IReadOnlyList<string> lines, int maxLines, PdfFont font, double size, double maxWidth)
    {
        if (lines.Count <= maxLines)
            return lines;
        var kept = lines.Take(maxLines).ToList();
        var last = kept[^1].TrimEnd();
        while (last.Length > 0 && Width(last + Ellipsis, font, size) > maxWidth)
            last = last[..^1].TrimEnd();
        kept[^1] = last + Ellipsis;
        return kept;
    }

    /// <summary>A text meant for one line or one block: its line breaks and runs of spaces are one space.</summary>
    public static string OneLine(string? text)
        => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>How wide <paramref name="text"/> is in points, in <paramref name="font"/> at <paramref name="size"/>.</summary>
    public static double Width(string text, PdfFont font, double size) => Width(Encode(text), font, size);

    public static double Width(byte[] encoded, PdfFont font, double size)
    {
        var widths = font == PdfFont.Bold ? BoldWidths : RegularWidths;
        var total = 0;
        foreach (var code in encoded)
            total += code >= 32 ? widths[code - 32] : 0;
        return total * size / 1000d;
    }

    /// <summary>
    /// <paramref name="text"/> in lines no wider than <paramref name="maxWidth"/>: by words, its own line breaks kept, and a
    /// word wider than a line broken where it reaches the edge. Blank text has no lines.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string? text, PdfFont font, double size, double maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return lines;

        foreach (var paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (Width(candidate, font, size) <= maxWidth)
                {
                    line.Clear().Append(candidate);
                    continue;
                }
                if (line.Length > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                // A word wider than the line goes in pieces as wide as it.
                var rest = word;
                while (Width(rest, font, size) > maxWidth)
                {
                    var cut = 1;
                    while (cut < rest.Length && Width(rest[..(cut + 1)], font, size) <= maxWidth)
                        cut++;
                    lines.Add(rest[..cut]);
                    rest = rest[cut..];
                }
                line.Append(rest);
            }
            lines.Add(line.ToString());
        }

        // Blank lines at the end of a note say nothing.
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static bool TryEncode(char c, out byte code)
    {
        if (c is >= ' ' and <= '~' || c is >= '¡' and <= 'ÿ')
        {
            code = (byte)c;
            return true;
        }
        if (c == ' ')
        {
            code = (byte)' ';
            return true;
        }
        return Windows1252.TryGetValue(c, out code);
    }

    // «ő» is «o», «ﬁ» is «fi»: the letters of its decomposition the encoding has, without the marks it lacks.
    private static List<byte> Nearest(char c)
    {
        var nearest = new List<byte>();
        foreach (var part in c.ToString().Normalize(NormalizationForm.FormKD))
        {
            if (TryEncode(part, out var code))
                nearest.Add(code);
            else if (CharUnicodeInfo.GetUnicodeCategory(part) != UnicodeCategory.NonSpacingMark)
                return [];
        }
        return nearest;
    }
}
