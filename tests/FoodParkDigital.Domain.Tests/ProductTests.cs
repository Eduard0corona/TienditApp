using System.Reflection;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;

namespace FoodParkDigital.Domain.Tests;

public class ProductTests
{
    // Se comparan todas las propiedades públicas para que un campo nuevo de Product
    // quede cubierto sin tener que actualizar estas pruebas.
    private static readonly string[] PropertiesExpectedToChange =
    [
        nameof(Product.ImageUrl),
        nameof(Product.UpdatedAt),
        nameof(Product.UpdatedBy),
        nameof(Product.Category),
        nameof(Product.DomainEvents)
    ];

    [Fact]
    public void SetImage_ShouldOnlyChangeImageUrl()
    {
        var product = CreateProductWithEveryFieldSet(imageUrl: null);
        var before = Snapshot(product);

        product.SetImage("https://cdn.test/products/nueva");

        Assert.Equal("https://cdn.test/products/nueva", product.ImageUrl);
        Assert.Empty(ChangedProperties(before, product));
    }

    [Fact]
    public void SetImage_WithNull_ShouldOnlyClearImageUrl()
    {
        var product = CreateProductWithEveryFieldSet(imageUrl: "https://cdn.test/products/actual");
        var before = Snapshot(product);

        product.SetImage(null);

        Assert.Null(product.ImageUrl);
        Assert.Empty(ChangedProperties(before, product));
    }

    [Fact]
    public void Update_ShouldNotChangeImageUrl()
    {
        var product = CreateProductWithEveryFieldSet(imageUrl: "https://cdn.test/products/actual");

        product.Update(
            categoryId: Guid.NewGuid(),
            name: "Lavado express",
            description: null,
            priceAmount: 90m,
            priceCurrency: "MXN",
            preparationTimeMinutes: null,
            sku: null,
            barcode: null,
            sortOrder: 0,
            isFeatured: false,
            tags: null,
            compareAtPrice: null,
            taxRate: null,
            calories: null,
            allergens: null,
            stockQuantity: null,
            lowStockThreshold: null,
            maxQuantityPerOrder: null,
            unit: UnitType.Service,
            availableFromTime: null,
            availableToTime: null,
            isTrackable: false,
            notifyCustomer: false);

        Assert.Equal("Lavado express", product.Name);
        Assert.Equal("https://cdn.test/products/actual", product.ImageUrl);
    }

    // Cada campo tiene un valor distinto de su default para que cualquier reseteo sea visible.
    private static Product CreateProductWithEveryFieldSet(string? imageUrl) => new(
        tenantId: Guid.NewGuid(),
        categoryId: Guid.NewGuid(),
        name: "Lavado premium",
        description: "Lavado profundo de calzado",
        priceAmount: 180m,
        priceCurrency: "MXN",
        imageUrl: imageUrl,
        preparationTimeMinutes: 90,
        sku: "LAV-001",
        barcode: "7501234567890",
        sortOrder: 3,
        isFeatured: true,
        tags: "lavado,calzado",
        compareAtPrice: 220m,
        taxRate: 16m,
        calories: 1,
        allergens: "ninguno",
        stockQuantity: 10,
        lowStockThreshold: 2,
        maxQuantityPerOrder: 5,
        unit: UnitType.Service,
        availableFromTime: new TimeSpan(9, 0, 0),
        availableToTime: new TimeSpan(18, 0, 0),
        isTrackable: true,
        notifyCustomer: true);

    private static Dictionary<string, object?> Snapshot(Product product) =>
        typeof(Product)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !PropertiesExpectedToChange.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property.GetValue(product));

    private static List<string> ChangedProperties(Dictionary<string, object?> before, Product after) =>
        Snapshot(after)
            .Where(property => !Equals(before[property.Key], property.Value))
            .Select(property => $"{property.Key}: {before[property.Key] ?? "null"} -> {property.Value ?? "null"}")
            .ToList();
}
