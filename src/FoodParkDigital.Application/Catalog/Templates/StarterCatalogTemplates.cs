using FoodParkDigital.Application.Tenants;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Catalog.Templates;

public sealed class StarterCatalogDefinition
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Description { get; init; } = null!;
    public BusinessType BusinessType { get; init; }
    public IReadOnlyList<StarterCategoryDefinition> Categories { get; init; } = [];

    /// <summary>The giros of the sign-up list it is made for; the guided setup recommends it to them.</summary>
    public IReadOnlyList<string> Industries { get; init; } = [];

    public int ProductCount => Categories.Sum(category => category.Products.Count);

    public bool IsMadeFor(string? industry) => Industries.Any(giro => BusinessIndustries.IsGiro(industry, giro));
}

public sealed class StarterCategoryDefinition
{
    public string Name { get; init; } = null!;
    public int SortOrder { get; init; }
    public IReadOnlyList<StarterProductDefinition> Products { get; init; } = [];
}

/// <param name="TaxRate">IVA in percent; null uses the business default (16 % unless changed).</param>
/// <param name="PreparationTimeMinutes">For a service, how long it takes: the length of its appointments.</param>
/// <param name="IsTaxExempt">Exempt from IVA ("exento", not 0 %), so it takes no rate.</param>
/// <param name="IsHomeVisit">
/// The visit to check and quote of a trade (Fase 29A, ADR 0053): installed, it becomes the visit of the business unless
/// it already picked one.
/// </param>
public sealed record StarterProductDefinition(
    string Name,
    decimal Price,
    UnitType Unit = UnitType.Unit,
    decimal? TaxRate = null,
    bool IsTrackable = false,
    bool IsFeatured = false,
    int? PreparationTimeMinutes = null,
    bool IsTaxExempt = false,
    bool IsHomeVisit = false);

/// <summary>
/// Sample catalogs offered by the guided setup so a new business reaches its first sale in minutes.
/// Prices are examples in pesos that the owner edits; the IVA rate follows the usual Mexican rule for
/// each product (prepared food and services 16 %, basic groceries 0 %, and exempt the medical services of a
/// doctor or dentist who is a person or a sociedad civil: LIVA art. 15-XIV and its Reglamento, art. 41).
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
            Industries = [BusinessIndustries.HomeDelivery],
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
            Industries = ["Abarrotes"],
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
            Industries = ["Papelería"],
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
            Industries = ["Lavandería"],
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
        },
        new StarterCatalogDefinition
        {
            Id = "consultorio-medico",
            Name = "Consultorio médico",
            Description = "Consultas y procedimientos exentos de IVA, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = ["Consultorio"],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Consultas",
                    SortOrder = 10,
                    Products =
                    [
                        MedicalService("Consulta general", 500m, 30, isFeatured: true),
                        MedicalService("Consulta de seguimiento", 350m, 20),
                        MedicalService("Certificado médico", 300m, 20)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Procedimientos",
                    SortOrder = 20,
                    Products =
                    [
                        MedicalService("Curación", 250m, 20),
                        MedicalService("Aplicación de inyección", 80m, 10),
                        MedicalService("Retiro de puntos", 300m, 20),
                        MedicalService("Toma de glucosa", 80m, 10)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "consultorio-dental",
            Name = "Consultorio dental",
            Description = "Consultas, limpiezas y tratamientos exentos de IVA, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = ["Consultorio dental"],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Consultas y limpieza",
                    SortOrder = 10,
                    Products =
                    [
                        MedicalService("Consulta y diagnóstico", 400m, 30, isFeatured: true),
                        MedicalService("Limpieza dental", 700m, 45, isFeatured: true),
                        MedicalService("Aplicación de flúor", 350m, 20)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Tratamientos",
                    SortOrder = 20,
                    Products =
                    [
                        MedicalService("Resina", 900m, 45),
                        MedicalService("Extracción simple", 800m, 40),
                        MedicalService("Endodoncia", 4500m, 90)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Estética dental",
                    SortOrder = 30,
                    Products =
                    [
                        MedicalService("Blanqueamiento", 3500m, 60)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "carpinteria",
            Name = "Carpintería",
            Description = "Muebles a medida, reparaciones e instalaciones, con seguimiento de cada trabajo del taller.",
            BusinessType = BusinessType.Service,
            Industries = ["Carpintería"],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Muebles a medida",
                    SortOrder = 10,
                    Products =
                    [
                        new("Mueble a medida", 4500m, UnitType.Service, IsTrackable: true, IsFeatured: true),
                        new("Clóset a medida", 9500m, UnitType.Service, IsTrackable: true),
                        new("Puerta de madera a medida", 5500m, UnitType.Service, IsTrackable: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Reparación y restauración",
                    SortOrder = 20,
                    Products =
                    [
                        new("Reparación de silla", 350m, UnitType.Service, IsTrackable: true),
                        new("Restauración de mueble", 1800m, UnitType.Service, IsTrackable: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "A domicilio",
                    SortOrder = 30,
                    Products =
                    [
                        new("Visita para medir y cotizar", 250m, UnitType.Service, PreparationTimeMinutes: 60, IsHomeVisit: true),
                        new("Instalación de puerta", 900m, UnitType.Service, PreparationTimeMinutes: 120),
                        new("Mano de obra por hora", 250m, UnitType.Hour, PreparationTimeMinutes: 60)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "herreria-soldadura",
            Name = "Herrería y soldadura",
            Description = "Soldadura, protecciones, barandales y portones, con seguimiento de cada trabajo del taller.",
            BusinessType = BusinessType.Service,
            Industries = ["Herrería y soldadura"],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Soldadura",
                    SortOrder = 10,
                    Products =
                    [
                        new("Soldadura de pieza", 250m, UnitType.Service, IsTrackable: true, IsFeatured: true),
                        new("Reparación de silla o mesa metálica", 400m, UnitType.Service, IsTrackable: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Fabricación",
                    SortOrder = 20,
                    Products =
                    [
                        new("Protección para ventana", 1800m, UnitType.Service, IsTrackable: true),
                        new("Barandal a medida", 3500m, UnitType.Service, IsTrackable: true),
                        new("Portón a medida", 12000m, UnitType.Service, IsTrackable: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "A domicilio",
                    SortOrder = 30,
                    Products =
                    [
                        new("Visita para medir y cotizar", 250m, UnitType.Service, PreparationTimeMinutes: 60, IsHomeVisit: true),
                        new("Reparación de reja o portón", 800m, UnitType.Service, PreparationTimeMinutes: 120),
                        new("Mano de obra por hora", 300m, UnitType.Hour, PreparationTimeMinutes: 60)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "electricista",
            Name = "Electricista",
            Description = "Visitas, instalaciones y reparaciones eléctricas, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = ["Electricista"],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Visitas",
                    SortOrder = 10,
                    Products =
                    [
                        new("Visita y diagnóstico", 350m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 60, IsHomeVisit: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Instalaciones",
                    SortOrder = 20,
                    Products =
                    [
                        new("Instalación de contacto o apagador", 250m, UnitType.Service, PreparationTimeMinutes: 30),
                        new("Instalación de lámpara", 300m, UnitType.Service, PreparationTimeMinutes: 45),
                        new("Instalación de ventilador de techo", 600m, UnitType.Service, PreparationTimeMinutes: 90),
                        new("Instalación de centro de carga", 1800m, UnitType.Service, PreparationTimeMinutes: 180)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Reparaciones",
                    SortOrder = 30,
                    Products =
                    [
                        new("Reparación de cortocircuito", 650m, UnitType.Service, PreparationTimeMinutes: 90),
                        new("Mano de obra por hora", 300m, UnitType.Hour, PreparationTimeMinutes: 60)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "plomeria",
            Name = "Plomería",
            Description = "Visitas, fugas, destapes e instalaciones, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = ["Plomería"],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Visitas",
                    SortOrder = 10,
                    Products =
                    [
                        new("Visita y diagnóstico", 350m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 60, IsHomeVisit: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Reparaciones",
                    SortOrder = 20,
                    Products =
                    [
                        new("Reparación de fuga", 500m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 60),
                        new("Destape de drenaje", 700m, UnitType.Service, PreparationTimeMinutes: 90),
                        new("Cambio de llave o mezcladora", 400m, UnitType.Service, PreparationTimeMinutes: 45),
                        new("Mano de obra por hora", 300m, UnitType.Hour, PreparationTimeMinutes: 60)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Instalaciones",
                    SortOrder = 30,
                    Products =
                    [
                        new("Instalación de inodoro", 800m, UnitType.Service, PreparationTimeMinutes: 120),
                        new("Instalación de calentador", 1200m, UnitType.Service, PreparationTimeMinutes: 150),
                        new("Instalación de tinaco", 1500m, UnitType.Service, PreparationTimeMinutes: 180)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "aire-acondicionado",
            Name = "Aire acondicionado",
            Description = "Mantenimiento, instalación y reparación de minisplits, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = ["Aire acondicionado"],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Mantenimiento",
                    SortOrder = 10,
                    Products =
                    [
                        new("Mantenimiento preventivo de minisplit", 800m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 90),
                        new("Limpieza profunda de minisplit", 1200m, UnitType.Service, PreparationTimeMinutes: 120)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Instalación",
                    SortOrder = 20,
                    Products =
                    [
                        new("Instalación de minisplit de 1 tonelada", 2800m, UnitType.Service, PreparationTimeMinutes: 240),
                        new("Instalación de minisplit de 2 toneladas", 3500m, UnitType.Service, PreparationTimeMinutes: 300)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Reparación",
                    SortOrder = 30,
                    Products =
                    [
                        new("Visita y diagnóstico", 400m, UnitType.Service, PreparationTimeMinutes: 60, IsHomeVisit: true),
                        new("Carga de gas refrigerante", 1200m, UnitType.Service, PreparationTimeMinutes: 60),
                        new("Reparación de fuga de gas", 1500m, UnitType.Service, PreparationTimeMinutes: 120)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "fumigacion",
            Name = "Fumigación",
            Description = "Inspección, fumigación y control de plagas en casas y negocios, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = [HomeTrades.PestControl],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Visitas",
                    SortOrder = 10,
                    Products =
                    [
                        new("Visita de inspección y cotización", 300m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 60, IsHomeVisit: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Fumigación",
                    SortOrder = 20,
                    Products =
                    [
                        new("Fumigación de casa", 1200m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 120),
                        new("Fumigación de departamento", 900m, UnitType.Service, PreparationTimeMinutes: 90),
                        new("Fumigación de negocio", 1500m, UnitType.Service, PreparationTimeMinutes: 150)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Control de plagas",
                    SortOrder = 30,
                    Products =
                    [
                        new("Control de cucarachas", 800m, UnitType.Service, PreparationTimeMinutes: 60),
                        new("Control de roedores", 1000m, UnitType.Service, PreparationTimeMinutes: 90),
                        new("Tratamiento contra termitas", 2500m, UnitType.Service, PreparationTimeMinutes: 180)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "refrigeracion-linea-blanca",
            Name = "Refrigeración y línea blanca",
            Description = "Visitas y reparación de refrigeradores, lavadoras, secadoras y estufas, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = [HomeTrades.Refrigeration],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Visitas",
                    SortOrder = 10,
                    Products =
                    [
                        new("Visita y diagnóstico", 350m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 60, IsHomeVisit: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Refrigeración",
                    SortOrder = 20,
                    Products =
                    [
                        new("Reparación de refrigerador", 1200m, UnitType.Service, PreparationTimeMinutes: 120),
                        new("Carga de gas a refrigerador", 900m, UnitType.Service, PreparationTimeMinutes: 60),
                        new("Cambio de termostato", 650m, UnitType.Service, PreparationTimeMinutes: 60)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Lavado y cocina",
                    SortOrder = 30,
                    Products =
                    [
                        new("Reparación de lavadora", 900m, UnitType.Service, PreparationTimeMinutes: 90),
                        new("Reparación de secadora", 900m, UnitType.Service, PreparationTimeMinutes: 90),
                        new("Reparación de estufa", 700m, UnitType.Service, PreparationTimeMinutes: 60)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "cerrajeria",
            Name = "Cerrajería",
            Description = "Aperturas, cambio de chapas y cerraduras a domicilio, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = [HomeTrades.Locksmith],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Visitas",
                    SortOrder = 10,
                    Products =
                    [
                        new("Visita y diagnóstico", 250m, UnitType.Service, PreparationTimeMinutes: 30, IsHomeVisit: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Aperturas",
                    SortOrder = 20,
                    Products =
                    [
                        new("Apertura de puerta", 450m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 30),
                        new("Apertura de auto", 600m, UnitType.Service, PreparationTimeMinutes: 45)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Chapas y cerraduras",
                    SortOrder = 30,
                    Products =
                    [
                        new("Cambio de chapa", 550m, UnitType.Service, PreparationTimeMinutes: 45),
                        new("Instalación de cerradura", 700m, UnitType.Service, PreparationTimeMinutes: 60),
                        new("Cambio de combinación", 400m, UnitType.Service, PreparationTimeMinutes: 30)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "jardineria",
            Name = "Jardinería",
            Description = "Corte de pasto, mantenimiento, poda y riego de jardines, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = [HomeTrades.Gardening],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Visitas",
                    SortOrder = 10,
                    Products =
                    [
                        new("Visita para cotizar", 200m, UnitType.Service, PreparationTimeMinutes: 45, IsHomeVisit: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Mantenimiento",
                    SortOrder = 20,
                    Products =
                    [
                        new("Corte de pasto", 350m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 60),
                        new("Mantenimiento de jardín", 600m, UnitType.Service, PreparationTimeMinutes: 120),
                        new("Poda de árbol", 900m, UnitType.Service, PreparationTimeMinutes: 120),
                        new("Mano de obra por hora", 200m, UnitType.Hour, PreparationTimeMinutes: 60)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Instalación",
                    SortOrder = 30,
                    Products =
                    [
                        new("Siembra de plantas", 400m, UnitType.Service, PreparationTimeMinutes: 60),
                        new("Instalación de riego", 2500m, UnitType.Service, PreparationTimeMinutes: 240)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "limpieza-hogar",
            Name = "Limpieza del hogar",
            Description = "Limpieza de casas y departamentos, profunda y de muebles, con su duración para la agenda.",
            BusinessType = BusinessType.Service,
            Industries = [HomeTrades.HomeCleaning],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Visitas",
                    SortOrder = 10,
                    Products =
                    [
                        new("Visita para cotizar", 150m, UnitType.Service, PreparationTimeMinutes: 30, IsHomeVisit: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Limpieza",
                    SortOrder = 20,
                    Products =
                    [
                        new("Limpieza de casa", 700m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 240),
                        new("Limpieza de departamento", 550m, UnitType.Service, PreparationTimeMinutes: 180),
                        new("Limpieza profunda", 1200m, UnitType.Service, PreparationTimeMinutes: 360)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Muebles y alfombras",
                    SortOrder = 30,
                    Products =
                    [
                        new("Lavado de sala", 900m, UnitType.Service, PreparationTimeMinutes: 120),
                        new("Lavado de colchón", 500m, UnitType.Service, PreparationTimeMinutes: 60),
                        new("Lavado de alfombra", 600m, UnitType.Service, PreparationTimeMinutes: 90)
                    ]
                }
            ]
        },
        new StarterCatalogDefinition
        {
            Id = "pintura-impermeabilizacion",
            Name = "Pintura e impermeabilización",
            Description = "Pintura de interiores y fachadas e impermeabilización, con la visita para medir y cotizar.",
            BusinessType = BusinessType.Service,
            Industries = [HomeTrades.Painting],
            Categories =
            [
                new StarterCategoryDefinition
                {
                    Name = "Visitas",
                    SortOrder = 10,
                    Products =
                    [
                        new("Visita para medir y cotizar", 250m, UnitType.Service, IsFeatured: true, PreparationTimeMinutes: 60, IsHomeVisit: true)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Pintura",
                    SortOrder = 20,
                    Products =
                    [
                        new("Pintura de recámara", 1800m, UnitType.Service, PreparationTimeMinutes: 360),
                        new("Pintura de fachada", 4500m, UnitType.Service, PreparationTimeMinutes: 480),
                        new("Mano de obra por hora", 250m, UnitType.Hour, PreparationTimeMinutes: 60)
                    ]
                },
                new StarterCategoryDefinition
                {
                    Name = "Impermeabilización",
                    SortOrder = 30,
                    Products =
                    [
                        new("Impermeabilización de azotea", 3500m, UnitType.Service, PreparationTimeMinutes: 360),
                        new("Reparación de goteras", 900m, UnitType.Service, PreparationTimeMinutes: 120)
                    ]
                }
            ]
        }
    ];

    public static IReadOnlyList<StarterCatalogDefinition> GetAll() => Templates;

    public static StarterCatalogDefinition? GetById(string templateId)
        => Templates.FirstOrDefault(x => x.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase));

    /// <summary>A service of a doctor or dentist: exempt from IVA, and as long as its appointment.</summary>
    private static StarterProductDefinition MedicalService(string name, decimal price, int minutes, bool isFeatured = false)
        => new(name, price, UnitType.Service, IsFeatured: isFeatured, PreparationTimeMinutes: minutes, IsTaxExempt: true);
}
