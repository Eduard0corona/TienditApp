using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Domain.Catalog.Enums;

namespace FoodParkDigital.Application.Tests;

public class ProductTaxValidationTests
{
    [Theory]
    [InlineData(null, false, true)]
    [InlineData(16, false, true)]
    [InlineData(8, false, true)]
    [InlineData(0, false, true)]
    [InlineData(null, true, true)]
    [InlineData(16, true, false)]
    [InlineData(10, false, false)]
    public void CreateProduct_ShouldAcceptIvaRatesOrExemption(int? rate, bool isExempt, bool valid)
    {
        var result = new CreateProductCommandValidator().Validate(new CreateProductCommand
        {
            CategoryId = Guid.NewGuid(),
            Name = "Producto",
            PriceAmount = 10m,
            PriceCurrency = "MXN",
            TaxRate = (decimal?)rate,
            IsTaxExempt = isExempt
        });

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Services_ShouldAcceptARateOrExemption()
    {
        // Services pay IVA too; before tax policies any rate on a service was rejected.
        var validator = new UpdateProductCommandValidator();

        var withRate = validator.Validate(new UpdateProductCommand
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Consulta",
            PriceAmount = 300m,
            PriceCurrency = "MXN",
            Unit = UnitType.Service,
            TaxRate = 16m
        });
        var exempt = validator.Validate(new UpdateProductCommand
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Consulta médica",
            PriceAmount = 500m,
            PriceCurrency = "MXN",
            Unit = UnitType.Service,
            IsTaxExempt = true
        });

        Assert.True(withRate.IsValid, string.Join("; ", withRate.Errors));
        Assert.True(exempt.IsValid, string.Join("; ", exempt.Errors));
    }
}
