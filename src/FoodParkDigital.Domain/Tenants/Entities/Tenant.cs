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

    /// <summary>
    /// The wide photo across the top of its online menu (Fase 24E, ADR 0026): the base URL of its stored variants, like
    /// <see cref="LogoUrl"/>; null without one, and the menu shows the colors of the business instead.
    /// </summary>
    public string? CoverImageUrl { get; private set; }

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
        if (text is not null && !IsOneLine(text))
            throw new DomainException("La descripción va en un solo renglón.");

        DirectoryCategory = category;
        PublicDescription = text;
    }

    /// <summary>
    /// Text without a line break of any kind: no control characters (a tab, CR or LF) and no Unicode line or paragraph
    /// separators (U+2028, U+2029), which a card would also show as a new line.
    /// </summary>
    public static bool IsOneLine(string text) => !text.Any(c => char.IsControl(c)
        || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator
            or System.Globalization.UnicodeCategory.ParagraphSeparator);

    public void Update(string name, string contactEmail, string? contactPhone, string? logoUrl)
    {
        Name = name;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        LogoUrl = logoUrl;
    }

    /// <param name="keepDirectoryCategory">
    /// The owner already decided how the business shows in Explorar: a new giro then only suggests a category, also when
    /// the current one is the one the old giro gave.
    /// </param>
    public void SetBusinessInfo(
        string? rfc,
        string? legalName,
        string? industry,
        string? taxRegime,
        Address? address,
        bool keepDirectoryCategory = false)
    {
        // The category in Explorar follows the giro while it is still the one the giro gave and the owner has not
        // decided it (the sign-up and the guided setup write the giro after the business exists).
        if (!keepDirectoryCategory && DirectoryCategory == BusinessCategoryDefaults.Suggest(Industry, BusinessType))
            DirectoryCategory = BusinessCategoryDefaults.Suggest(industry, BusinessType);

        Rfc = rfc;
        LegalName = legalName;
        Industry = industry;
        TaxRegime = taxRegime;
        Address = address;
    }

    /// <summary>The cover of its online menu: the base URL of the variants just stored, or null to remove it.</summary>
    public void SetCoverImage(string? baseUrl) => CoverImageUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl;

    public void SetStatus(TenantStatus status) => Status = status;
}
