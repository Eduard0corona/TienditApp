using System.Text.RegularExpressions;
using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants;

/// <summary>What every new business starts with, whether it comes from the sign-up or from an account that opens another one.</summary>
public static class BusinessProvisioning
{
    /// <summary>A slug for the business's public links (/m/{slug}), unique across the platform.</summary>
    public static async Task<string> UniqueSlugAsync(IApplicationDbContext context, string businessName, CancellationToken cancellationToken)
    {
        var slug = GenerateSlug(businessName);
        if (slug.Length == 0)
            slug = "negocio";

        // Slugs are global: the check must see every business, not only the current one.
        var taken = await context.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Slug == slug, cancellationToken);
        return taken ? $"{slug}-{Guid.NewGuid().ToString()[..6]}" : slug;
    }

    /// <summary>
    /// Adds the business with its settings (prices with IVA included and the modules of its type; a trade receives visits
    /// to quote, ADR 0053), the general public customer, the default branch with its warehouse (delivery only for a
    /// kitchen without premises or a trade without a workshop, see <see cref="BusinessIndustries"/>) and, when the
    /// platform has a plan for new businesses, its trial. The caller saves, inside its transaction.
    /// </summary>
    public static async Task<TenantSettings> AddAsync(
        IApplicationDbContext context,
        Tenant tenant,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A new business starts with prices that include IVA (what its customers read on a menu); the
        // guided setup lets the owner change it before the first sale.
        var settings = new TenantSettings(
            tenant.Id,
            TenantCapabilityDefaults.FromBusinessType(tenant.BusinessType),
            pricesIncludeTax: true);
        // A trade starts receiving visits to quote: without zones and hours it receives none, and its settings say why.
        if (HomeTrades.IsHomeTrade(tenant.Industry))
            settings.StartReceivingHomeVisits();
        var defaultBranch = Branch.CreateDefault(
            tenant.Id,
            BusinessIndustries.OnlyDelivers(tenant.Industry) ? BranchServiceMode.DeliveryOnly : BranchServiceMode.Premises);

        context.Tenants.Add(tenant);
        context.TenantSettings.Add(settings);
        context.Customers.Add(new Customer(
            tenantId: tenant.Id,
            name: "Publico en general",
            phone: null,
            email: null,
            isPublicGeneral: true));
        context.Branches.Add(defaultBranch);
        context.InventoryLocations.Add(new InventoryLocation(
            tenantId: tenant.Id,
            name: "Almacen principal",
            code: "MAIN",
            isDefault: true,
            branchId: defaultBranch.Id));

        // With a plan for new businesses the business starts its trial there (modules trimmed to the plan).
        var trial = await SubscriptionRules.StartTrialForNewBusinessAsync(
            context, tenant.Id, settings, tenant.BusinessType, now, cancellationToken);
        if (trial is not null)
            context.TenantSubscriptions.Add(trial);

        return settings;
    }

    internal static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = Regex.Replace(slug, @"[áàäâ]", "a");
        slug = Regex.Replace(slug, @"[éèëê]", "e");
        slug = Regex.Replace(slug, @"[íìïî]", "i");
        slug = Regex.Replace(slug, @"[óòöô]", "o");
        slug = Regex.Replace(slug, @"[úùüû]", "u");
        slug = Regex.Replace(slug, @"[ñ]", "n");
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"[\s]+", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');
        return slug;
    }
}
