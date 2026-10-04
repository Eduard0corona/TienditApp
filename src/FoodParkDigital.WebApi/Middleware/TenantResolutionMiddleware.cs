using System.Diagnostics;
using System.Security.Claims;
using FoodParkDigital.Application.Common.Diagnostics;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.WebApi.Middleware;

public class TenantResolutionMiddleware
{
    // Paths that never resolve a tenant. Matched by prefix so that a route parameter containing one of
    // these fragments cannot skip tenant resolution (the previous check used Contains).
    private static readonly string[] ExcludedPathPrefixes =
    [
        "/api/v1/auth/login",
        "/api/v1/auth/refresh-token",
        "/api/v1/auth/register",
        "/api/v1/whatsapp/webhook",
        "/swagger",
        "/health",
        "/hubs/"
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware>? logger = null)
    {
        _next = next;
        _logger = logger ?? NullLogger<TenantResolutionMiddleware>.Instance;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentTenantService tenantService,
        ITenantAccessService tenantAccessService,
        CurrentAccessContext? accessContext = null)
    {
        // Skip for unauthenticated paths
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (IsExcludedPath(path))
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
            // Cross-tenant access must be explicit, authorized and audited (AGENTS.md).
            if (overrideTenantId != tenantId)
            {
                _logger.LogInformation(
                    "Audit SuperAdminTenantOverride: user {UserId} acting on tenant {TenantId} (token tenant {TokenTenantId}) {Method} {Path}",
                    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    overrideTenantId,
                    tenantId,
                    context.Request.Method,
                    context.Request.Path.Value);
            }

            tenantId = overrideTenantId;
        }

        TenantAccessSnapshot? accessSnapshot = null;
        if (tenantScopedPath && role != "SUPER_ADMIN")
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 403,
                    Title = "Tenant access denied",
                    Detail = "No se pudo determinar el usuario autenticado."
                });
                return;
            }

            accessSnapshot = await tenantAccessService.GetAccessSnapshotAsync(userId, tenantId, context.RequestAborted);
            // The authorization behavior reuses it instead of reading it again.
            accessContext?.Prime(userId, tenantId, accessSnapshot);
            if (!accessSnapshot.HasActiveAccess)
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
            var requiredPermissions = GetPermissionRequirements(path, context.Request.Method);
            if (requiredPermissions.Count > 0)
            {
                var userPermissions = accessSnapshot?.Permissions ?? [];

                var hasPermission = requiredPermissions.Any(required =>
                    userPermissions.Contains(required.ToString(), StringComparer.OrdinalIgnoreCase));

                if (!hasPermission)
                {
                    var expectedPermissions = string.Join(" | ", requiredPermissions.Select(x => x.ToString()));
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = 403,
                        Title = "Permission denied",
                        Detail = $"El usuario no tiene permisos requeridos ({expectedPermissions}) para acceder a '{path}'."
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
                var tenantCapabilities = accessSnapshot?.NormalizedCapabilities ?? TenantCapability.None;
                var hasCapability = requirement.Mode switch
                {
                    CapabilityMatchMode.All => (tenantCapabilities & requirement.Value) == requirement.Value,
                    _ => (tenantCapabilities & requirement.Value) != 0
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
        TagTelemetry(context, tenantId);
        await _next(context);
    }

    /// <summary>
    /// Adds the tenant to the request trace and to the built-in HTTP metrics (http.server.request.duration),
    /// so error rates and latency can be charted and alerted per business.
    /// </summary>
    private static void TagTelemetry(HttpContext context, Guid tenantId)
    {
        var tenant = tenantId.ToString();
        context.Features.Get<IHttpMetricsTagsFeature>()?.Tags.Add(
            new KeyValuePair<string, object?>(TienditAppMetrics.TenantTag, tenant));
        Activity.Current?.SetTag(TienditAppMetrics.TenantTag, tenant);
    }

    private static bool IsExcludedPath(string path) =>
        ExcludedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));

    // /api/v1/tenants/{tenantId}/orders[...]
    private static bool IsTenantOrdersPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 5
            && segments[0] == "api"
            && segments[1] == "v1"
            && segments[2] == "tenants"
            && Guid.TryParse(segments[3], out _)
            && segments[4] == "orders";
    }

    private static bool IsTenantScopedPath(string path) =>
        path.StartsWith("/api/v1/tenants/", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/marketing", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/messaging", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/auth/my-tenant", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/branches", StringComparison.Ordinal)
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
        || path.StartsWith("/api/v1/tables", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/service-board", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/dashboard", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/audit", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/whatsapp/chat", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/auth/my-tenant/whatsapp", StringComparison.Ordinal);

    private static CapabilityRequirement? GetCapabilityRequirement(string path)
    {
        if (path.StartsWith("/api/v1/branches", StringComparison.Ordinal))
            return new CapabilityRequirement(TenantCapability.Inventory, CapabilityMatchMode.All);

        if (path.StartsWith("/api/v1/inventory", StringComparison.Ordinal))
            return new CapabilityRequirement(TenantCapability.Inventory, CapabilityMatchMode.All);

        if (path.StartsWith("/api/v1/kitchen", StringComparison.Ordinal))
            return new CapabilityRequirement(TenantCapability.Kitchen, CapabilityMatchMode.All);

        if (path.StartsWith("/api/v1/tables", StringComparison.Ordinal))
            return new CapabilityRequirement(TenantCapability.Food, CapabilityMatchMode.All);

        if (path.StartsWith("/api/v1/service-board", StringComparison.Ordinal))
            return new CapabilityRequirement(TenantCapability.Service, CapabilityMatchMode.All);

        if (path.StartsWith("/api/v1/orders", StringComparison.Ordinal)
            || IsTenantOrdersPath(path)
            || path.StartsWith("/api/v1/marketing", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/messaging", StringComparison.Ordinal)
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

    private static IReadOnlyCollection<TenantPermission> GetPermissionRequirements(string path, string method)
    {
        var isReadRequest =
            HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method);

        if (path.StartsWith("/api/v1/employees", StringComparison.Ordinal))
            return [TenantPermission.ManageEmployees];

        if (path.StartsWith("/api/v1/branches", StringComparison.Ordinal) && isReadRequest)
        {
            // Cash module needs active branches to register payments/cuts by branch.
            return [TenantPermission.ManageInventory, TenantPermission.ManageCash];
        }

        if (path.StartsWith("/api/v1/branches", StringComparison.Ordinal))
            return [TenantPermission.ManageInventory];

        if (path.StartsWith("/api/v1/inventory/locations", StringComparison.Ordinal) && isReadRequest)
        {
            // Cash module needs active locations to register payments/cuts by branch.
            return [TenantPermission.ManageInventory, TenantPermission.ManageCash];
        }

        if (path.StartsWith("/api/v1/inventory", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/suppliers", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/purchaseorders", StringComparison.Ordinal))
            return [TenantPermission.ManageInventory];

        if (path.StartsWith("/api/v1/orders", StringComparison.Ordinal) || IsTenantOrdersPath(path))
            return [TenantPermission.ManageOrders];

        if (path.StartsWith("/api/v1/marketing", StringComparison.Ordinal))
            return [TenantPermission.ManageWhatsApp];

        // The dining room is read by whoever takes orders; tables are configured with the catalog.
        if (path.StartsWith("/api/v1/tables", StringComparison.Ordinal))
            return isReadRequest ? [TenantPermission.ManageOrders, TenantPermission.ManageCatalog] : [TenantPermission.ManageCatalog];

        // Recording a customer's opt-out is also part of attending them.
        if (path.StartsWith("/api/v1/messaging/opt-outs", StringComparison.Ordinal))
            return [TenantPermission.ManageCampaigns, TenantPermission.ManageCustomers];

        if (path.StartsWith("/api/v1/messaging", StringComparison.Ordinal))
            return [TenantPermission.ManageCampaigns];

        if (path.StartsWith("/api/v1/service-board", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/kitchen", StringComparison.Ordinal))
            return [TenantPermission.ManageServiceBoard];

        if (path.StartsWith("/api/v1/products", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/categories", StringComparison.Ordinal))
        {
            if (isReadRequest)
            {
                // Catalog read is needed by other modules (orders, inventory, appointments).
                return
                [
                    TenantPermission.ManageCatalog,
                    TenantPermission.ManageOrders,
                    TenantPermission.ManageInventory,
                    TenantPermission.ManageAppointments
                ];
            }

            return [TenantPermission.ManageCatalog];
        }

        // The customer card is read from the chat and the orders screen too.
        if (path.StartsWith("/api/v1/customers/card", StringComparison.Ordinal) && isReadRequest)
            return [TenantPermission.ManageCustomers, TenantPermission.ManageWhatsApp, TenantPermission.ManageOrders];

        if (path.StartsWith("/api/v1/customers", StringComparison.Ordinal))
            return [TenantPermission.ManageCustomers];

        if (path.StartsWith("/api/v1/cash", StringComparison.Ordinal))
            return [TenantPermission.ManageCash];

        if (path.StartsWith("/api/v1/reports", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/dashboard", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/audit", StringComparison.Ordinal))
            return [TenantPermission.ViewReports];

        if (path.StartsWith("/api/v1/whatsapp/chat", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/auth/my-tenant/whatsapp", StringComparison.Ordinal))
            return [TenantPermission.ManageWhatsApp];

        if (path.StartsWith("/api/v1/ai-agents", StringComparison.Ordinal))
            return [TenantPermission.ManageAiAgent];

        if (path.StartsWith("/api/v1/appointments", StringComparison.Ordinal))
            return [TenantPermission.ManageAppointments];

        return [];
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
