using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.DTOs;

/// <summary>Progress of a business through its first steps, for the guided setup and the Inicio checklist.</summary>
public class TenantSetupDto
{
    public BusinessType BusinessType { get; set; }
    public TenantCapability Capabilities { get; set; }
    public bool PricesIncludeTax { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool HasProducts { get; set; }
    public List<TenantSetupStepDto> Steps { get; set; } = new();
    public List<StarterCatalogDto> StarterCatalogs { get; set; } = new();
}

public class TenantSetupStepDto
{
    /// <summary>
    /// salesPolicy, catalog, homeVisits (only with visits to quote on and the agenda module, Fase 29A), cash, firstSale,
    /// team or logo.
    /// </summary>
    public string Key { get; set; } = null!;
    public bool Done { get; set; }
}

public class StarterCatalogDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public BusinessType BusinessType { get; set; }
    public int ProductCount { get; set; }
    public List<string> Categories { get; set; } = new();
    /// <summary>Made for the giro of the business: the guided setup shows it first.</summary>
    public bool IsRecommended { get; set; }
}
