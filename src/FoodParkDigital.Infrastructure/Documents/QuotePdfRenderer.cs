using System.Globalization;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Infrastructure.Invoicing;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FoodParkDigital.Infrastructure.Documents;

/// <summary>
/// The PDF of a quote (Fase 31M, ADR 0093), on letter-size pages: the business (its logo when it has one, its name and its
/// phone), the folio and version, where the quote stands, who it is for, when it was sent and until when it is valid,
/// its lines (the table continues on the next page with its header), its totals with the visit it takes off and, since the
/// Fase 31N, its deposit, whether its prices include IVA and its note. Every page says its number and how many there are.
/// </summary>
internal sealed class QuotePdfRenderer : IQuotePdfRenderer
{
    private const double Margin = 48;
    private const double Right = PdfWriter.PageWidth - Margin;
    private const double Top = PdfWriter.PageHeight - Margin;
    // The lowest a line goes: below it, the footer.
    private const double Bottom = 72;
    private const double FooterY = 40;

    private const double BodySize = 10;
    private const double SmallSize = 8.5;
    private const double Leading = 13;

    // The columns of the table: what it includes wraps; the amounts end at their right edge.
    private const double TotalRight = Right;
    private const double PriceRight = TotalRight - 112;
    private const double QuantityRight = PriceRight - 92;
    private const double DescriptionWidth = QuantityRight - 64 - Margin;
    private const double TableHeaderHeight = 20;
    private const double CellPadding = 5;

    private const double LogoMaxWidth = 150;
    private const double LogoMaxHeight = 60;

    private static readonly PdfColor Accepted = new(0x19, 0x87, 0x54);
    private static readonly PdfColor Rejected = new(0xB0, 0x2A, 0x37);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly ICfdiPdfLogos _logos;
    private readonly ILogger<QuotePdfRenderer> _logger;

    public QuotePdfRenderer(ICfdiPdfLogos logos, ILogger<QuotePdfRenderer> logger)
    {
        _logos = logos;
        _logger = logger;
    }

    public async Task<byte[]> RenderAsync(QuotePdfModel quote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quote);
        var logo = Decode(await _logos.PngAsync(quote.TenantId, cancellationToken), quote.TenantId);
        return Render(quote, logo);
    }

    /// <summary>The PDF of <paramref name="quote"/>, with <paramref name="logo"/> when there is one.</summary>
    internal static byte[] Render(QuotePdfModel quote, LogoPixels? logo)
    {
        var pdf = new PdfWriter();
        var layout = new Layout(pdf, quote);
        layout.Header(logo is null ? null : (pdf.AddImage(logo.Width, logo.Height, logo.Rgb, logo.Alpha), logo.Width, logo.Height));
        layout.Lines();
        layout.Totals();
        layout.Note();
        layout.Footers();
        return pdf.Build($"Cotización {Folio(quote)} de {PdfText.OneLine(quote.BusinessName)}");
    }

    /// <summary>The logo's pixels, or null when there is none or it does not decode: the PDF goes without it.</summary>
    internal LogoPixels? Decode(byte[]? png, Guid tenantId)
    {
        if (png is null)
            return null;
        try
        {
            using var image = Image.Load<Rgba32>(png);
            var pixels = new Rgba32[image.Width * image.Height];
            image.CopyPixelDataTo(pixels);
            var rgb = new byte[pixels.Length * 3];
            var alpha = new byte[pixels.Length];
            var opaque = true;
            for (var i = 0; i < pixels.Length; i++)
            {
                rgb[i * 3] = pixels[i].R;
                rgb[i * 3 + 1] = pixels[i].G;
                rgb[i * 3 + 2] = pixels[i].B;
                alpha[i] = pixels[i].A;
                opaque &= pixels[i].A == byte.MaxValue;
            }
            return new LogoPixels(image.Width, image.Height, rgb, opaque ? null : alpha);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever it is, the PDF goes without the logo: never missing because of it.
            _logger.LogWarning("The logo of business {TenantId} could not be decoded ({Error}): its quote PDF goes without it",
                tenantId, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>«COT-000123», or «COT-000123 v2» from its second version on: how the customer's messages name it.</summary>
    private static string Folio(QuotePdfModel quote) => QuoteDeliveries.Folio(quote.Number, quote.Revision);

    private static string Money(decimal amount) => OrderStatusLabels.FormatMoney(amount);

    private static string Day(DateOnly day) => QuoteDeliveries.Date(day);

    /// <summary>
    /// Where the version stands, in one sentence for whoever reads the paper; none while it is sent and valid: its validity is
    /// beside who it is for.
    /// </summary>
    internal static (string Text, PdfColor Color) Standing(QuotePdfModel quote) => quote.Status switch
    {
        QuoteStatus.Draft => ("Borrador: todavía no se envía al cliente.", PdfColor.Muted),
        QuoteStatus.Accepted => (quote.RepliedOn is { } on ? $"Aceptada el {Day(on)}." : "Aceptada.", Accepted),
        QuoteStatus.Rejected => (quote.RepliedOn is { } on ? $"Rechazada el {Day(on)}." : "Rechazada.", Rejected),
        QuoteStatus.Expired when quote.ValidUntil is { } until => ($"Venció el {Day(until)}.", PdfColor.Muted),
        QuoteStatus.Superseded => ("Se corrigió con una versión nueva: esta ya no vale.", PdfColor.Muted),
        QuoteStatus.Discarded => ("Borrador descartado: no se envió.", PdfColor.Muted),
        _ => (string.Empty, PdfColor.Black)
    };

    /// <summary>A logo as the PDF draws it: three bytes per pixel and, when it has transparency, one more.</summary>
    internal sealed record LogoPixels(int Width, int Height, byte[] Rgb, byte[]? Alpha);

    /// <summary>Where the next line goes, page after page.</summary>
    private sealed class Layout
    {
        private readonly PdfWriter _pdf;
        private readonly QuotePdfModel _quote;
        private PdfPage _page;
        private double _y;

        public Layout(PdfWriter pdf, QuotePdfModel quote)
        {
            _pdf = pdf;
            _quote = quote;
            _page = pdf.AddPage();
            _y = Top;
        }

        public void Header((string Name, int Width, int Height)? logo)
        {
            var contact = new[] { _quote.BusinessPhone }
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => PdfText.OneLine(line))
                .ToList();
            double logoWidth = 0, logoHeight = 0;
            if (logo is { } image)
            {
                // Never larger than its box, nor than its own pixels at 72 per inch.
                var scale = Math.Min(1, Math.Min(LogoMaxWidth / image.Width, LogoMaxHeight / image.Height));
                logoWidth = image.Width * scale;
                logoHeight = image.Height * scale;
                _page.Image(image.Name, Margin, Top - logoHeight, logoWidth, logoHeight);
            }

            // The business: beside its logo, to the right; without one, where the logo would go.
            void Draw(string text, double y, PdfFont font, double size, PdfColor color)
            {
                if (logo is null)
                    _page.Text(Margin, y, text, font, size, color);
                else
                    _page.TextRight(Right, y, text, font, size, color);
            }
            var nameWidth = logo is null ? Right - Margin : Right - Margin - logoWidth - 24;
            var baseline = Top - 13;
            var lowest = baseline;
            var name = PdfText.Wrap(PdfText.OneLine(_quote.BusinessName), PdfFont.Bold, 13, nameWidth);
            foreach (var line in PdfText.Clip(name, 2, PdfFont.Bold, 13, nameWidth))
            {
                Draw(line, baseline, PdfFont.Bold, 13, PdfColor.Black);
                lowest = baseline;
                baseline -= 15;
            }
            baseline += 2;
            // How to reach it, never past the business's block.
            foreach (var line in contact.SelectMany(line => PdfText.Clip(PdfText.Wrap(line, PdfFont.Regular, 9, nameWidth), 2, PdfFont.Regular, 9, nameWidth)))
            {
                Draw(line, baseline, PdfFont.Regular, 9, PdfColor.Muted);
                lowest = baseline;
                baseline -= 12;
            }
            var blockHeight = Top - lowest + 4;
            _y = Top - Math.Max(logoHeight, blockHeight) - 28;

            var title = $"Cotización {_quote.Number}";
            _page.Text(Margin, _y - 18, title, PdfFont.Bold, 20, PdfColor.Black);
            if (_quote.Revision > 1)
                _page.Text(Margin + PdfText.Width(title, PdfFont.Bold, 20) + 10, _y - 18,
                    $"versión {_quote.Revision}", PdfFont.Regular, 12, PdfColor.Muted);
            _y -= 18 + 10;

            var (standing, color) = Standing(_quote);
            if (standing.Length > 0)
            {
                _page.Text(Margin, _y - 11, standing, PdfFont.Bold, 10.5, color);
                _y -= 11 + 14;
            }

            // Who it is for, when it was sent and until when it is valid.
            var sent = _quote.SentOn is { } on ? Day(on) : "Sin enviar";
            var valid = _quote.ValidUntil is { } until
                ? Day(until)
                : $"{_quote.ValidityDays} {(_quote.ValidityDays == 1 ? "día" : "días")} desde que se envíe";
            var columns = new (string Label, string Value, double X, double Width)[]
            {
                ("Para", _quote.CustomerName, Margin, 220),
                ("Fecha", sent, Margin + 236, 130),
                ("Vale hasta", valid, Margin + 382, Right - Margin - 382)
            };
            var tallest = 0;
            foreach (var (label, value, x, width) in columns)
            {
                _page.Text(x, _y - SmallSize, label, PdfFont.Regular, SmallSize, PdfColor.Muted);
                var lines = PdfText.Clip(PdfText.Wrap(PdfText.OneLine(value), PdfFont.Bold, BodySize, width), 3, PdfFont.Bold, BodySize, width);
                for (var i = 0; i < lines.Count; i++)
                    _page.Text(x, _y - SmallSize - 14 - i * Leading, lines[i], PdfFont.Bold, BodySize, PdfColor.Black);
                tallest = Math.Max(tallest, lines.Count);
            }
            _y -= SmallSize + 14 + Math.Max(0, tallest - 1) * Leading + 20;
        }

        public void Lines()
        {
            TableHeader(continues: false);
            foreach (var line in _quote.Lines)
            {
                // What it includes is one line of text, however it was written: a row never grows past a page.
                var description = PdfText.Wrap(PdfText.OneLine(line.Description), PdfFont.Regular, BodySize, DescriptionWidth);
                var rowHeight = Math.Max(1, description.Count) * Leading + CellPadding * 2;
                if (_y - rowHeight < Bottom)
                {
                    NewPage();
                    TableHeader(continues: true);
                }

                var baseline = _y - CellPadding - BodySize;
                for (var i = 0; i < description.Count; i++)
                    _page.Text(Margin + 6, baseline - i * Leading, description[i], PdfFont.Regular, BodySize, PdfColor.Black);
                _page.TextRight(QuantityRight, baseline, line.Quantity.ToString("#,##0.###", Invariant), PdfFont.Regular, BodySize, PdfColor.Black);
                _page.TextRight(PriceRight, baseline, Money(line.UnitPrice), PdfFont.Regular, BodySize, PdfColor.Black);
                _page.TextRight(TotalRight - 6, baseline, Money(line.Total), PdfFont.Regular, BodySize, PdfColor.Black);
                _y -= rowHeight;
                _page.Line(Margin, _y, Right, _y, 0.5, PdfColor.Rule);
            }
        }

        public void Totals()
        {
            var rows = new List<(string Label, string Amount, bool Strong)>
            {
                ("Subtotal", Money(_quote.SubTotal), false),
                ("IVA", Money(_quote.Tax), false),
                ("Total", Money(_quote.Total), true)
            };
            if (_quote.VisitCredit > 0)
            {
                rows.Add(("Visita (se descuenta)", "−" + Money(_quote.VisitCredit), false));
                rows.Add(("Por pagar", Money(_quote.ToPay), true));
            }
            // The deposit (Fase 31N): to pay to accept it while it waits, or paid once that accepted it.
            if (_quote.DepositAmount is { } deposit && (_quote.DepositPaid || _quote.Status == QuoteStatus.Sent))
                rows.Add((_quote.DepositPaid ? "Anticipo pagado en línea" : "Anticipo para aceptarla", Money(deposit), false));
            var height = 10 + rows.Count * 16 + 22;
            if (_y - height < Bottom)
                NewPage();

            _y -= 10;
            foreach (var (label, amount, strong) in rows)
            {
                var font = strong ? PdfFont.Bold : PdfFont.Regular;
                var size = strong ? 11 : BodySize;
                // A label never touches its amount, however long the amount is.
                var amountLeft = TotalRight - 6 - PdfText.Width(amount, font, size);
                _page.TextRight(Math.Min(PriceRight, amountLeft - 12), _y - size, label, font, size, strong ? PdfColor.Black : PdfColor.Muted);
                _page.TextRight(TotalRight - 6, _y - size, amount, font, size, PdfColor.Black);
                _y -= 16;
            }
            _y -= 6;
            _page.Text(Margin, _y - SmallSize,
                _quote.PricesIncludeTax ? "Los precios incluyen IVA." : "El IVA se suma a los precios.",
                PdfFont.Regular, SmallSize, PdfColor.Muted);
            _y -= SmallSize + 16;
        }

        public void Note()
        {
            var lines = PdfText.Wrap(_quote.Note, PdfFont.Regular, BodySize, Right - Margin);
            if (lines.Count == 0)
                return;
            // The heading never stays alone at the bottom of a page.
            if (_y - BodySize - 6 - Leading < Bottom)
                NewPage();
            _page.Text(Margin, _y - BodySize, "Notas", PdfFont.Bold, BodySize, PdfColor.Black);
            _y -= BodySize + 6;
            foreach (var line in lines)
            {
                if (_y - Leading < Bottom)
                    NewPage();
                _page.Text(Margin, _y - BodySize, line, PdfFont.Regular, BodySize, PdfColor.Black);
                _y -= Leading;
            }
        }

        /// <summary>Who it is from and which page of how many, on every page, once all are laid out.</summary>
        public void Footers()
        {
            var pages = _pdf.Pages;
            var from = $"{PdfText.OneLine(_quote.BusinessName)} · Cotización {Folio(_quote)}";
            var room = Right - Margin - 90;
            if (PdfText.Width(from, PdfFont.Regular, 8) > room)
                from = PdfText.Clip(PdfText.Wrap(from, PdfFont.Regular, 8, room), 1, PdfFont.Regular, 8, room)[0];
            for (var i = 0; i < pages.Count; i++)
            {
                pages[i].Line(Margin, FooterY + 12, Right, FooterY + 12, 0.5, PdfColor.Rule);
                pages[i].Text(Margin, FooterY, from, PdfFont.Regular, 8, PdfColor.Muted);
                pages[i].TextRight(Right, FooterY, $"Página {i + 1} de {pages.Count}", PdfFont.Regular, 8, PdfColor.Muted);
            }
        }

        private void TableHeader(bool continues)
        {
            _page.Rectangle(Margin, _y - TableHeaderHeight, Right - Margin, TableHeaderHeight, PdfColor.Shade);
            var baseline = _y - 13.5;
            _page.Text(Margin + 6, baseline, continues ? "Qué incluye (continúa)" : "Qué incluye", PdfFont.Bold, 9, PdfColor.Black);
            _page.TextRight(QuantityRight, baseline, "Cantidad", PdfFont.Bold, 9, PdfColor.Black);
            _page.TextRight(PriceRight, baseline, "Precio", PdfFont.Bold, 9, PdfColor.Black);
            _page.TextRight(TotalRight - 6, baseline, "Importe", PdfFont.Bold, 9, PdfColor.Black);
            _y -= TableHeaderHeight;
        }

        private void NewPage()
        {
            _page = _pdf.AddPage();
            _y = Top;
        }
    }
}
