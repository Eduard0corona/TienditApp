using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Services;

public interface IAuthSessionIssuer
{
    /// <summary>
    /// Emite access y refresh token para un usuario que ya completó todos los factores de autenticación.
    /// </summary>
    Task<AuthResponse> IssueAsync(ApplicationUser user, CancellationToken cancellationToken);
}

/// <summary>
/// Sesión común del login con contraseña y del login con 2FA, para que ambos caminos resuelvan
/// el mismo tenant, membresía y permisos.
/// </summary>
public class AuthSessionIssuer : IAuthSessionIssuer
{
    private readonly IApplicationDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITenantAccessService _tenantAccessService;

    public AuthSessionIssuer(
        IApplicationDbContext context,
        IJwtTokenService jwtTokenService,
        ITenantAccessService tenantAccessService)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _tenantAccessService = tenantAccessService;
    }

    public async Task<AuthResponse> IssueAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: login happens before tenant is resolved (no JWT yet); filtered by user explicitly.
        var tenantMembership = await _context.TenantUsers
            .IgnoreQueryFilters()
            .Where(x => x.UserId == user.Id && x.Status == TenantUserStatus.Active)
            .OrderByDescending(x => x.TenantId == user.DefaultTenantId)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantMembership is null && user.DefaultTenantId.HasValue)
        {
            var hasAnyMembershipForTenant = await _context.TenantUsers
                .IgnoreQueryFilters()
                .AnyAsync(x => x.UserId == user.Id && x.TenantId == user.DefaultTenantId.Value, cancellationToken);

            if (!hasAnyMembershipForTenant)
            {
                var role = user.Role == UserRole.TenantAdmin
                    ? TenantUserRole.Admin
                    : TenantUserRole.Staff;

                tenantMembership = new TenantUser(user.DefaultTenantId.Value, user.Id, role, TenantUserStatus.Active);
                _context.TenantUsers.Add(tenantMembership);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        var permissions = tenantMembership is null
            ? []
            : await _tenantAccessService.GetPermissionsAsync(user.Id, tenantMembership.TenantId, cancellationToken);

        var tenantContext = tenantMembership is null
            ? null
            : new TenantTokenContext(
                tenantMembership.TenantId,
                tenantMembership.Role.ToString().ToUpperInvariant(),
                permissions);

        if (user.DefaultTenantId is null && tenantMembership is not null)
            user.DefaultTenantId = tenantMembership.TenantId;

        // A new session: its refresh token rotates on every renewal and its access tokens carry its id.
        var sessionId = Guid.NewGuid();
        var accessToken = _jwtTokenService.GenerateAccessToken(user, sessionId, tenantContext);
        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.Id, sessionId);

        _context.RefreshTokens.Add(refreshToken.Token);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Value,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60),
            RequiresTwoFactor = false,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                Role = user.Role.ToString(),
                TenantId = tenantMembership?.TenantId ?? user.DefaultTenantId,
                IsActive = user.IsActive,
                AvatarUrl = string.IsNullOrEmpty(user.AvatarUrl) ? null : $"{user.AvatarUrl}_md.webp",
                AvatarImages = AvatarImageUrlsDto.FromBaseUrl(user.AvatarUrl)
            }
        };
    }
}
