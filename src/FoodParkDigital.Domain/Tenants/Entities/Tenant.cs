using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants.Entities;

public class Tenant : AuditableEntity
{
    /// <summary>The description on its card in Explorar: one line, short enough to read at a glance.</summary>
    public const int MaxPublicDescriptionLength = 160;

    private Tenant() { }

    public Tenant(string name, string slug, BusinessType businessType, string contactEmail)
    {
        Name = name;
        Slug = slug;
        BusinessType = businessType;
        ContactEmail = contactEmail;
        Status = TenantStatus.Active;
        DirectoryCategory = BusinessCategoryDefaults.Suggest(industry: null, businessType);
    }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public BusinessType BusinessType { get; private set; }
    public TenantStatus Status { get; private set; }
    public string ContactEmail { get; private set; } = null!;
    public string? ContactPhone { get; private set; }
    public string? LogoUrl { get; private set; }

    // MX Business fields
    public string? Rfc { get; private set; }
    public string? LegalName { get; private set; }
    public string? Industry { get; private set; }
    public string? TaxRegime { get; private set; }
    public Address? Address { get; private set; }

    /// <summary>Where Explorar lists the business (ADR 0021): the owner picks it; it starts from the giro or the type.</summary>
    public BusinessCategory DirectoryCategory { get; private set; } = BusinessCategory.Other;

    /// <summary>One line under the name on its card in Explorar ("Tacos al pastor desde 1998"); null without one.</summary>
    public string? PublicDescription { get; private set; }

    public TenantSettings? Settings { get; private set; }

    /// <summary>
    /// How the business shows in Explorar: its category and its description, trimmed (empty removes it). The
    /// description is public text on one line: no line breaks or other control characters.
    /// </summary>
    public void SetDirectoryProfile(BusinessCategory category, string? description)
    {
        if (!Enum.IsDefined(category))
            throw new DomainException("La categoría no existe.");

        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text is { Length: > MaxPublicDescriptionLength })
            throw new DomainException($"La descripción admite como máximo {MaxPublicDescriptionLength} caracteres.");
        if (text is not null && text.Any(char.IsControl))
            throw new DomainException("La descripción va en un solo renglón.");

        DirectoryCategory = category;
        PublicDescription = text;
    }

    public void Update(string name, string contactEmail, string? contactPhone, string? logoUrl)
    {
        Name = name;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        LogoUrl = logoUrl;
    }

    public void SetBusinessInfo(
        string? rfc,
        string? legalName,
        string? industry,
        string? taxRegime,
        Address? address)
    {
        // The category in Explorar follows the giro while it is still the one the giro gave: the owner has not picked
        // another (the sign-up and the guided setup write the giro after the business exists).
        if (DirectoryCategory == BusinessCategoryDefaults.Suggest(Industry, BusinessType))
            DirectoryCategory = BusinessCategoryDefaults.Suggest(industry, BusinessType);

        Rfc = rfc;
        LegalName = legalName;
        Industry = industry;
        TaxRegime = taxRegime;
        Address = address;
    }

    public void SetStatus(TenantStatus status) => Status = status;
}
