using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FoodParkDigital.Infrastructure.Documents;

/// <summary>A color of the PDF, in RGB.</summary>
internal readonly record struct PdfColor(byte R, byte G, byte B)
{
    public static readonly PdfColor Black = new(0x21, 0x25, 0x29);
    public static readonly PdfColor Muted = new(0x6C, 0x75, 0x7D);
    public static readonly PdfColor Rule = new(0xDE, 0xE2, 0xE6);
    public static readonly PdfColor Shade = new(0xF1, 0xF3, 0xF5);
}

/// <summary>
/// The smallest PDF writer TienditApp needs (Fase 31M, ADR 0093): letter-size pages with text in Helvetica and its bold
/// (WinAnsiEncoding, not embedded: every reader has them), lines, filled rectangles and RGB images with transparency. It
/// writes PDF 1.4 with compressed content and a cross-reference table, and nothing it was not given: no metadata but the
/// title, nothing of the reader nor of the request.
/// </summary>
internal sealed class PdfWriter
{
    /// <summary>US Letter, in points: the paper of Mexico's printers.</summary>
    public const double PageWidth = 612;
    public const double PageHeight = 792;

    private readonly List<PdfPage> _pages = [];
    private readonly List<Image> _images = [];

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage()
    {
        var page = new PdfPage(this);
        _pages.Add(page);
        return page;
    }

    /// <summary>
    /// An image to draw on any page: <paramref name="rgb"/> has three bytes per pixel, row by row from the top, and
    /// <paramref name="alpha"/> one, or null when it is opaque.
    /// </summary>
    /// <returns>Its name, for <see cref="PdfPage.Image"/>.</returns>
    public string AddImage(int width, int height, byte[] rgb, byte[]? alpha)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "An image has pixels.");
        if (rgb.Length != width * height * 3 || (alpha is not null && alpha.Length != width * height))
            throw new ArgumentException("The pixels do not match the size.", nameof(rgb));

        var name = $"Im{_images.Count + 1}";
        _images.Add(new Image(name, width, height, rgb, alpha));
        return name;
    }

    internal bool HasImage(string name) => _images.Any(image => image.Name == name);

    /// <summary>The PDF, with <paramref name="title"/> as its title.</summary>
    public byte[] Build(string title)
    {
        if (_pages.Count == 0)
            throw new InvalidOperationException("A PDF has at least one page.");

        var objects = new List<byte[]>();
        int Reserve()
        {
            objects.Add([]);
            return objects.Count;
        }
        void Set(int number, byte[] content) => objects[number - 1] = content;

        var catalog = Reserve();
        var pagesRoot = Reserve();
        var regular = Reserve();
        var bold = Reserve();
        var info = Reserve();
        Set(regular, Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        Set(bold, Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        Set(info, Ascii($"<< /Title {Utf16(title)} /Producer (TienditApp) >>"));

        var imageObjects = new Dictionary<string, int>();
        foreach (var image in _images)
        {
            int? mask = null;
            if (image.Alpha is not null)
            {
                mask = Reserve();
                Set(mask.Value, Stream(
                    $"/Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceGray /BitsPerComponent 8",
                    image.Alpha));
            }
            var number = Reserve();
            Set(number, Stream(
                $"/Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8"
                + (mask is { } m ? $" /SMask {m} 0 R" : string.Empty),
                image.Rgb));
            imageObjects[image.Name] = number;
        }

        var xObjects = imageObjects.Count == 0
            ? string.Empty
            : " /XObject << " + string.Join(" ", imageObjects.Select(pair => $"/{pair.Key} {pair.Value} 0 R")) + " >>";
        var resources = $"<< /Font << /F1 {regular} 0 R /F2 {bold} 0 R >>{xObjects} >>";
        var kids = new List<int>();
        foreach (var page in _pages)
        {
            var content = Reserve();
            Set(content, Stream(string.Empty, page.Content));
            var number = Reserve();
            Set(number, Ascii(
                $"<< /Type /Page /Parent {pagesRoot} 0 R /MediaBox [0 0 {Number(PageWidth)} {Number(PageHeight)}] /Resources {resources} /Contents {content} 0 R >>"));
            kids.Add(number);
        }
        Set(pagesRoot, Ascii($"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(kid => $"{kid} 0 R"))}] /Count {kids.Count} >>"));
        Set(catalog, Ascii($"<< /Type /Catalog /Pages {pagesRoot} 0 R >>"));

        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n");
        // A comment with bytes over 127 tells transfer programs the file is binary.
        output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = output.Position;
            Write(output, $"{i + 1} 0 obj\n");
            output.Write(objects[i]);
            Write(output, "\nendobj\n");
        }
        var xref = output.Position;
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Write(output, table.ToString());
        return output.ToArray();
    }

    /// <summary>A number as the PDF writes it: with a point and at most three decimals.</summary>
    internal static string Number(double value)
        => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static byte[] Stream(string dictionary, byte[] data)
    {
        var compressed = Compress(data);
        using var output = new MemoryStream();
        Write(output, $"<< {dictionary}{(dictionary.Length > 0 ? " " : string.Empty)}/Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        output.Write(compressed);
        Write(output, "\nendstream");
        return output.ToArray();
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static void Write(Stream output, string text) => output.Write(Encoding.ASCII.GetBytes(text));

    // A text string of the document's information, in UTF-16 with its byte order mark: any title reads right.
    private static string Utf16(string text)
    {
        var bytes = Encoding.BigEndianUnicode.GetBytes(text);
        return "<FEFF" + Convert.ToHexString(bytes) + ">";
    }

    private sealed record Image(string Name, int Width, int Height, byte[] Rgb, byte[]? Alpha);
}

/// <summary>A page of a <see cref="PdfWriter"/>: what is drawn on it, in points from its bottom-left corner.</summary>
internal sealed class PdfPage
{
    private readonly PdfWriter _document;
    private readonly MemoryStream _content = new();

    internal PdfPage(PdfWriter document) => _document = document;

    internal byte[] Content => _content.ToArray();

    /// <summary><paramref name="text"/> on one line, starting at (<paramref name="x"/>, <paramref name="y"/>), its baseline.</summary>
    public void Text(double x, double y, string text, PdfFont font, double size, PdfColor color)
    {
        var encoded = PdfText.Encode(text);
        if (encoded.Length == 0)
            return;
        Write($"BT /{(font == PdfFont.Bold ? "F2" : "F1")} {N(size)} Tf {Color(color)} rg 1 0 0 1 {N(x)} {N(y)} Tm (");
        foreach (var code in encoded)
        {
            // Inside a literal string only the backslash and the parentheses are special; the rest goes as octal so the
            // content stays ASCII.
            if (code is (byte)'\\' or (byte)'(' or (byte)')')
                Write("\\" + (char)code);
            else if (code is < 32 or > 126)
                Write("\\" + Convert.ToString(code, 8).PadLeft(3, '0'));
            else
                _content.WriteByte(code);
        }
        Write(") Tj ET\n");
    }

    /// <summary><paramref name="text"/> on one line, ending at <paramref name="right"/>.</summary>
    public void TextRight(double right, double y, string text, PdfFont font, double size, PdfColor color)
        => Text(right - PdfText.Width(text, font, size), y, text, font, size, color);

    public void Line(double x1, double y1, double x2, double y2, double width, PdfColor color)
        => Write($"q {Color(color)} RG {N(width)} w {N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S Q\n");

    public void Rectangle(double x, double y, double width, double height, PdfColor fill)
        => Write($"q {Color(fill)} rg {N(x)} {N(y)} {N(width)} {N(height)} re f Q\n");

    /// <summary>An image of the document (<see cref="PdfWriter.AddImage"/>) in the box with its bottom-left corner at (x, y).</summary>
    public void Image(string name, double x, double y, double width, double height)
    {
        if (!_document.HasImage(name))
            throw new ArgumentException("The image is not in this document.", nameof(name));
        Write($"q {N(width)} 0 0 {N(height)} {N(x)} {N(y)} cm /{name} Do Q\n");
    }

    private static string N(double value) => PdfWriter.Number(value);

    private static string Color(PdfColor color)
        => $"{N(color.R / 255d)} {N(color.G / 255d)} {N(color.B / 255d)}";

    private void Write(string text) => _content.Write(Encoding.ASCII.GetBytes(text));
}
