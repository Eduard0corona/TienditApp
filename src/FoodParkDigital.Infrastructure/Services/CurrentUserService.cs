using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace FoodParkDigital.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IBackgroundExecutionContext? _backgroundContext;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        IBackgroundExecutionContext? backgroundContext = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _backgroundContext = backgroundContext;
    }

    // Outside a request (background workers) the user is whoever caused the work, as the worker recorded it.
    public string? UserId => _httpContextAccessor.HttpContext is { } httpContext
        ? httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        : _backgroundContext?.UserId;
    public string? UserName => _httpContextAccessor.HttpContext is { } httpContext
        ? httpContext.User?.FindFirstValue(ClaimTypes.Email)
        : _backgroundContext?.UserName;
    public string? Role => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);
    public string? TenantRole => _httpContextAccessor.HttpContext?.User?.FindFirstValue("tenant_role");
    public IReadOnlyCollection<string> Permissions =>
        _httpContextAccessor.HttpContext?.User?
            .FindAll("perm")
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? [];

    public Guid? TenantId
    {
        get
        {
            var tenantClaim = _httpContextAccessor.HttpContext?.User?.FindFirstValue("tenant_id");
            return Guid.TryParse(tenantClaim, out var tenantId) ? tenantId : null;
        }
    }
}
