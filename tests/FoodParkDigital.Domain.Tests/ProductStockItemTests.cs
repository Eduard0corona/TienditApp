using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// A product that controls its stock does it through one inventory item: its name and unit, linked to it, and not
/// deactivated or measured differently while the link lasts.
/// </summary>
public class ProductStockItemTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void ForProduct_ShouldBeTheProductsStock_WithItsNameUnitAndCost()
    {
        var item = SupplyItem.ForProduct(TenantId, ProductId, "Refresco 600 ml", UnitType.Unit, "7501055300075", 12.5m, reorderPoint: 6);

        Assert.Equal(ProductId, item.ProductId);
        Assert.Equal("Refresco 600 ml", item.Name);
        Assert.Equal(UnitType.Unit, item.Unit);
        Assert.Equal("7501055300075", item.Barcode);
        Assert.Equal(12.5m, item.DefaultCost);
        Assert.Equal(6, item.ReorderPoint);
        Assert.True(item.IsActive);
    }

    [Theory]
    [InlineData(UnitType.Service)]
    [InlineData(UnitType.Hour)]
    [InlineData(UnitType.Session)]
    public void ForProduct_ShouldRefuseAService(UnitType unit)
    {
        Assert.Throws<DomainException>(() => SupplyItem.ForProduct(TenantId, ProductId, "Corte de cabello", unit, null, 0, 0));
    }

    [Fact]
    public void LinkToProduct_ShouldRefuseAnItemThatIsAnotherProductsStock()
    {
        var item = SupplyItem.ForProduct(TenantId, ProductId, "Refresco", UnitType.Unit, null, 0, 0);

        Assert.Throws<DomainException>(() => item.LinkToProduct(Guid.NewGuid()));
        item.LinkToProduct(ProductId);
        Assert.Equal(ProductId, item.ProductId);
    }

    [Fact]
    public void ProductsStock_ShouldNotBeDeactivatedNorChangeItsUnit_UntilTheProductLetsItGo()
    {
        var item = SupplyItem.ForProduct(TenantId, ProductId, "Queso", UnitType.Kg, null, 0, 0);

        Assert.Throws<DomainException>(() => item.SetActive(false));
        Assert.Throws<DomainException>(() => item.Update("Queso", UnitType.G));

        item.UnlinkFromProduct();
        item.Update("Queso", UnitType.G);
        item.SetActive(false);

        Assert.Null(item.ProductId);
        Assert.Equal(UnitType.G, item.Unit);
        Assert.False(item.IsActive);
    }

    [Fact]
    public void RenameForProduct_ShouldOnlyApplyToAProductsStock()
    {
        var item = SupplyItem.ForProduct(TenantId, ProductId, "Refresco", UnitType.Unit, null, 0, 0);
        item.RenameForProduct("  Refresco de cola  ");
        var supply = new SupplyItem(TenantId, "Harina", UnitType.Kg);

        Assert.Equal("Refresco de cola", item.Name);
        Assert.Throws<DomainException>(() => supply.RenameForProduct("Otra"));
    }
}
