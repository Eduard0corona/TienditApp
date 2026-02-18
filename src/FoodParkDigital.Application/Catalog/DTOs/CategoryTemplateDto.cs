using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Catalog.DTOs;

public class CategoryTemplateItemDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public class CategoryTemplateDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public BusinessType? BusinessType { get; set; }
    public bool IsRecommended { get; set; }
    public int CategoryCount { get; set; }
    public IReadOnlyList<CategoryTemplateItemDto> Categories { get; set; } = [];
}

public class InstallCategoryTemplateResultDto
{
    public string TemplateId { get; set; } = null!;
    public string TemplateName { get; set; } = null!;
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalCount { get; set; }
}
