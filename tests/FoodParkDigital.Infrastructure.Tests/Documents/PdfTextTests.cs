using FoodParkDigital.Infrastructure.Documents;

namespace FoodParkDigital.Infrastructure.Tests.Documents;

/// <summary>Text in the two fonts of TienditApp's PDF (Fase 31M): WinAnsiEncoding, Helvetica's widths and wrapping.</summary>
public class PdfTextTests
{
    [Fact]
    public void Spanish_ShouldBeWrittenAsIs_InWinAnsi()
    {
        Assert.Equal(
            new byte[] { (byte)'C', (byte)'o', (byte)'t', (byte)'i', (byte)'z', (byte)'a', (byte)'c', (byte)'i', 0xF3, (byte)'n', (byte)' ',
                0xF1, 0xD1, 0xBF, 0xA1, 0xFC, 0xC9 },
            PdfText.Encode("Cotización ñÑ¿¡üÉ"));
    }

    [Fact]
    public void TheCharactersOfWindows1252_ShouldUseTheirCodesBetween128And159()
    {
        Assert.Equal(new byte[] { 0x80, 0x96, 0x97, 0x93, 0x94, 0x91, 0x92, 0x85, 0x95 }, PdfText.Encode("€–—“”‘’…•"));
        // The minus sign is not in the encoding: the en dash draws the same.
        Assert.Equal(new byte[] { 0x96, (byte)'5' }, PdfText.Encode("−5"));
    }

    [Fact]
    public void WhatTheEncodingLacks_ShouldBecomeTheNearestLetter_ASpace_NothingOrAQuestionMark()
    {
        // A letter with a mark it lacks is the letter; a ligature, its letters.
        Assert.Equal("Lorinc fino"u8.ToArray(), PdfText.Encode("Lőrinc ﬁno"));
        // Line breaks, tabs and other spaces are a space; invisible characters and emoji are left out.
        Assert.Equal("a b c d e"u8.ToArray(), PdfText.Encode("a\nb\tc d e"));
        Assert.Equal("ab"u8.ToArray(), PdfText.Encode("a​😀b"));
        // A script these fonts never had is a question mark per character, so the reader sees something is missing.
        Assert.Equal("Wang ??"u8.ToArray(), PdfText.Encode("Wang 王芳"));
        // A symbol they lack is left out, also inside the basic plane, with the space it leaves doubled once.
        Assert.Equal("Listo para el lunes"u8.ToArray(), PdfText.Encode("Listo ✅ para ★ el lunes ❤"));
        // A letter with a stroke or without a dot is its letter.
        Assert.Equal(new byte[] { (byte)'L', 0xF3, (byte)'d', (byte)'z', (byte)' ', (byte)'I', (byte)'s', (byte)'t', (byte)'a', (byte)'n',
            (byte)' ', (byte)'d', (byte)'a', (byte)'k' }, PdfText.Encode("Łódź İstan đak"));
    }

    [Fact]
    public void Clip_ShouldKeepTheLinesThatFit_AndEndTheLastWithAnEllipsis()
    {
        var lines = PdfText.Wrap(string.Join(' ', Enumerable.Repeat("Plomería Díaz", 10)), PdfFont.Bold, 13, 120);

        var clipped = PdfText.Clip(lines, 2, PdfFont.Bold, 13, 120);

        Assert.Equal(2, clipped.Count);
        Assert.Equal(lines[0], clipped[0]);
        Assert.EndsWith("…", clipped[1]);
        Assert.All(clipped, line => Assert.True(PdfText.Width(line, PdfFont.Bold, 13) <= 120, line));
        Assert.Same(lines, PdfText.Clip(lines, lines.Count, PdfFont.Bold, 13, 120));
        Assert.Equal("Cambio de tubería del baño", PdfText.OneLine(" Cambio de\ntubería\r\n  del baño "));
    }

    [Fact]
    public void Width_ShouldFollowEachFontsOwnWidths()
    {
        // H 722, o 556, l 222, a 556 in Helvetica; H 722, o 611, l 278, a 556 in its bold.
        Assert.Equal(20.56, PdfText.Width("Hola", PdfFont.Regular, 10), 3);
        Assert.Equal(21.67, PdfText.Width("Hola", PdfFont.Bold, 10), 3);
        // Measured as written: «ó» is as wide as «o».
        Assert.Equal(PdfText.Width("o", PdfFont.Regular, 12), PdfText.Width("ó", PdfFont.Regular, 12));
        Assert.Equal(0, PdfText.Width(string.Empty, PdfFont.Regular, 10));
    }

    [Fact]
    public void Wrap_ShouldBreakByWords_KeepItsOwnLines_AndCutAWordWiderThanTheLine()
    {
        var lines = PdfText.Wrap("Cambio de la tubería del baño\n\nY revisión del calentador", PdfFont.Regular, 10, 100);

        Assert.Equal(["Cambio de la tubería", "del baño", "", "Y revisión del", "calentador"], lines);
        Assert.All(lines, line => Assert.True(PdfText.Width(line, PdfFont.Regular, 10) <= 100, line));

        var word = new string('M', 40);
        var pieces = PdfText.Wrap(word, PdfFont.Bold, 10, 60);
        Assert.Equal(word, string.Concat(pieces));
        Assert.All(pieces, piece => Assert.True(PdfText.Width(piece, PdfFont.Bold, 10) <= 60, piece));
        Assert.True(pieces.Count > 1);
    }

    [Fact]
    public void Wrap_ShouldGiveNoLines_ForBlankText_NorTheBlankLinesAtItsEnd()
    {
        Assert.Empty(PdfText.Wrap(null, PdfFont.Regular, 10, 100));
        Assert.Empty(PdfText.Wrap("  \n ", PdfFont.Regular, 10, 100));
        Assert.Equal(["Nota"], PdfText.Wrap("Nota\n\n\n", PdfFont.Regular, 10, 100));
    }
}
