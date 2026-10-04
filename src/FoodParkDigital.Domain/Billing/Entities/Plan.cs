using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Billing.Entities;

/// <summary>
/// A plan of the platform (not of a business): its monthly price, the modules a business on it may turn on and its
/// limits. Prices and plans are set by the platform admin; nothing is assumed about them.
/// </summary>
public class Plan : AuditableEntity, IAggregateRoot
{
    public const int MaxCodeLength = 30;
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 500;
    public const int MaxTrialDays = 90;
    public const int MaxBranchesLimit = 50;
    public const int MaxUsersLimit = 500;
    public const string DefaultCurrency = "MXN";

    private const TenantCapability CommerceModules = TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food;
    private const TenantCapability KnownModules = CommerceModules
        | TenantCapability.Inventory | TenantCapability.Appointments | TenantCapability.Kitchen;

    private Plan() { }

    public Plan(string code, string name, string? description, decimal monthlyPrice, TenantCapability modules,
        int maxBranches, int maxUsers, int trialDays, int sortOrder)
    {
        IsActive = true;
        Currency = DefaultCurrency;
        Update(code, name, description, monthlyPrice, modules, maxBranches, maxUsers, trialDays, sortOrder);
    }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal MonthlyPrice { get; private set; }
    public string Currency { get; private set; } = DefaultCurrency;
    /// <summary>Modules a business on this plan may turn on.</summary>
    public TenantCapability Modules { get; private set; }
    public int MaxBranches { get; private set; }
    /// <summary>Active members of the business, the owner included.</summary>
    public int MaxUsers { get; private set; }
    /// <summary>Free days a new business gets on this plan.</summary>
    public int TrialDays { get; private set; }
    /// <summary>Businesses that sign up on their own start a trial on this plan.</summary>
    public bool IsDefaultForNewBusinesses { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public void Update(string code, string name, string? description, decimal monthlyPrice, TenantCapability modules,
        int maxBranches, int maxUsers, int trialDays, int sortOrder)
    {
        var normalizedCode = code is null ? string.Empty : NormalizeCode(code);
        if (normalizedCode.Length == 0 || normalizedCode.Length > MaxCodeLength)
            throw new DomainException($"La clave del plan es obligatoria y tiene máximo {MaxCodeLength} caracteres.");
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > MaxNameLength)
            throw new DomainException($"El nombre del plan es obligatorio y tiene máximo {MaxNameLength} caracteres.");
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > MaxDescriptionLength)
            throw new DomainException($"La descripción tiene máximo {MaxDescriptionLength} caracteres.");
        if (monthlyPrice < 0 || decimal.Round(monthlyPrice, 2) != monthlyPrice)
            throw new DomainException("El precio mensual no puede ser negativo y lleva máximo 2 decimales.");
        if ((modules & ~KnownModules) != 0)
            throw new DomainException("El plan incluye un módulo que no existe.");
        if ((modules & CommerceModules) == 0)
            throw new DomainException("El plan debe incluir al menos un tipo de venta: productos, servicios o alimentos.");
        if (maxBranches is < 1 or > MaxBranchesLimit)
            throw new DomainException($"Las sucursales del plan van de 1 a {MaxBranchesLimit}.");
        if (maxUsers is < 1 or > MaxUsersLimit)
            throw new DomainException($"Los usuarios del plan van de 1 a {MaxUsersLimit}.");
        if (trialDays is < 0 or > MaxTrialDays)
            throw new DomainException($"La prueba gratis dura de 0 a {MaxTrialDays} días.");

        Code = normalizedCode;
        Name = trimmedName;
        Description = trimmedDescription;
        MonthlyPrice = monthlyPrice;
        Modules = modules;
        MaxBranches = maxBranches;
        MaxUsers = maxUsers;
        TrialDays = trialDays;
        SortOrder = sortOrder;
    }

    /// <summary>The caller unmarks the previous default plan.</summary>
    public void MarkAsDefaultForNewBusinesses()
    {
        if (!IsActive)
            throw new DomainException("Activa el plan antes de ofrecerlo a los negocios nuevos.");
        IsDefaultForNewBusinesses = true;
    }

    public void UnmarkAsDefaultForNewBusinesses() => IsDefaultForNewBusinesses = false;

    public void Activate() => IsActive = true;

    /// <summary>Businesses already on it keep it; it is no longer offered.</summary>
    public void Deactivate()
    {
        if (IsDefaultForNewBusinesses)
            throw new DomainException("Elige otro plan para los negocios nuevos antes de desactivar este.");
        IsActive = false;
    }

    /// <summary>
    /// The modules a business keeps on this plan: the ones it had that the plan includes. A business always keeps a
    /// way to sell: when none of its kinds of sale is included, it gets the plan's.
    /// </summary>
    public TenantCapability ClampModules(TenantCapability current)
    {
        var kept = current & Modules;
        if ((kept & CommerceModules) == 0)
            kept |= Modules & CommerceModules;
        return kept;
    }

    public TenantCapability ModulesOutsidePlan(TenantCapability requested) => requested & ~Modules;
}
