using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Catalog.Templates;

public sealed class StarterCatalogDefinition
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Description { get; init; } = null!;
    public BusinessType BusinessType { get; init; }
    public IReadOnlyList<StarterCategoryDefinition> Categories { get; init; } = [];

    public int ProductCount => Categories.Sum(category => category.Products.Count);
}

public sealed class StarterCategoryDefinition
{
    public string Name { get; init; } = null!;
    public int SortOrder { get; init; }
    public IReadOnlyList<StarterProductDefinition> Products { get; init; } = [];
}

/// <param name="TaxRate">IVA in percent; null uses the business default (16 % unless changed).</param>
public sealed record StarterProductDefinition(
    string Name,
    decimal Price,
    UnitType Unit = UnitType.Unit,
    decimal? TaxRate = null,
    bool IsTrackable = false,
    bool IsFeatured = false,
    int? PreparationTimeMinutes = null);

/// <summary>
/// Sample catalogs offered by the guided setup so a new business reaches its first sale in minutes.
/// Prices are examples in pesos that the owner edits; the IVA rate follows the usual Mexican rule for
/// each product (prepared food and services 16 %, basic groceries 0 %).
/// </summary>
public static class StarterCatalogTemplates
{
    private static readonly IReadOnlyList<StarterCatalogDefinition> Templates =
    [
        new StarterCatalogDefinition
        {
            Id = "cafeteria",
            Name = "Cafetería",
            Description = "Café, bebidas frías, pan y desayunos.",
            BusinessType = BusinessType.Food,
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Café caliente",
                    SortOrder = 10,
                    Products =
                    [
                        new("Americano", 35m, IsFeatured: true, PreparationTimeMinutes: 3),
                        new("Capuchino", 48m, IsFeatured: true, PreparationTimeMinutes: 4),
                        new("Latte", 52m, PreparationTimeMinutes: 4),
                        new("Chocolate caliente", 45m, PreparationTimeMinutes: 4)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Bebidas frías",
                    SortOrder = 20,
                    Products =
                    [
                        new("Frappé de moka", 65m, PreparationTimeMinutes: 5),
                        new("Té helado", 38m, PreparationTimeMinutes: 2),
                        new("Limonada", 35m, PreparationTimeMinutes: 3)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Panadería",
                    SortOrder = 30,
                    Products =
                    [
                        new("Croissant", 38m),
                        new("Concha", 22m),
                        new("Muffin de arándano", 40m)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Desayunos",
                    SortOrder = 40,
                    Products =
                    [
                        new("Chilaquiles", 95m, PreparationTimeMinutes: 12),
                        new("Molletes", 75m, PreparationTimeMinutes: 10),
                        new("Sándwich de jamón y queso", 70m, PreparationTimeMinutes: 8)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "taqueria",
            Name = "Taquería",
            Description = "Tacos, órdenes, bebidas y extras.",
            BusinessType = BusinessType.Food,
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Tacos",
                    SortOrder = 10,
                    Products =
                    [
                        new("Taco de pastor", 22m, IsFeatured: true, PreparationTimeMinutes: 5),
                        new("Taco de suadero", 22m, PreparationTimeMinutes: 5),
                        new("Taco de bistec", 24m, PreparationTimeMinutes: 5),
                        new("Taco campechano", 25m, PreparationTimeMinutes: 5)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Órdenes",
                    SortOrder = 20,
                    Products =
                    [
                        new("Gringa", 55m, PreparationTimeMinutes: 8),
                        new("Quesadilla", 35m, PreparationTimeMinutes: 6),
                        new("Volcán", 30m, PreparationTimeMinutes: 6)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Bebidas",
                    SortOrder = 30,
                    Products =
                    [
                        new("Refresco", 30m),
                        new("Agua fresca", 25m)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Extras",
                    SortOrder = 40,
                    Products =
                    [
                        new("Guacamole", 30m),
                        new("Cebollitas asadas", 20m)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "comida-a-domicilio",
            Name = "Comida a domicilio",
            Description = "Paquetes, platos fuertes, guarniciones, bebidas y postres para entregar.",
            BusinessType = BusinessType.Food,
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Paquetes",
                    SortOrder = 10,
                    Products =
                    [
                        new("Comida corrida", 95m, IsFeatured: true, PreparationTimeMinutes: 15),
                        new("Paquete familiar", 320m, IsFeatured: true, PreparationTimeMinutes: 25)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Platos fuertes",
                    SortOrder = 20,
                    Products =
                    [
                        new("Pollo en mole", 120m, PreparationTimeMinutes: 15),
                        new("Enchiladas verdes", 95m, PreparationTimeMinutes: 12),
                        new("Milanesa con papas", 110m, PreparationTimeMinutes: 15)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Guarniciones",
                    SortOrder = 30,
                    Products =
                    [
                        new("Arroz", 30m),
                        new("Frijoles", 30m)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Bebidas",
                    SortOrder = 40,
                    Products =
                    [
                        new("Agua fresca de 1 litro", 40m),
                        new("Refresco", 30m)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Postres",
                    SortOrder = 50,
                    Products =
                    [
                        new("Flan", 40m)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "abarrotes",
            Name = "Abarrotes",
            Description = "Básicos con IVA 0 %, bebidas, botanas y limpieza.",
            BusinessType = BusinessType.Retail,
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Básicos",
                    SortOrder = 10,
                    Products =
                    [
                        new("Tortilla de maíz 1 kg", 25m, TaxRate: 0m, IsFeatured: true),
                        new("Leche entera 1 L", 28m, TaxRate: 0m, IsFeatured: true),
                        new("Huevo blanco 12 piezas", 42m, TaxRate: 0m),
                        new("Arroz 1 kg", 32m, TaxRate: 0m),
                        new("Frijol negro 1 kg", 38m, TaxRate: 0m),
                        new("Azúcar estándar 1 kg", 35m, TaxRate: 0m)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Bebidas y botanas",
                    SortOrder = 20,
                    Products =
                    [
                        new("Refresco de cola 600 ml", 20m, IsFeatured: true),
                        new("Papas fritas 45 g", 18m),
                        new("Galletas surtidas", 25m)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Limpieza e higiene",
                    SortOrder = 30,
                    Products =
                    [
                        new("Jabón de tocador", 18m),
                        new("Detergente en polvo 1 kg", 45m),
                        new("Papel higiénico 4 rollos", 38m)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "papeleria",
            Name = "Papelería",
            Description = "Útiles escolares, copias e impresiones.",
            BusinessType = BusinessType.Retail,
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Útiles",
                    SortOrder = 10,
                    Products =
                    [
                        new("Cuaderno profesional", 45m, IsFeatured: true),
                        new("Pluma negra", 10m),
                        new("Lápiz", 6m),
                        new("Juego geométrico", 35m)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Copias e impresiones",
                    SortOrder = 20,
                    Products =
                    [
                        new("Copia blanco y negro", 1.5m, IsFeatured: true),
                        new("Impresión a color", 8m),
                        new("Engargolado", 30m)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "lavanderia",
            Name = "Lavandería",
            Description = "Lavado, planchado y tintorería con seguimiento de prendas.",
            BusinessType = BusinessType.Service,
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Lavado",
                    SortOrder = 10,
                    Products =
                    [
                        new("Lavado por carga", 90m, UnitType.Service, IsTrackable: true),
                        new("Lavado y secado de edredón", 150m, UnitType.Service, IsTrackable: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Planchado y tintorería",
                    SortOrder = 20,
                    Products =
                    [
                        new("Planchado por prenda", 15m, UnitType.Service, IsTrackable: true),
                        new("Tintorería de traje", 180m, UnitType.Service, IsTrackable: true)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "barberia",
            Name = "Barbería",
            Description = "Cortes, barba y tratamientos.",
            BusinessType = BusinessType.Service,
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Servicios",
                    SortOrder = 10,
                    Products =
                    [
                        new("Corte de cabello", 150m, UnitType.Service),
                        new("Corte y barba", 220m, UnitType.Service),
                        new("Arreglo de barba", 100m, UnitType.Service),
                        new("Afeitado clásico", 120m, UnitType.Service)
                    ]
                }
            ]
        }
    ];

    public static IReadOnlyList<StarterCatalogDefinition> GetAll() => Templates;

    public static StarterCatalogDefinition? GetById(string templateId)
        => Templates.FirstOrDefault(x => x.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase));
}
