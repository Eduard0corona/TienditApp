using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentTenantService tenantService)
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

        var tenantClaim = context.User.FindFirst("tenant_id");
        if (tenantClaim is null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
        {
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

        tenantService.SetTenant(tenantId);
        await _next(context);
    }
}
