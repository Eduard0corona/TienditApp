using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Catalog.Entities;

public class Category : AuditableEntity, ITenantScoped
{
    /// <summary>Archived ones included: the list of a business shows them all, without pages.</summary>
    public const int MaxCategoriesPerTenant = 300;

    private Category() { }

    public Category(Guid tenantId, string name, string? description, int sortOrder = 0)
    {
        TenantId = tenantId;
        Name = name;
        Description = description;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public ICollection<Product> Products { get; private set; } = new List<Product>();

    public void Update(string name, string? description, int sortOrder)
    {
        Name = name;
        Description = description;
        SortOrder = sortOrder;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    /// <summary>
    /// What makes two names the same category of a business: without the spaces at its ends, with one space wherever
    /// there were several (tabs and line breaks too) and without capitals. Accents count: «Café» and «Cafe» are two.
    /// </summary>
    public static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).ToLowerInvariant();
    }
}
