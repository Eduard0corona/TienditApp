using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.DTOs;

public class LogoImageUrlsDto
{
    public string Original { get; set; } = null!;
    public string Large { get; set; } = null!;
    public string Medium { get; set; } = null!;
    public string Small { get; set; } = null!;
    public string Thumbnail { get; set; } = null!;

    public static LogoImageUrlsDto? FromBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return null;

        return new LogoImageUrlsDto
        {
            Original = $"{baseUrl}_original.webp",
            Large = $"{baseUrl}_lg.webp",
            Medium = $"{baseUrl}_md.webp",
            Small = $"{baseUrl}_sm.webp",
            Thumbnail = $"{baseUrl}_xs.webp"
        };
    }
}

public class TenantProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public BusinessType BusinessType { get; set; }
    public TenantStatus Status { get; set; }
    public string ContactEmail { get; set; } = null!;
    public string? ContactPhone { get; set; }
    public string? LogoUrl { get; set; }
    public LogoImageUrlsDto? LogoImages { get; set; }
    public TenantCapability Capabilities { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Sales policy (TenantSettings)
    public bool PricesIncludeTax { get; set; }
    public decimal DefaultTaxRate { get; set; } = 16m;
    public bool AutoCloseCounterSales { get; set; } = true;
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    // MX Business fields
    public string? Rfc { get; set; }
    public string? LegalName { get; set; }
    public string? Industry { get; set; }
    public string? TaxRegime { get; set; }

    // Address
    public string? Street { get; set; }
    public string? ExteriorNumber { get; set; }
    public string? InteriorNumber { get; set; }
    public string? Neighborhood { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
}
