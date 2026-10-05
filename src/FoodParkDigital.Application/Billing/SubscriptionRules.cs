using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Billing.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Billing;

/// <summary>
/// What a business's plan allows (modules, branches, users) and how its billing status reaches the business.
/// A business without a plan (created before plans existed) is not limited.
/// </summary>
public static class SubscriptionRules
{
    /// <summary>The plan of the business in scope, or null when it has none.</summary>
    public static Task<Plan?> CurrentPlanAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
        => context.TenantSubscriptions
            .Where(x => x.TenantId == tenantId && x.Status != SubscriptionStatus.Cancelled)
            .Join(context.Plans, s => s.PlanId, p => p.Id, (s, p) => p)
            .FirstOrDefaultAsync(cancellationToken);

    /// <returns>An error for the person when the plan does not include some of the modules; null otherwise.</returns>
    public static async Task<string?> CheckModulesAsync(
        IApplicationDbContext context,
        Guid tenantId,
        TenantCapability requested,
        CancellationToken cancellationToken)
    {
        var plan = await CurrentPlanAsync(context, tenantId, cancellationToken);
        if (plan is null)
            return null;
        var outside = plan.ModulesOutsidePlan(requested);
        return outside == TenantCapability.None
            ? null
            : $"Tu plan {plan.Name} no incluye: {DescribeModules(outside)}.";
    }

    /// <returns>An error when one more active branch would go over the plan.</returns>
    public static async Task<string?> CheckBranchLimitAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await CurrentPlanAsync(context, tenantId, cancellationToken);
        if (plan is null)
            return null;
        var active = await context.Branches.CountAsync(x => x.TenantId == tenantId && x.IsActive, cancellationToken);
        return active < plan.MaxBranches
            ? null
            : $"Tu plan {plan.Name} permite {plan.MaxBranches} {(plan.MaxBranches == 1 ? "sucursal activa" : "sucursales activas")}.";
    }

    /// <returns>An error when one more active member would go over the plan.</returns>
    public static async Task<string?> CheckUserLimitAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await CurrentPlanAsync(context, tenantId, cancellationToken);
        if (plan is null)
            return null;
        // A pending invitation holds its seat: accepting it must not go over the plan.
        var seats = await context.TenantUsers.CountAsync(x => x.TenantId == tenantId && x.Status != TenantUserStatus.Disabled, cancellationToken);
        return seats < plan.MaxUsers
            ? null
            : $"Tu plan {plan.Name} permite {plan.MaxUsers} {(plan.MaxUsers == 1 ? "usuario" : "usuarios")}, contando las invitaciones pendientes.";
    }

    /// <summary>Businesses without access for billing reasons are suspended (unpaid, by the platform) or inactive (cancelled).</summary>
    public static TenantStatus TenantStatusFor(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Suspended => TenantStatus.Suspended,
        SubscriptionStatus.Cancelled => TenantStatus.Inactive,
        _ => TenantStatus.Active
    };

    /// <summary>Keeps only the modules the plan includes in the business's configuration (normalized first).</summary>
    public static void ClampModules(TenantSettings settings, BusinessType businessType, Plan plan)
    {
        var current = TenantCapabilityRules.NormalizeCapabilities(settings.Capabilities, businessType);
        var clamped = plan.ClampModules(current);
        if (clamped != settings.Capabilities)
            settings.UpdateCapabilities(clamped);
    }

    /// <summary>
    /// The trial a business that signs up on its own starts, when the platform has a plan for new businesses; its
    /// starting modules are trimmed to the plan. Null when there is no such plan (the business is not limited).
    /// </summary>
    public static async Task<TenantSubscription?> StartTrialForNewBusinessAsync(
        IApplicationDbContext context,
        Guid tenantId,
        TenantSettings settings,
        BusinessType businessType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var plan = await context.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsDefaultForNewBusinesses && x.IsActive, cancellationToken);
        if (plan is null)
            return null;

        ClampModules(settings, businessType, plan);
        return new TenantSubscription(tenantId, plan.Id, plan.TrialDays, now);
    }

    public static AuditLog Audit(Guid tenantId, string action, string performedBy, object details)
        => new(
            tenantId: tenantId,
            entityName: nameof(TenantSubscription),
            entityId: tenantId.ToString(),
            action: action,
            performedBy: performedBy,
            metadata: JsonSerializer.Serialize(details));

    public static string DescribeModules(TenantCapability modules)
    {
        var names = new List<string>();
        if (modules.HasFlag(TenantCapability.Retail)) names.Add("venta de productos");
        if (modules.HasFlag(TenantCapability.Service)) names.Add("servicios");
        if (modules.HasFlag(TenantCapability.Food)) names.Add("alimentos");
        if (modules.HasFlag(TenantCapability.Inventory)) names.Add("inventario");
        if (modules.HasFlag(TenantCapability.Appointments)) names.Add("agenda");
        if (modules.HasFlag(TenantCapability.Kitchen)) names.Add("cocina");
        return string.Join(", ", names);
    }
}
