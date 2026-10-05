using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Rules;

public static class TenantCapabilityRules
{
    private const TenantCapability CommerceCapabilities =
        TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food;

    public static bool CanManageCatalog(TenantCapability capabilities) =>
        (capabilities & CommerceCapabilities) != 0;

    public static bool CanAccessInventory(TenantCapability capabilities) =>
        (capabilities & TenantCapability.Inventory) == TenantCapability.Inventory;

    public static bool CanAccessKitchen(TenantCapability capabilities) =>
        (capabilities & TenantCapability.Kitchen) == TenantCapability.Kitchen;

    public static bool CanAccessAppointments(TenantCapability capabilities) =>
        (capabilities & TenantCapability.Appointments) == TenantCapability.Appointments;

    public static bool CanManageProductUnit(TenantCapability capabilities, UnitType? unit)
    {
        if (IsServiceUnit(unit))
            return (capabilities & TenantCapability.Service) == TenantCapability.Service;

        return (capabilities & (TenantCapability.Retail | TenantCapability.Food)) != 0;
    }

    public static bool CanCreateOrderType(TenantCapability capabilities, OrderType orderType)
        => orderType switch
        {
            OrderType.Service => (capabilities & TenantCapability.Service) == TenantCapability.Service,
            OrderType.Retail => (capabilities & (TenantCapability.Retail | TenantCapability.Food)) != 0,
            _ => false
        };

    public static async Task<TenantCapability> ResolveCapabilitiesAsync(
        IApplicationDbContext context,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var settingsCapabilities = await context.TenantSettings
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .Select(x => (TenantCapability?)x.Capabilities)
            .FirstOrDefaultAsync(cancellationToken);

        var businessType = await context.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => (BusinessType?)t.BusinessType)
            .FirstOrDefaultAsync(cancellationToken);

        return NormalizeCapabilities(
            settingsCapabilities ?? TenantCapability.None,
            businessType);
    }

    public static TenantCapability NormalizeCapabilities(
        TenantCapability configuredCapabilities,
        BusinessType? businessType)
    {
        if (!businessType.HasValue)
            return configuredCapabilities == TenantCapability.None ? TenantCapability.Inventory : configuredCapabilities;

        var defaults = TenantCapabilityDefaults.FromBusinessType(businessType.Value);
        if (configuredCapabilities == TenantCapability.None)
            return defaults;

        // Legacy compatibility: old rows were initialized to Inventory-only.
        if (configuredCapabilities == TenantCapability.Inventory && defaults != TenantCapability.Inventory)
            return defaults;

        // A business always sells something, like its plan: a configuration without a type of sale keeps the one
        // of its business type, so the modules of a request never depend on one being left on.
        if (!HasCommerce(configuredCapabilities))
            return configuredCapabilities | (defaults & CommerceCapabilities);

        return configuredCapabilities;
    }

    /// <summary>Whether the modules include a type of sale: products, services or food.</summary>
    public static bool HasCommerce(TenantCapability capabilities) => (capabilities & CommerceCapabilities) != 0;

    private static bool IsServiceUnit(UnitType? unit)
        => unit is UnitType.Hour or UnitType.Session or UnitType.Service;
}
