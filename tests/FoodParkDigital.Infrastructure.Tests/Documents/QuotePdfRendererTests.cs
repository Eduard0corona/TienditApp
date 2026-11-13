using System.Text;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Infrastructure.Documents;
using FoodParkDigital.Infrastructure.Invoicing;
using FoodParkDigital.Infrastructure.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FoodParkDigital.Infrastructure.Tests.Documents;

/// <summary>
/// The PDF of a quote (Fase 31M, ADR 0093), read back with an independent reader: what it says, its pages and its logo.
/// </summary>
public class QuotePdfRendererTests
{
    private static readonly Guid Business = Guid.NewGuid();

    [Fact]
    public async Task ASentQuote_ShouldSayEverythingItsPageSays_InOnePage()
    {
        var pdf = await RenderAsync(Quote() with { Revision = 2, VisitCredit = 350m, ToPay = 1_490.50m });

        using var document = PdfDocument.Open(pdf);
        Assert.Equal(1, document.NumberOfPages);
        Assert.Equal("Cotización COT-000123 v2 de Plomería Díaz", document.Information.Title);
        var text = document.GetPage(1).Text;
        foreach (var expected in new[]
        {
            "Plomería Díaz", "+52 55 1234 5678",
            "Cotización COT-000123", "versión 2",
            "Para", "Ana Pérez Núñez", "Fecha", "13 de noviembre de 2026", "Vale hasta", "28 de noviembre de 2026",
            "Qué incluye", "Cantidad", "Precio", "Importe",
            "Cambio de tubería de cobre", "1.5", "$450.00", "$675.00",
            "Mano de obra", "$1,000.00",
            "Subtotal", "$1,675.00", "IVA", "$165.50", "Total", "$1,840.50",
            "Visita (se descuenta)", "–$350.00", "Por pagar", "$1,490.50",
            "Los precios incluyen IVA.",
            "Notas", "Incluye material y garantía de 30 días.",
            "Plomería Díaz · Cotización COT-000123 v2", "Página 1 de 1"
        })
            Assert.Contains(expected, text);
        // Sent and valid, its validity is beside who it is for: no sentence repeats it.
        Assert.DoesNotContain("Vale hasta el", text);
    }

    [Fact]
    public async Task AHugeTotal_ShouldNeverTouchItsLabel()
    {
        var pdf = await RenderAsync(Quote() with { SubTotal = 500_000_000_000m, Tax = 80_000_000_000m, Total = 580_000_000_000m, ToPay = 580_000_000_000m });

        using var document = PdfDocument.Open(pdf);
        var words = document.GetPage(1).GetWords().ToList();
        var label = words.Single(word => word.Text == "Total");
        var amount = words.Single(word => word.Text == "$580,000,000,000.00");
        Assert.True(amount.BoundingBox.Left - label.BoundingBox.Right >= 10, $"{label.BoundingBox.Right} vs {amount.BoundingBox.Left}");
    }

    [Fact]
    public async Task WithoutAVisitCredit_ShouldEndAtTheTotal_AndSayIfTheTaxIsAdded()
    {
        var pdf = await RenderAsync(Quote() with { PricesIncludeTax = false, Note = null });

        var text = Text(pdf);
        Assert.Contains("El IVA se suma a los precios.", text);
        Assert.DoesNotContain("Visita (se descuenta)", text);
        Assert.DoesNotContain("Por pagar", text);
        Assert.DoesNotContain("Notas", text);
    }

    [Theory]
    [InlineData(QuoteStatus.Draft, "Borrador: todavía no se envía al cliente.")]
    [InlineData(QuoteStatus.Accepted, "Aceptada el 15 de noviembre de 2026.")]
    [InlineData(QuoteStatus.Rejected, "Rechazada el 15 de noviembre de 2026.")]
    [InlineData(QuoteStatus.Expired, "Venció el 28 de noviembre de 2026.")]
    [InlineData(QuoteStatus.Superseded, "Se corrigió con una versión nueva: esta ya no vale.")]
    [InlineData(QuoteStatus.Discarded, "Borrador descartado: no se envió.")]
    public async Task EachStatus_ShouldSayWhereTheVersionStands(QuoteStatus status, string standing)
    {
        var replied = status is QuoteStatus.Accepted or QuoteStatus.Rejected;
        var pdf = await RenderAsync(Quote() with { Status = status, RepliedOn = replied ? new DateOnly(2026, 11, 15) : null });

        var text = Text(pdf);
        Assert.Contains(standing, text);
        Assert.DoesNotContain("Vale hasta el", text);
    }

    [Fact]
    public async Task ADraft_ShouldSayItWasNotSent_AndHowLongItWillBeValid()
    {
        var pdf = await RenderAsync(Quote() with { Status = QuoteStatus.Draft, SentOn = null, ValidUntil = null, ValidityDays = 15 });

        var text = Text(pdf);
        Assert.Contains("Sin enviar", text);
        Assert.Contains("15 días desde que se envíe", text);
        Assert.Contains("1 día desde que se envíe", Text(await RenderAsync(Quote() with
        {
            Status = QuoteStatus.Draft, SentOn = null, ValidUntil = null, ValidityDays = 1
        })));
    }

    [Fact]
    public async Task ManyLines_ShouldContinueTheTableOnTheNextPages_EachLineOnce_AndNumberEveryPage()
    {
        var lines = Enumerable.Range(1, 50)
            .Select(i => new QuotePdfLine(
                $"Renglón {i:00}: " + string.Join(' ', Enumerable.Repeat("trabajo de instalación con material", 4)),
                1, 100m, 100m))
            .ToList();
        var note = string.Join('\n', Enumerable.Range(1, 60).Select(i => $"Condición {i}: el cliente acepta la vigencia."));
        var pdf = await RenderAsync(Quote() with { Lines = lines, Note = note });

        using var document = PdfDocument.Open(pdf);
        Assert.True(document.NumberOfPages >= 4, $"Only {document.NumberOfPages} pages.");
        var pages = document.GetPages().ToList();
        var all = string.Concat(pages.Select(page => page.Text));
        for (var i = 1; i <= 50; i++)
            Assert.Equal(1, Count(all, $"Renglón {i:00}:"));
        for (var i = 1; i <= 60; i++)
            Assert.Equal(1, Count(all, $"Condición {i}:"));
        // The table's header on the first page, and on each page it continues on, saying so.
        Assert.Contains("Qué incluye", pages[0].Text);
        Assert.DoesNotContain("(continúa)", pages[0].Text);
        Assert.Contains("Qué incluye (continúa)", pages[1].Text);
        foreach (var page in pages)
        {
            Assert.Contains($"Página {page.Number} de {pages.Count}", page.Text);
            AssertWithinTheMargins(page);
        }
        // The totals come after the last line.
        var lastPage = pages.Single(page => page.Text.Contains("Renglón 50:"));
        Assert.True(pages.IndexOf(lastPage) <= pages.FindIndex(page => page.Text.Contains("Subtotal")));
    }

    [Fact]
    public async Task ALongDescription_ShouldWrapWithinItsColumn()
    {
        var description = string.Join(' ', Enumerable.Repeat("Tubería", 30));
        var pdf = await RenderAsync(Quote() with { Lines = [new QuotePdfLine(description, 2, 10m, 20m)] });

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        var words = page.GetWords().Where(word => word.Text == "Tubería").ToList();
        Assert.Equal(30, words.Count);
        Assert.True(words.Select(word => Math.Round(word.BoundingBox.Bottom)).Distinct().Count() > 1, "It did not wrap.");
        // What it includes never reaches the quantity's column.
        Assert.All(words, word => Assert.True(word.BoundingBox.Right < 300, $"A word ends at {word.BoundingBox.Right}."));
    }

    [Fact]
    public async Task WhatTheFontsLack_ShouldNotStopThePdf()
    {
        var pdf = await RenderAsync(Quote() with { CustomerName = "Zoë 😀 王", Note = "Llámame\tantes ‐ gracias" });

        var text = Text(pdf);
        // The emoji is left out with its space; the other alphabet says something is missing.
        Assert.Contains("Zoë ?", text);
        Assert.Contains("Llámame antes - gracias", text);
    }

    [Fact]
    public async Task ALogoWithTransparency_ShouldBeDrawnWithItsMask_WithinItsBox()
    {
        var pdf = await RenderAsync(Quote(), Png(800, 200, transparent: true));

        using var document = PdfDocument.Open(pdf);
        var image = Assert.Single(document.GetPage(1).GetImages());
        Assert.Equal((800, 200), (image.WidthInSamples, image.HeightInSamples));
        // Fitted into 150 x 60 points, keeping its proportions, at the top left.
        Assert.Equal(150, image.Bounds.Width, 1);
        Assert.Equal(37.5, image.Bounds.Height, 1);
        Assert.Equal(48, image.Bounds.Left, 1);
        Assert.Contains("/SMask", Encoding.Latin1.GetString(pdf));
        // The business goes to the right of its logo.
        var name = document.GetPage(1).GetWords().First(word => word.Text == "Plomería");
        Assert.True(name.BoundingBox.Left > image.Bounds.Right, "The name is over the logo.");
    }

    [Fact]
    public async Task AnOpaqueLogo_ShouldGoWithoutAMask_AndNeverLargerThanItsPixels()
    {
        var pdf = await RenderAsync(Quote(), Png(40, 20, transparent: false));

        using var document = PdfDocument.Open(pdf);
        var image = Assert.Single(document.GetPage(1).GetImages());
        Assert.Equal((40, 20), (Math.Round(image.Bounds.Width), Math.Round(image.Bounds.Height)));
        Assert.DoesNotContain("/SMask", Encoding.Latin1.GetString(pdf));
    }

    [Fact]
    public async Task WithoutALogo_OrWithOneThatDoesNotDecode_ShouldGoWithoutAnImage()
    {
        var logger = new CapturingLogger<QuotePdfRenderer>();
        foreach (var logo in new byte[]?[] { null, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3] })
        {
            var pdf = await new QuotePdfRenderer(new FixedLogos(logo), logger).RenderAsync(Quote(), CancellationToken.None);

            using var document = PdfDocument.Open(pdf);
            Assert.Empty(document.GetPage(1).GetImages());
            Assert.Contains("Plomería Díaz", document.GetPage(1).Text);
        }
        var warning = Assert.Single(logger.Entries);
        Assert.Contains(Business.ToString(), warning.Message);
    }

    [Fact]
    public async Task ThePdf_ShouldBeAValidPdf14_WithItsCrossReferenceTable()
    {
        var pdf = await RenderAsync(Quote());

        var raw = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4\n", raw);
        Assert.EndsWith("%%EOF\n", raw);
        var startXref = int.Parse(raw[(raw.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.Equal("xref", raw.Substring(startXref, 4));
        // Each object is where the table says.
        var entries = raw[startXref..].Split('\n').Skip(3).TakeWhile(line => line.EndsWith(" n ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(entries);
        for (var i = 0; i < entries.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj", raw[int.Parse(entries[i][..10])..]);
        // The fonts every reader has, never embedded.
        Assert.Contains("/BaseFont /Helvetica /Encoding /WinAnsiEncoding", raw);
        Assert.Contains("/BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding", raw);
        Assert.DoesNotContain("/FontFile", raw);
    }

    [Fact]
    public async Task ADescriptionWithManyLineBreaks_ShouldStayOneRow_WithinThePage()
    {
        // The domain takes 200 characters, line breaks among them, through the API.
        var description = string.Concat(Enumerable.Repeat("w\n", 99)) + "w";
        var pdf = await RenderAsync(Quote() with { Lines = [new QuotePdfLine(description, 1, 10m, 10m)] });

        using var document = PdfDocument.Open(pdf);
        Assert.Equal(1, document.NumberOfPages);
        AssertWithinTheMargins(document.GetPage(1));
        Assert.Equal(100, document.GetPage(1).Letters.Count(letter => letter.Value == "w" && letter.StartBaseLine.Y > 72));
    }

    [Fact]
    public async Task NamesLongerThanTheirRoom_ShouldBeCutWithAnEllipsis_WithinTheMargins()
    {
        var business = "Plomería " + string.Join(' ', Enumerable.Repeat("Hermanos Díaz y Asociados", 8));
        var customer = string.Join(' ', Enumerable.Repeat("Valeria Guadalupe Ruiz", 9));
        var phone = new string('5', 120);
        foreach (var logo in new byte[]?[] { null, Png(300, 120, transparent: false) })
        {
            var pdf = await RenderAsync(Quote() with { BusinessName = business, CustomerName = customer, BusinessPhone = phone }, logo);

            using var document = PdfDocument.Open(pdf);
            var page = document.GetPage(1);
            AssertWithinTheMargins(page);
            // The business, the customer and the footer, each cut where its room ends, and said so.
            Assert.True(Count(page.Text, "…") >= 3, page.Text);
            Assert.DoesNotContain(business, page.Text);
            Assert.DoesNotContain(customer, page.Text);
            Assert.Contains("Plomería Hermanos", page.Text);
            Assert.Contains("Valeria Guadalupe", page.Text);
            // The phone, never over the logo nor past the edge.
            Assert.Contains("5555", page.Text);
        }
    }

    [Fact]
    public async Task EachStream_ShouldDeclareItsOwnLength()
    {
        var pdf = await RenderAsync(Quote() with { Lines = Enumerable.Range(1, 30).Select(i => new QuotePdfLine($"Renglón {i}", 1, 1m, 1m)).ToList() },
            Png(64, 32, transparent: true));

        var raw = Encoding.Latin1.GetString(pdf);
        var streams = 0;
        for (var at = raw.IndexOf("stream\n", StringComparison.Ordinal); at >= 0; at = raw.IndexOf("stream\n", at + 1, StringComparison.Ordinal))
        {
            if (at >= 3 && raw.Substring(at - 3, 3) == "end")
                continue;
            var dictionary = raw[raw.LastIndexOf("<<", at, StringComparison.Ordinal)..at];
            var declared = int.Parse(System.Text.RegularExpressions.Regex.Match(dictionary, @"/Length (\d+)").Groups[1].Value);
            var start = at + "stream\n".Length;
            Assert.Equal("\nendstream", raw.Substring(start + declared, "\nendstream".Length));
            streams++;
        }
        // The page's content, the logo and its mask.
        Assert.True(streams >= 3, $"{streams} streams");
    }

    /// <summary>Nothing but the footer (its rule at 52 and its line at 40) below the bottom margin, nor past the side margins.</summary>
    private static void AssertWithinTheMargins(Page page)
    {
        foreach (var letter in page.Letters)
        {
            var footer = Math.Abs(letter.StartBaseLine.Y - 40) < 0.5;
            Assert.True(footer || letter.StartBaseLine.Y >= 72 - 0.01, $"«{letter.Value}» at y {letter.StartBaseLine.Y} on page {page.Number}");
            Assert.True(letter.StartBaseLine.X >= 48 - 0.01 && letter.EndBaseLine.X <= 564 + 0.01,
                $"«{letter.Value}» from x {letter.StartBaseLine.X} to {letter.EndBaseLine.X} on page {page.Number}");
        }
    }

    private static QuotePdfModel Quote() => new(
        Business,
        "Plomería Díaz",
        "+52 55 1234 5678",
        "COT-000123",
        1,
        QuoteStatus.Sent,
        "Ana Pérez Núñez",
        new DateOnly(2026, 11, 13),
        new DateOnly(2026, 11, 28),
        15,
        null,
        [
            new QuotePdfLine("Cambio de tubería de cobre", 1.5m, 450m, 675m),
            new QuotePdfLine("Mano de obra", 1m, 1_000m, 1_000m)
        ],
        1_675m,
        165.50m,
        1_840.50m,
        0m,
        1_840.50m,
        true,
        "Incluye material y garantía de 30 días.");

    private static Task<byte[]> RenderAsync(QuotePdfModel quote, byte[]? logo = null)
        => new QuotePdfRenderer(new FixedLogos(logo), new CapturingLogger<QuotePdfRenderer>()).RenderAsync(quote, CancellationToken.None);

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Concat(document.GetPages().Select(page => page.Text));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static byte[] Png(int width, int height, bool transparent)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(0x0D, 0x6E, 0xFD, transparent ? (byte)0x80 : byte.MaxValue));
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    private sealed class FixedLogos(byte[]? png) : ICfdiPdfLogos
    {
        public Task<byte[]?> PngAsync(Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(tenantId == Business ? png : null);
    }
}
