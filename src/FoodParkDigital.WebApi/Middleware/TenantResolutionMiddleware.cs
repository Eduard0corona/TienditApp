using System.Diagnostics;
using System.Security.Claims;
using FoodParkDigital.Application.Audit;
using FoodParkDigital.Application.Common.Diagnostics;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.WebApi.Errors;

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

    // Requests about the account rather than one business: signing in and the profile, the person's businesses,
    // two-step verification, the platform console and the public menu. Every other /api route acts on the business
    // of the token and needs an active membership in it, so a new route is checked unless it is listed here.
    private static readonly string[] AccountPaths =
    [
        "/api/v1/auth",
        "/api/v1/me",
        "/api/v1/twofactor",
        "/api/v1/platform",
        "/api/v1/public",
        "/api/v1/whatsapp/webhook"
    ];

    // The business's own profile, plan and settings, under /auth.
    private const string MyTenantPath = "/api/v1/auth/my-tenant";

    // The platform's list of businesses (GET and POST /api/v1/tenants); /api/v1/tenants/{id}/... is a business.
    private const string PlatformTenantsPath = "/api/v1/tenants";

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
        CurrentAccessContext? accessContext = null,
        ISupportAccessAudit? supportAccessAudit = null)
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

            await ApiProblem.WriteAsync(
                context, StatusCodes.Status403Forbidden, "Elige un negocio para continuar.",
                ApiErrorCodes.TenantRequired, "Missing tenant context");
            return;
        }

        // SUPER_ADMIN can override tenant via header
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        Guid? sessionTenantId = null;
        if (role == "SUPER_ADMIN" &&
            context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenantId) &&
            Guid.TryParse(headerTenantId, out var overrideTenantId))
        {
            // Cross-tenant access must be explicit, authorized and audited (AGENTS.md).
            if (overrideTenantId != tenantId)
            {
                sessionTenantId = tenantId;
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

        // Who may do what is decided by each use case (AuthorizationBehavior); here the business is resolved and the
        // membership in it checked, once per request.
        if (tenantScopedPath && role != "SUPER_ADMIN")
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                await ApiProblem.WriteAsync(
                    context, StatusCodes.Status403Forbidden, "No se pudo determinar el usuario autenticado.",
                    ApiErrorCodes.TenantAccessDenied, "Tenant access denied");
                return;
            }

            var accessSnapshot = await tenantAccessService.GetAccessSnapshotAsync(userId, tenantId, context.RequestAborted);
            // The authorization behavior reuses it instead of reading it again.
            accessContext?.Prime(userId, tenantId, accessSnapshot);
            if (!accessSnapshot.HasActiveAccess)
            {
                await ApiProblem.WriteAsync(
                    context, StatusCodes.Status403Forbidden, "Ya no tienes acceso a este negocio.",
                    ApiErrorCodes.TenantAccessDenied, "Tenant access denied");
                return;
            }

            // A business suspended or cancelled for billing keeps only what explains why (its profile and plan).
            if (accessSnapshot.TenantSuspended && !IsAllowedWhileSuspended(path, context.Request.Method))
            {
                var suspended = ApiProblem.Create(
                    context, StatusCodes.Status403Forbidden,
                    "El negocio está suspendido. Revisa tu plan en Mi negocio o contacta a soporte.",
                    ApiErrorCodes.TenantSuspended, "Tenant suspended");
                suspended.Type = TenantSuspendedProblemType;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(suspended, (System.Text.Json.JsonSerializerOptions?)null, ApiProblem.ContentType);
                return;
            }
        }

        tenantService.SetTenant(tenantId);
        TagTelemetry(context, tenantId);

        // On a business route, the request is recorded in the business it acts on before it runs, and does not run
        // when it cannot be recorded.
        if (sessionTenantId is { } fromTenant && tenantScopedPath
            && !await RecordSupportAccessAsync(context, supportAccessAudit, tenantId, fromTenant))
            return;

        await _next(context);
    }

    private async Task<bool> RecordSupportAccessAsync(
        HttpContext context,
        ISupportAccessAudit? supportAccessAudit,
        Guid tenantId,
        Guid sessionTenantId)
    {
        try
        {
            if (supportAccessAudit is not null)
            {
                if (await supportAccessAudit.RecordAsync(
                        tenantId, sessionTenantId, context.Request.Method, context.Request.Path.Value ?? "", context.RequestAborted))
                    return true;

                await ApiProblem.WriteAsync(
                    context, StatusCodes.Status404NotFound, "El negocio no existe.", ApiErrorCodes.NotFound, "Tenant not found");
                return false;
            }
        }
        catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(
                LogRedaction.RedactDatabaseMessages(ex),
                "Support access to tenant {TenantId} could not be recorded; the request did not run.",
                tenantId);
        }

        await ApiProblem.WriteAsync(
            context, StatusCodes.Status503ServiceUnavailable,
            "No se pudo registrar el acceso de soporte a este negocio. Intenta de nuevo.",
            ApiErrorCodes.ServerError, "Support access not recorded");
        return false;
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

    public const string TenantSuspendedProblemType = "tenant-suspended";

    private static bool IsAllowedWhileSuspended(string path, string method) =>
        HttpMethods.IsGet(method)
        && (path == "/api/v1/auth/my-tenant" || path == "/api/v1/auth/my-tenant/subscription");

    private static bool IsTenantScopedPath(string path) =>
        path.StartsWith("/api/", StringComparison.Ordinal)
        && (IsUnder(path, MyTenantPath)
            || (!AccountPaths.Any(prefix => IsUnder(path, prefix)) && path.TrimEnd('/') != PlatformTenantsPath));

    private static bool IsUnder(string path, string prefix) =>
        path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "/", StringComparison.Ordinal);
}
