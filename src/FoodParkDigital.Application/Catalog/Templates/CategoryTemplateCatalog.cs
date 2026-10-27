using FoodParkDigital.Application.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Catalog.Templates;

public sealed class CategoryTemplateDefinition
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Description { get; init; } = null!;
    public BusinessType? BusinessType { get; init; }
    public IReadOnlyList<CategoryTemplateItemDefinition> Categories { get; init; } = [];

    /// <summary>The giros of the sign-up list it is made for; it is the one recommended to them.</summary>
    public IReadOnlyList<string> Industries { get; init; } = [];

    public bool IsMadeFor(string? industry) => Industries.Any(giro => BusinessIndustries.IsGiro(industry, giro));
}

public sealed class CategoryTemplateItemDefinition
{
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

public static class CategoryTemplateCatalog
{
    private static readonly IReadOnlyList<CategoryTemplateDefinition> Templates =
    [
        new CategoryTemplateDefinition
        {
            Id = "food",
            Name = "Alimentos",
            Description = "Categorías comunes para restaurantes, cafeterías y dark kitchens.",
            BusinessType = BusinessType.Food,
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Desayunos", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Comidas", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Cenas", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Snacks", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Bebidas", SortOrder = 50 },
                new CategoryTemplateItemDefinition { Name = "Postres", SortOrder = 60 },
                new CategoryTemplateItemDefinition { Name = "Promociones", SortOrder = 70 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "retail",
            Name = "Retail",
            Description = "Categorías base para tienda física o ecommerce con inventario.",
            BusinessType = BusinessType.Retail,
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Novedades", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Mas vendidos", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Temporada", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Accesorios", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Limpieza y cuidado", SortOrder = 50 },
                new CategoryTemplateItemDefinition { Name = "Promociones", SortOrder = 60 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "services",
            Name = "Servicios",
            Description = "Categorías generales para negocios orientados a servicios.",
            BusinessType = BusinessType.Service,
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Servicios basicos", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Servicios premium", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Express", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "A domicilio", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Membresias", SortOrder = 50 },
                new CategoryTemplateItemDefinition { Name = "Promociones", SortOrder = 60 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "laundry",
            Name = "Lavanderia de calzado",
            Description = "Plantilla especializada para limpieza y restauracion de tenis/calzado.",
            BusinessType = BusinessType.Service,
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Lavado basico", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Lavado premium", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Restauracion", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Blanqueamiento", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Impermeabilizado", SortOrder = 50 },
                new CategoryTemplateItemDefinition { Name = "Productos de cuidado", SortOrder = 60 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "barber",
            Name = "Barberia",
            Description = "Plantilla para barberias y salones con enfoque en citas.",
            BusinessType = BusinessType.Service,
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Corte", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Barba", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Paquetes", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Tratamientos", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Productos", SortOrder = 50 },
                new CategoryTemplateItemDefinition { Name = "Promociones", SortOrder = 60 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "medical",
            Name = "Consultorio médico",
            Description = "Categorías para consultorios y clínicas que atienden con cita.",
            BusinessType = BusinessType.Service,
            Industries = ["Consultorio"],
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Consultas", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Procedimientos", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Certificados", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Estudios", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Paquetes", SortOrder = 50 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "dental",
            Name = "Consultorio dental",
            Description = "Categorías para consultorios dentales que atienden con cita.",
            BusinessType = BusinessType.Service,
            Industries = ["Consultorio dental"],
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Consultas", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Limpieza y prevención", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Tratamientos", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Ortodoncia", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Estética dental", SortOrder = 50 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "carpentry",
            Name = "Carpintería",
            Description = "Categorías para talleres de carpintería y trabajos a domicilio.",
            BusinessType = BusinessType.Service,
            Industries = ["Carpintería"],
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Muebles a medida", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Puertas y clósets", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Reparación y restauración", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Instalación", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Mano de obra", SortOrder = 50 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "welding",
            Name = "Herrería y soldadura",
            Description = "Categorías para herrerías y talleres de soldadura.",
            BusinessType = BusinessType.Service,
            Industries = ["Herrería y soldadura"],
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Soldadura", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Protecciones y rejas", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Portones y barandales", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Estructuras", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Mano de obra", SortOrder = 50 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "electrical",
            Name = "Electricista",
            Description = "Categorías para instalaciones y reparaciones eléctricas a domicilio.",
            BusinessType = BusinessType.Service,
            Industries = ["Electricista"],
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Visitas y diagnóstico", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Instalaciones", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Iluminación", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Reparaciones", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Mano de obra", SortOrder = 50 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "plumbing",
            Name = "Plomería",
            Description = "Categorías para plomería a domicilio.",
            BusinessType = BusinessType.Service,
            Industries = ["Plomería"],
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Visitas y diagnóstico", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Fugas", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Destapes", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Instalaciones", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Calentadores y tinacos", SortOrder = 50 },
                new CategoryTemplateItemDefinition { Name = "Mano de obra", SortOrder = 60 }
            ]
        },
        new CategoryTemplateDefinition
        {
            Id = "air-conditioning",
            Name = "Aire acondicionado",
            Description = "Categorías para instalación y mantenimiento de equipos de aire acondicionado.",
            BusinessType = BusinessType.Service,
            Industries = ["Aire acondicionado"],
            Categories =
            [
                new CategoryTemplateItemDefinition { Name = "Mantenimiento", SortOrder = 10 },
                new CategoryTemplateItemDefinition { Name = "Instalación", SortOrder = 20 },
                new CategoryTemplateItemDefinition { Name = "Reparación", SortOrder = 30 },
                new CategoryTemplateItemDefinition { Name = "Carga de gas", SortOrder = 40 },
                new CategoryTemplateItemDefinition { Name = "Visitas y diagnóstico", SortOrder = 50 }
            ]
        }
    ];

    public static IReadOnlyList<CategoryTemplateDefinition> GetAll() => Templates;

    public static CategoryTemplateDefinition? GetById(string templateId)
        => Templates.FirstOrDefault(x =>
            x.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase));
}
