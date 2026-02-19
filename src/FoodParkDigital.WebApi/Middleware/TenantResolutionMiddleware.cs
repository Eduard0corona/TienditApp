using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentTenantService tenantService,
        ITenantAccessService tenantAccessService)
    {
        // Skip for unauthenticated paths
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (path.Contains("/auth/login") ||
            path.Contains("/auth/refresh") ||
            path.Contains("/auth/register") ||
            path.Contains("/whatsapp/webhook") ||
            path.Contains("/swagger") ||
            path.Contains("/health") ||
            path.Contains("/hubs/"))
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var tenantScopedPath = IsTenantScopedPath(path);
        var tenantClaim = context.User.FindFirst("tenant_id");
        if (tenantClaim is null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
        {
            if (!tenantScopedPath)
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 403,
                Title = "Missing tenant context",
                Detail = "The JWT does not contain a valid tenant_id claim."
            });
            return;
        }

        // SUPER_ADMIN can override tenant via header
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        if (role == "SUPER_ADMIN" &&
            context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenantId) &&
            Guid.TryParse(headerTenantId, out var overrideTenantId))
        {
            tenantId = overrideTenantId;
        }

        if (tenantScopedPath && role != "SUPER_ADMIN")
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId) ||
                !await tenantAccessService.HasActiveAccessAsync(userId, tenantId, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 403,
                    Title = "Tenant access denied",
                    Detail = "User does not belong to the requested tenant or membership is disabled."
                });
                return;
            }
        }

        if (tenantScopedPath && role is not ("SUPER_ADMIN" or "TENANT_ADMIN"))
        {
            var requiredPermission = GetPermissionRequirement(path);
            if (requiredPermission.HasValue)
            {
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(userIdClaim, out var userId)
                    || !await tenantAccessService.HasPermissionAsync(
                        userId,
                        tenantId,
                        requiredPermission.Value,
                        context.RequestAborted))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = 403,
                        Title = "Permission denied",
                        Detail = $"El usuario no tiene permiso '{requiredPermission.Value}' para acceder a '{path}'."
                    });
                    return;
                }
            }
        }

        if (tenantScopedPath && role != "SUPER_ADMIN")
        {
            var capabilityRequirement = GetCapabilityRequirement(path);
            if (capabilityRequirement.HasValue)
            {
                var requirement = capabilityRequirement.Value;
                var hasCapability = requirement.Mode switch
                {
                    CapabilityMatchMode.All => await tenantAccessService.HasCapabilityAsync(
                        tenantId, requirement.Value, context.RequestAborted),
                    _ => await tenantAccessService.HasAnyCapabilityAsync(
                        tenantId, requirement.Value, context.RequestAborted)
                };

                if (!hasCapability)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = 403,
                        Title = "Tenant capability denied",
                        Detail = $"El tenant no tiene habilitado el modulo requerido para '{path}'."
                    });
                    return;
                }
            }
        }

        tenantService.SetTenant(tenantId);
        await _next(context);
    }

    private static bool IsTenantScopedPath(string path) =>
        path.StartsWith("/api/v1/tenants/", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/orders", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/products", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/categories", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/customers", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/cash", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/inventory", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/suppliers", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/purchaseorders", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/reports", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/appointments", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/employees", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/ai-agents", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/kitchen", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/service-board", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/dashboard", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/audit", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/whatsapp/chat", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/auth/my-tenant/whatsapp", StringComparison.Ordinal);

    private static CapabilityRequirement? GetCapabilityRequirement(string path)
    {
        if (path.StartsWith("/api/v1/inventory", StringComparison.Ordinal))
            return new CapabilityRequirement(TenantCapability.Inventory, CapabilityMatchMode.All);

        if (path.StartsWith("/api/v1/kitchen", StringComparison.Ordinal))
            return new CapabilityRequirement(TenantCapability.Kitchen, CapabilityMatchMode.All);

        if (path.StartsWith("/api/v1/service-board", StringComparison.Ordinal))
            return new CapabilityRequirement(TenantCapability.Service, CapabilityMatchMode.All);

        if (path.StartsWith("/api/v1/orders", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/products", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/categories", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/customers", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/cash", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/suppliers", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/purchaseorders", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/reports", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/appointments", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/employees", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/dashboard", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/audit", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/ai-agents", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/whatsapp/chat", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/auth/my-tenant/whatsapp", StringComparison.Ordinal))
        {
            return new CapabilityRequirement(
                TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food,
                CapabilityMatchMode.Any);
        }

        return null;
    }

    private static TenantPermission? GetPermissionRequirement(string path)
    {
        if (path.StartsWith("/api/v1/employees", StringComparison.Ordinal))
            return TenantPermission.ManageEmployees;

        if (path.StartsWith("/api/v1/inventory", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/suppliers", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/purchaseorders", StringComparison.Ordinal))
            return TenantPermission.ManageInventory;

        if (path.StartsWith("/api/v1/orders", StringComparison.Ordinal))
            return TenantPermission.ManageOrders;

        if (path.StartsWith("/api/v1/service-board", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/kitchen", StringComparison.Ordinal))
            return TenantPermission.ManageServiceBoard;

        if (path.StartsWith("/api/v1/products", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/categories", StringComparison.Ordinal))
            return TenantPermission.ManageCatalog;

        if (path.StartsWith("/api/v1/customers", StringComparison.Ordinal))
            return TenantPermission.ManageCustomers;

        if (path.StartsWith("/api/v1/cash", StringComparison.Ordinal))
            return TenantPermission.ManageCash;

        if (path.StartsWith("/api/v1/reports", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/dashboard", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/audit", StringComparison.Ordinal))
            return TenantPermission.ViewReports;

        if (path.StartsWith("/api/v1/whatsapp/chat", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/auth/my-tenant/whatsapp", StringComparison.Ordinal))
            return TenantPermission.ManageWhatsApp;

        if (path.StartsWith("/api/v1/ai-agents", StringComparison.Ordinal))
            return TenantPermission.ManageAiAgent;

        if (path.StartsWith("/api/v1/appointments", StringComparison.Ordinal))
            return TenantPermission.ManageAppointments;

        return null;
    }

    private readonly record struct CapabilityRequirement(
        TenantCapability Value,
        CapabilityMatchMode Mode);

    private enum CapabilityMatchMode
    {
        Any = 0,
        All = 1
    }
}
