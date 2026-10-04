using System.Diagnostics;
using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using Serilog.Context;

namespace FoodParkDigital.WebApi.Middleware;

/// <summary>
/// Tags the request's trace and every log written while it runs with the business and the user (by id, never
/// by name, email or phone), so errors and slow requests can be filtered per business.
/// </summary>
public sealed class RequestTelemetryEnrichmentMiddleware
{
    public const string TenantTag = "tienditapp.tenant_id";
    public const string UserTag = "enduser.id";

    private readonly RequestDelegate _next;

    public RequestTelemetryEnrichmentMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentTenantService tenantService)
    {
        var tenantId = tenantService.TenantId == Guid.Empty ? null : tenantService.TenantId.ToString();
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var activity = Activity.Current;
        if (tenantId is not null)
            activity?.SetTag(TenantTag, tenantId);
        if (userId is not null)
            activity?.SetTag(UserTag, userId);

        using (LogContext.PushProperty("TenantId", tenantId))
        using (LogContext.PushProperty("UserId", userId))
        {
            await _next(context);
        }
    }
}
