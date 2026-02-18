using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Catalog.Templates;

public sealed class CategoryTemplateDefinition
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Description { get; init; } = null!;
    public BusinessType? BusinessType { get; init; }
    public IReadOnlyList<CategoryTemplateItemDefinition> Categories { get; init; } = [];
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
            Description = "Categorias comunes para restaurantes, cafeterias y dark kitchens.",
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
            Description = "Categorias base para tienda fisica o ecommerce con inventario.",
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
            Description = "Categorias generales para negocios orientados a servicios.",
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
        }
    ];

    public static IReadOnlyList<CategoryTemplateDefinition> GetAll() => Templates;

    public static CategoryTemplateDefinition? GetById(string templateId)
        => Templates.FirstOrDefault(x =>
            x.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase));
}
