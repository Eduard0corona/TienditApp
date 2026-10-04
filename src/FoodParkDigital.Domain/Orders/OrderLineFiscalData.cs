using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Fiscal;

namespace FoodParkDigital.Domain.Orders;

/// <summary>
/// How a line is measured and invoiced, copied from the product when it is sold so a later change to the
/// product never alters an order already sold.
/// </summary>
public sealed record OrderLineFiscalData(UnitType Unit, string SatProductCode, string SatUnitCode, CfdiTaxObject TaxObject)
{
    /// <summary>A piece invoiced with the generic codes, as every line was before fiscal data existed.</summary>
    public static readonly OrderLineFiscalData Default = new(
        UnitType.Unit,
        SatCatalog.GenericProductCode,
        UnitTypeRules.DefaultSatUnitCode(UnitType.Unit),
        CfdiTaxObject.Subject);

    public static OrderLineFiscalData From(Product product) => new(
        product.Unit ?? UnitType.Unit,
        product.GetSatProductCode(),
        product.GetSatUnitCode(),
        product.TaxObject);
}
