using System.Globalization;
using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Quotes.Entities;

namespace FoodParkDigital.Domain.Quotes;

/// <summary>
/// How the lines of an accepted quote become the lines of its service order (Fase 29E, ADR 0053 point 17): at the price and
/// with the IVA the customer accepted, never at the catalog's prices today, so the order totals what the quote did.
/// </summary>
public static class QuoteConversion
{
    /// <summary>
    /// How a line written by hand is measured and invoiced: a unit of service, with the generic product code (the business
    /// changes it in the invoice, as for any line).
    /// </summary>
    public static readonly OrderLineFiscalData HandWrittenFiscal = new(
        UnitType.Service,
        SatCatalog.GenericProductCode,
        SatCatalog.ServiceUnitCode,
        CfdiTaxObject.Subject);

    /// <summary>
    /// The lines of the order, in the quote's order.
    /// <list type="bullet">
    /// <item>A line of a product still in the catalog keeps the product and its unit and invoicing codes from
    /// <paramref name="catalog"/>.</item>
    /// <item>A line written by hand, or whose product left the catalog, is a unit of service named by the line itself: the
    /// order keeps the line's id where a product would go, as it keeps the id of a product deleted after the sale (no product
    /// has it, so it takes no stock).</item>
    /// <item>A quantity the unit does not sell (3.5 of a service sold by unit) goes as one at the line's amount, with the
    /// quantity in its name, so its amount does not change.</item>
    /// </list>
    /// </summary>
    /// <param name="catalog">The unit and invoicing codes of the quote's products that are still in the catalog, by product.</param>
    public static IReadOnlyList<OrderLineRequest> Lines(Quote quote, IReadOnlyDictionary<Guid, OrderLineFiscalData> catalog)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(catalog);

        var lines = new List<OrderLineRequest>(quote.Lines.Count);
        foreach (var line in quote.Lines.OrderBy(line => line.Position))
        {
            var fromCatalog = line.ProductId is { } productId && catalog.ContainsKey(productId);
            var fiscal = fromCatalog ? WithItsTax(catalog[line.ProductId!.Value], line.VatRate) : HandWrittenFiscal;
            var id = fromCatalog ? line.ProductId!.Value : line.Id;

            lines.Add(UnitTypeRules.IsValidQuantity(line.Quantity, fiscal.Unit)
                ? new OrderLineRequest(id, line.Description, line.Quantity, line.UnitPrice, null, line.VatRate, fiscal, [])
                : new OrderLineRequest(id, WithQuantity(line.Description, line.Quantity), 1m, line.Total, null, line.VatRate, fiscal, []));
        }

        return lines;
    }

    /// <summary>
    /// The codes of a product whose IVA the customer accepted: a product made «no objeto de impuesto» after it was quoted with
    /// IVA invoices the line as subject to it, so the invoice adds up the IVA the order charges.
    /// </summary>
    private static OrderLineFiscalData WithItsTax(OrderLineFiscalData product, VatRate accepted)
        => product.TaxObject == CfdiTaxObject.NotSubject && !accepted.IsExempt && accepted.Percent > 0
            ? product with { TaxObject = CfdiTaxObject.Subject }
            : product;

    /// <summary>«Tubo de cobre (3.5)», within the length of a line's name (<see cref="QuoteRules.MaxDescriptionLength"/>).</summary>
    public static string WithQuantity(string description, decimal quantity)
    {
        var suffix = $" ({quantity.ToString("0.###", CultureInfo.InvariantCulture)})";
        var room = QuoteRules.MaxDescriptionLength - suffix.Length;
        return (description.Length > room ? description[..room].TrimEnd() : description) + suffix;
    }
}
